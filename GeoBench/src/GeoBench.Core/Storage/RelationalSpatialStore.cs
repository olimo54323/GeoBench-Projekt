using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using GeoBench.Core.Benchmark;
using GeoBench.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NetTopologySuite.Geometries;

namespace GeoBench.Core.Storage
{
    /// <summary>
    /// Wspólna część magazynów: polecenia ADO.NET na połączeniu kontekstu EF (z jego transakcją, jeśli jest otwarta).
    /// Silnik dostarcza tylko odczyt geometrii, cytowanie nazw i zapytania diagnostyczne.
    /// </summary>
    public abstract class RelationalSpatialStore : ISpatialStore
    {
        /// <summary>Zapytania scenariuszy na pełnych danych trwają dłużej niż domyślne 30 s.</summary>
        protected static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(30);

        protected RelationalSpatialStore(GeoBenchContext context)
        {
            Context = context;
        }

        public GeoBenchContext Context { get; }

        public abstract DatabaseEngine Engine { get; }

        protected abstract string Quote(string identifier);

        /// <summary>Geometria z kolumny w natywnym formacie sterownika.</summary>
        protected abstract Geometry ReadGeometry(DbDataReader reader, int ordinal);

        public abstract Task<QueryDiagnostics> DiagnoseAsync(string sql, CancellationToken cancellationToken = default);

        public abstract Task<IReadOnlyList<KeyValuePair<string, string>>> GetServerInfoAsync(CancellationToken cancellationToken = default);

        public async Task<long> CountAsync(SpatialLayer layer, CancellationToken cancellationToken = default)
        {
            await Context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var command = CreateCommand(
                    $"SELECT COUNT(*) FROM {Quote(layer.Table)} WHERE {Quote(layer.GeometryColumn)} IS NOT NULL");
                return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            }
            finally
            {
                await Context.Database.CloseConnectionAsync();
            }
        }

        /// <summary>Zapytanie zwracające jedną geometrię: obwiednię kolumny tabeli.</summary>
        protected abstract string ExtentSql(string quotedTable, string quotedColumn);

        public async Task<Envelope> ExtentAsync(SpatialLayer layer, CancellationToken cancellationToken = default)
        {
            await Context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var command = CreateCommand(ExtentSql(Quote(layer.Table), Quote(layer.GeometryColumn)));
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0))
                    throw new InvalidOperationException($"Tabela {layer.Table} nie ma geometrii, więc nie ma zasięgu.");
                return ReadGeometry(reader, 0).EnvelopeInternal;
            }
            finally
            {
                await Context.Database.CloseConnectionAsync();
            }
        }

        public async Task<IReadOnlyList<ResultPair>> ExecuteParameterizedAsync(string sql, IReadOnlyList<ParameterSet> sets,
            CancellationToken cancellationToken = default)
        {
            var pairs = new List<ResultPair>();
            if (sets.Count == 0)
                return pairs;

            await Context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var command = CreateCommand(sql);
                var parameters = sets[0].Values.Select(v =>
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@" + v.Key;
                    parameter.DbType = System.Data.DbType.Double;
                    parameter.Value = v.Value;
                    command.Parameters.Add(parameter);
                    return parameter;
                }).ToList();
                // Jak w aplikacji mapowej: plan przygotowany raz, kolejne wywołania zmieniają tylko parametry
                await command.PrepareAsync(cancellationToken);

                foreach (var set in sets)
                {
                    for (var i = 0; i < parameters.Count; i++)
                        parameters[i].Value = set.Values[i].Value;
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                        pairs.Add(new ResultPair(set.Key, ReadId(reader, 0)));
                }
                return pairs;
            }
            finally
            {
                await Context.Database.CloseConnectionAsync();
            }
        }

        /// <summary>Warunek SQL „geometria w kolumnie jest niepoprawna”.</summary>
        protected abstract string InvalidCondition(string quotedColumn);

        public async Task<IReadOnlyList<string>> InvalidIdsAsync(SpatialLayer layer, CancellationToken cancellationToken = default)
        {
            await Context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                var geometry = Quote(layer.GeometryColumn);
                await using var command = CreateCommand(
                    $"SELECT {Quote(layer.IdColumn)} FROM {Quote(layer.Table)} " +
                    $"WHERE {geometry} IS NOT NULL AND {InvalidCondition(geometry)} ORDER BY {Quote(layer.IdColumn)}");
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                var ids = new List<string>();
                while (await reader.ReadAsync(cancellationToken))
                    ids.Add(ReadId(reader, 0));
                return ids;
            }
            finally
            {
                await Context.Database.CloseConnectionAsync();
            }
        }

        public async IAsyncEnumerable<SpatialFeature> ReadFeaturesAsync(SpatialLayer layer,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var command = CreateCommand(
                    $"SELECT {Quote(layer.IdColumn)}, {Quote(layer.GeometryColumn)} FROM {Quote(layer.Table)} " +
                    $"WHERE {Quote(layer.GeometryColumn)} IS NOT NULL");
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    yield return new SpatialFeature(ReadId(reader, 0), ReadGeometry(reader, 1));
            }
            finally
            {
                await Context.Database.CloseConnectionAsync();
            }
        }

        public async Task<IReadOnlyList<ResultPair>> ExecuteScenarioAsync(string sql, CancellationToken cancellationToken = default)
        {
            await Context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var command = CreateCommand(sql);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                return await ReadPairsAsync(reader, cancellationToken);
            }
            finally
            {
                await Context.Database.CloseConnectionAsync();
            }
        }

        /// <summary>Pary z bieżącego zbioru wyników: id_a, id_b i opcjonalna trzecia kolumna z miarą.</summary>
        protected static async Task<List<ResultPair>> ReadPairsAsync(DbDataReader reader, CancellationToken cancellationToken)
        {
            var pairs = new List<ResultPair>();
            var hasMeasure = reader.FieldCount > 2;
            while (await reader.ReadAsync(cancellationToken))
            {
                double? measure = hasMeasure && !reader.IsDBNull(2)
                    ? Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture)
                    : null;
                pairs.Add(new ResultPair(ReadId(reader, 0), ReadId(reader, 1), measure));
            }
            return pairs;
        }

        /// <summary>Polecenie na połączeniu kontekstu, w jego transakcji i z długim limitem czasu.</summary>
        protected DbCommand CreateCommand(string sql)
        {
            var command = Context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = (int)CommandTimeout.TotalSeconds;
            command.Transaction = Context.Database.CurrentTransaction?.GetDbTransaction();
            return command;
        }

        /// <summary>Wiersze (nazwa, wartość) zapytania o ustawienia serwera.</summary>
        protected async Task<IReadOnlyList<KeyValuePair<string, string>>> ReadSettingsAsync(string sql, CancellationToken cancellationToken)
        {
            await Context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var command = CreateCommand(sql);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                var settings = new List<KeyValuePair<string, string>>();
                while (await reader.ReadAsync(cancellationToken))
                    settings.Add(new(ReadId(reader, 0), reader.IsDBNull(1) ? "" : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? ""));
                return settings;
            }
            finally
            {
                await Context.Database.CloseConnectionAsync();
            }
        }

        private static string ReadId(DbDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? "" : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? "";
    }
}
