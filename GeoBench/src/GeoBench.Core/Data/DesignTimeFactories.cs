using GeoBench.Core.Configuration;
using Microsoft.EntityFrameworkCore.Design;

namespace GeoBench.Core.Data
{
    /// <summary>
    /// Fabryki kontekstów dla narzędzia dotnet ef. Connection string pochodzi z appsettings.json
    /// (poza gitem) albo ze zmiennej środowiskowej ConnectionStrings__SqlServer / ConnectionStrings__Postgres.
    /// </summary>
    public class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<SqlServerContext>
    {
        public SqlServerContext CreateDbContext(string[] args)
        {
            var configuration = GeoBenchConfiguration.Build();
            var connectionString = GeoBenchConfiguration.RequireConnectionString(configuration, GeoBenchConfiguration.SqlServerConnectionName);
            return new SqlServerContext(SqlServerContext.CreateOptions(connectionString));
        }
    }

    public class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresContext>
    {
        public PostgresContext CreateDbContext(string[] args)
        {
            var configuration = GeoBenchConfiguration.Build();
            var connectionString = GeoBenchConfiguration.RequireConnectionString(configuration, GeoBenchConfiguration.PostgresConnectionName);
            return new PostgresContext(PostgresContext.CreateOptions(connectionString));
        }
    }
}
