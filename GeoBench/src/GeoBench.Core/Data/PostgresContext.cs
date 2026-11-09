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

        protected override string ServerVersionSql =>
            "SELECT current_setting('server_version') || ', PostGIS ' || postgis_lib_version()";

        // GiST nie potrzebuje zasięgu danych; parametr jest wspólny z indeksem siatkowym SQL Server
        protected override string SpatialIndexSql(string table, string column, Envelope extent)
        {
            var index = $"ix_{table}_{column}";
            return $"""
                DROP INDEX IF EXISTS "{index}";
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
