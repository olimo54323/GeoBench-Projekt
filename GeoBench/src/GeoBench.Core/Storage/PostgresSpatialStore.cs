using System.Data.Common;
using GeoBench.Core.Data;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace GeoBench.Core.Storage
{
    /// <summary>Magazyn PostgreSQL: geometria jako typ NTS z pluginu Npgsql, plan z EXPLAIN (ANALYZE, BUFFERS).</summary>
    public sealed class PostgresSpatialStore : RelationalSpatialStore
    {
        private const string ServerInfoSql = """
            SELECT 'wersja' AS nazwa, current_setting('server_version') AS wartosc
            UNION ALL SELECT 'postgis', extversion FROM pg_extension WHERE extname = 'postgis'
            UNION ALL SELECT name, setting || COALESCE(' ' || unit, '')
                FROM pg_settings
                WHERE name IN ('shared_buffers', 'work_mem', 'effective_cache_size', 'max_parallel_workers_per_gather',
                               'max_parallel_workers', 'random_page_cost', 'jit')
            UNION ALL SELECT 'indeks ' || tablename || '.' || indexname, indexdef
                FROM pg_indexes
                WHERE schemaname = current_schema() AND indexdef ILIKE '%USING gist%'
            """;

        public PostgresSpatialStore(PostgresContext context) : base(context)
        {
        }

        public override DatabaseEngine Engine => DatabaseEngine.Postgres;

        protected override string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

        protected override string ExtentSql(string quotedTable, string quotedColumn) =>
            $"SELECT ST_SetSRID(ST_Extent({quotedColumn})::geometry, {GeoBenchContext.Srid}) FROM {quotedTable}";

        protected override string InvalidCondition(string quotedColumn) => $"NOT ST_IsValid({quotedColumn})";

        // Połączenie kontekstu ma plugin NetTopologySuite (UseNetTopologySuite), więc sterownik zwraca typ NTS
        protected override Geometry ReadGeometry(DbDataReader reader, int ordinal) => reader.GetFieldValue<Geometry>(ordinal);

        public override Task<IReadOnlyList<KeyValuePair<string, string>>> GetServerInfoAsync(CancellationToken cancellationToken = default) =>
            ReadSettingsAsync(ServerInfoSql, cancellationToken);

        public override async Task<QueryDiagnostics> DiagnoseAsync(string sql, CancellationToken cancellationToken = default)
        {
            await Context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                // EXPLAIN ANALYZE wykonuje zapytanie naprawdę; czasy planowania i wykonania są w JSON
                await using var command = CreateCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)\n" + sql);
                var plan = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? "";
                return new QueryDiagnostics("json", plan, "");
            }
            finally
            {
                await Context.Database.CloseConnectionAsync();
            }
        }
    }
}
