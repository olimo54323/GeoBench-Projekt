using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Npgsql;
using NpgsqlTypes;

namespace GeoBench.Core.Data.Bulk
{
    /// <summary>
    /// Zapis masowy do PostgreSQL przez binarny COPY. Typ NTS obsługuje wtyczka NetTopologySuite połączenia
    /// (włączana przez UseNetTopologySuite w opcjach kontekstu). COPY sprawdza ograniczenia, w tym CHECK na SRID.
    /// Połączenie samo używa otwartej transakcji kontekstu.
    /// </summary>
    internal static class PostgresBulkWriter
    {
        public static async Task WriteAsync<TEntity>(DbContext context, BulkTable<TEntity> table, IReadOnlyCollection<TEntity> rows,
            CancellationToken cancellationToken) where TEntity : class
        {
            if (rows.Count == 0)
                return;

            var types = table.Columns.Select(DbType).ToArray();
            var columns = string.Join(", ", table.Columns.Select(c => $"\"{c.Name}\""));
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            await context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var writer = await connection.BeginBinaryImportAsync(
                    $"COPY \"{table.Name}\" ({columns}) FROM STDIN (FORMAT BINARY)", cancellationToken);
                foreach (var row in rows)
                {
                    await writer.StartRowAsync(cancellationToken);
                    for (var i = 0; i < types.Length; i++)
                    {
                        var value = table.Columns[i].GetValue(row);
                        if (value == null)
                            await writer.WriteNullAsync(cancellationToken);
                        else
                            await writer.WriteAsync(value, types[i], cancellationToken);
                    }
                }
                // Bez Complete COPY jest wycofywany przy zamknięciu
                await writer.CompleteAsync(cancellationToken);
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }
        }

        /// <summary>Typ Npgsql kolumny; binarny COPY wymaga formatu zgodnego z typem kolumny.</summary>
        private static NpgsqlDbType DbType(BulkColumn column)
        {
            var type = column.ClrType;
            if (typeof(Geometry).IsAssignableFrom(type))
                return NpgsqlDbType.Geometry;
            if (type == typeof(string))
                return column.StoreType.StartsWith("character varying", StringComparison.OrdinalIgnoreCase) ? NpgsqlDbType.Varchar : NpgsqlDbType.Text;
            if (type == typeof(decimal))
                return NpgsqlDbType.Numeric;
            if (type == typeof(int))
                return NpgsqlDbType.Integer;
            if (type == typeof(long))
                return NpgsqlDbType.Bigint;
            if (type == typeof(bool))
                return NpgsqlDbType.Boolean;
            if (type == typeof(double))
                return NpgsqlDbType.Double;
            if (type == typeof(DateOnly))
                return NpgsqlDbType.Date;
            if (type == typeof(DateTime))
                return column.StoreType.Contains("with time zone", StringComparison.OrdinalIgnoreCase) ? NpgsqlDbType.TimestampTz : NpgsqlDbType.Timestamp;
            throw new NotSupportedException($"Zapis masowy nie obsługuje typu {type.Name} (kolumna {column.Name}).");
        }
    }
}
