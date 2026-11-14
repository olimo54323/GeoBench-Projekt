using System.Data.Common;
using System.Globalization;
using GeoBench.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GeoBench.Core.Export
{
    /// <summary>Wariant zapisany w tabeli wyników i liczba jego wierszy.</summary>
    public sealed record ExportVariant(string Variant, long Rows);

    /// <summary>Dane do zapisu: kolumny w wybranej kolejności i wiersze (wartości w typach sterownika, null dla NULL).</summary>
    public sealed record ExportData(IReadOnlyList<ResultColumn> Columns, IReadOnlyList<object?[]> Rows);

    /// <summary>
    /// Odczyt tabel wyników do eksportu przez ADO.NET na połączeniu kontekstu (z jego transakcją, jeśli jest otwarta).
    /// Nazwy tabel i kolumn pochodzą z modelu, więc są cytowane, a wariant jest parametrem.
    /// </summary>
    public sealed class ResultExportReader
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);

        private readonly GeoBenchContext _context;

        public ResultExportReader(GeoBenchContext context)
        {
            _context = context;
        }

        /// <summary>Warianty w tabeli (alfabetycznie) z liczbą wierszy.</summary>
        public async Task<IReadOnlyList<ExportVariant>> VariantsAsync(ResultTable table, CancellationToken cancellationToken = default)
        {
            var name = Quote(ResultTableCatalog.TableName(_context, table));
            var variant = Quote(ResultTableCatalog.VariantColumn);
            await _context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var command = CreateCommand($"SELECT {variant}, COUNT(*) FROM {name} GROUP BY {variant} ORDER BY {variant}");
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                var variants = new List<ExportVariant>();
                while (await reader.ReadAsync(cancellationToken))
                    variants.Add(new ExportVariant(reader.GetString(0), Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture)));
                return variants;
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }
        }

        /// <summary>
        /// Wiersze wariantu z wybranymi kolumnami w podanej kolejności, posortowane po kolumnach tekstowych tabeli
        /// (identyfikatory), żeby kolejne eksporty dawały ten sam plik.
        /// </summary>
        public async Task<ExportData> ReadAsync(ResultTable table, string variant, IReadOnlyList<ResultColumn> columns,
            CancellationToken cancellationToken = default)
        {
            if (columns.Count == 0)
                throw new ArgumentException("Wybierz co najmniej jedną kolumnę.", nameof(columns));

            var all = ResultTableCatalog.Columns(_context, table);
            var unknown = columns.FirstOrDefault(c => !all.Any(a => a.Name == c.Name));
            if (unknown != null)
                throw new ArgumentException($"Tabela nie ma kolumny {unknown.Name}.", nameof(columns));
            var order = all.Where(c => c.ClrType == typeof(string) && c.Name != ResultTableCatalog.VariantColumn).Select(c => Quote(c.Name)).ToList();

            var sql = $"SELECT {string.Join(", ", columns.Select(c => Quote(c.Name)))} " +
                      $"FROM {Quote(ResultTableCatalog.TableName(_context, table))} " +
                      $"WHERE {Quote(ResultTableCatalog.VariantColumn)} = @wariant" +
                      (order.Count > 0 ? $" ORDER BY {string.Join(", ", order)}" : "");

            await _context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var command = CreateCommand(sql);
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@wariant";
                parameter.Value = variant;
                command.Parameters.Add(parameter);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                var rows = new List<object?[]>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new object?[columns.Count];
                    for (var i = 0; i < row.Length; i++)
                        row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    rows.Add(row);
                }
                return new ExportData(columns, rows);
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }
        }

        private string Quote(string identifier) => _context is SqlServerContext
            ? $"[{identifier.Replace("]", "]]")}]"
            : $"\"{identifier.Replace("\"", "\"\"")}\"";

        private DbCommand CreateCommand(string sql)
        {
            var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = (int)CommandTimeout.TotalSeconds;
            command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
            return command;
        }
    }
}
