using GeoBench.Core.Data.Bulk;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace GeoBench.Core.Data
{
    /// <summary>
    /// Model danych w SQL Server (typ geometry; SRID pilnuje ograniczenie CHECK, bo kolumna go nie wymusza).
    /// </summary>
    public class SqlServerContext : GeoBenchContext
    {
        public SqlServerContext(DbContextOptions<SqlServerContext> options) : base(options)
        {
        }

        public static DbContextOptions<SqlServerContext> CreateOptions(string connectionString) =>
            new DbContextOptionsBuilder<SqlServerContext>()
                .UseSqlServer(connectionString, x => x.UseNetTopologySuite())
                .UseSnakeCaseNamingConvention()
                .Options;

        protected override string GeometryColumnType(string geometryType) => "geometry";

        protected override string SridCheckSql(string column) => $"[{column}].STSrid = {Srid}";

        protected override string LocalDateTimeColumnType => "datetime2";

        // Edycja ma znaczenie dla pomiarów: Express ogranicza pamięć i liczbę rdzeni
        protected override string ServerVersionSql =>
            // SERVERPROPERTY zwraca sql_variant, którego CONCAT nie zamienia sam na tekst
            "SELECT CONCAT(CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)), N' ', CAST(SERVERPROPERTY('Edition') AS nvarchar(128)))";

        // Parametry siatki trafiają do raportu (warunek uczciwości porównania); na razie wartości domyślne
        // GEOMETRY_GRID, dostrajanie w etapie pomiarów.
        public const string SpatialIndexGrids = "LEVEL_1 = MEDIUM, LEVEL_2 = MEDIUM, LEVEL_3 = MEDIUM, LEVEL_4 = MEDIUM";
        public const int SpatialIndexCellsPerObject = 16;

        private static string SpatialIndexName(string table, string column) => $"six_{table}_{column}";

        protected override string DropSpatialIndexSql(string table, string column)
        {
            var index = SpatialIndexName(table, column);
            return $"""
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{index}' AND object_id = OBJECT_ID(N'{table}'))
                    DROP INDEX [{index}] ON [{table}];
                """;
        }

        // TRUNCATE zeruje też licznik IDENTITY
        protected override string TruncateSql(string table) => $"TRUNCATE TABLE [{table}];";

        public override Task BulkInsertAsync<TEntity>(BulkTable<TEntity> table, IReadOnlyCollection<TEntity> rows,
            CancellationToken cancellationToken = default) =>
            SqlServerBulkWriter.WriteAsync(this, table, rows, cancellationToken);

        protected override string SpatialIndexSql(string table, string column, Envelope extent)
        {
            var index = SpatialIndexName(table, column);
            return $"""
                {DropSpatialIndexSql(table, column)}
                CREATE SPATIAL INDEX [{index}] ON [{table}] ([{column}]) USING GEOMETRY_GRID
                    WITH (BOUNDING_BOX = ({SqlNumber(extent.MinX)}, {SqlNumber(extent.MinY)}, {SqlNumber(extent.MaxX)}, {SqlNumber(extent.MaxY)}),
                          GRIDS = ({SpatialIndexGrids}), CELLS_PER_OBJECT = {SpatialIndexCellsPerObject});
                UPDATE STATISTICS [{table}];
                """;
        }
    }
}
