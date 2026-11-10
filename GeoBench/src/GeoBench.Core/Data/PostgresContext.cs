using GeoBench.Core.Data.Bulk;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace GeoBench.Core.Data
{
    /// <summary>
    /// Model danych w PostgreSQL + PostGIS (typ geometry z typem OGC i SRID w definicji kolumny).
    /// </summary>
    public class PostgresContext : GeoBenchContext
    {
        public PostgresContext(DbContextOptions<PostgresContext> options) : base(options)
        {
        }

        public static DbContextOptions<PostgresContext> CreateOptions(string connectionString) =>
            new DbContextOptionsBuilder<PostgresContext>()
                .UseNpgsql(connectionString, x => x.UseNetTopologySuite())
                .UseSnakeCaseNamingConvention()
                .Options;

        protected override string GeometryColumnType(string geometryType) => $"geometry({geometryType},{Srid})";

        protected override string SridCheckSql(string column) => $"ST_SRID({column}) = {Srid}";

        // Npgsql mapuje DateTime domyślnie na timestamptz i odrzuca wartości bez strefy (Kind = Unspecified)
        protected override string LocalDateTimeColumnType => "timestamp without time zone";

        // Wersja PostGIS z katalogu rozszerzeń: postgis_lib_version() nie istnieje w bazie przed migracją
        protected override string ServerVersionSql =>
            "SELECT current_setting('server_version') || " +
            "COALESCE(', PostGIS ' || (SELECT extversion FROM pg_extension WHERE extname = 'postgis'), ', bez PostGIS')";

        // GiST nie potrzebuje zasięgu danych; parametr jest wspólny z indeksem siatkowym SQL Server
        private static string SpatialIndexName(string table, string column) => $"ix_{table}_{column}";

        protected override string DropSpatialIndexSql(string table, string column) =>
            $"""DROP INDEX IF EXISTS "{SpatialIndexName(table, column)}";""";

        protected override string TruncateSql(string table) => $"""TRUNCATE TABLE "{table}" RESTART IDENTITY;""";

        public override Task BulkInsertAsync<TEntity>(BulkTable<TEntity> table, IReadOnlyCollection<TEntity> rows,
            CancellationToken cancellationToken = default) =>
            PostgresBulkWriter.WriteAsync(this, table, rows, cancellationToken);

        protected override string SpatialIndexSql(string table, string column, Envelope extent)
        {
            var index = SpatialIndexName(table, column);
            return $"""
                {DropSpatialIndexSql(table, column)}
                CREATE INDEX "{index}" ON "{table}" USING GIST ("{column}");
                ANALYZE "{table}";
                """;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasPostgresExtension("postgis");
            base.OnModelCreating(modelBuilder);
        }
    }
}
