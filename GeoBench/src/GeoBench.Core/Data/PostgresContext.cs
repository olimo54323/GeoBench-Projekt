using Microsoft.EntityFrameworkCore;

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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasPostgresExtension("postgis");
            base.OnModelCreating(modelBuilder);
        }
    }
}
