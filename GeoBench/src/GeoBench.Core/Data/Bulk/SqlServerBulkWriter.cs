using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace GeoBench.Core.Data.Bulk
{
    /// <summary>
    /// Zapis masowy do SQL Server przez SqlBulkCopy. Geometria w natywnym formacie binarnym typu geometry
    /// (SqlServerBytesWriter, z SRID). CheckConstraints jest konieczne: bez niego SqlBulkCopy pomija CHECK na SRID.
    /// </summary>
    internal static class SqlServerBulkWriter
    {
        public static async Task WriteAsync<TEntity>(DbContext context, BulkTable<TEntity> table, IReadOnlyCollection<TEntity> rows,
            CancellationToken cancellationToken) where TEntity : class
        {
            if (rows.Count == 0)
                return;

            using var data = ToDataTable(table, rows);
            var connection = (SqlConnection)context.Database.GetDbConnection();
            await context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                var transaction = (SqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction();
                using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.TableLock, transaction)
                {
                    DestinationTableName = $"[{table.Name}]",
                    BatchSize = rows.Count,
                    BulkCopyTimeout = 0
                };
                foreach (var column in table.Columns)
                    bulk.ColumnMappings.Add(column.Name, column.Name);
                await bulk.WriteToServerAsync(data, cancellationToken);
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }
        }

        private static DataTable ToDataTable<TEntity>(BulkTable<TEntity> table, IReadOnlyCollection<TEntity> rows) where TEntity : class
        {
            var geometryWriter = new SqlServerBytesWriter { IsGeography = false };
            var data = new DataTable(table.Name);
            foreach (var column in table.Columns)
                data.Columns.Add(column.Name, IsGeometry(column) ? typeof(byte[]) : column.ClrType);

            var values = new object[table.Columns.Count];
            foreach (var row in rows)
            {
                for (var i = 0; i < values.Length; i++)
                {
                    var value = table.Columns[i].GetValue(row);
                    values[i] = value switch
                    {
                        null => DBNull.Value,
                        Geometry geometry => geometryWriter.Write(geometry),
                        _ => value
                    };
                }
                data.Rows.Add(values);
            }
            return data;
        }

        private static bool IsGeometry(BulkColumn column) => typeof(Geometry).IsAssignableFrom(column.ClrType);
    }
}
