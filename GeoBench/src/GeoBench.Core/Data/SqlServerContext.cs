using Microsoft.EntityFrameworkCore;

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
    }
}
