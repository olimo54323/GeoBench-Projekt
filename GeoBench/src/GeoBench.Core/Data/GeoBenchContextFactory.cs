using GeoBench.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace GeoBench.Core.Data
{
    /// <summary>Silnik bazy danych, w którym przechowywane są dane (magazyn wariantu).</summary>
    public enum DatabaseEngine
    {
        SqlServer,
        Postgres
    }

    /// <summary>
    /// Tworzy kontekst EF dla wskazanego silnika z connection stringów konfiguracji
    /// (appsettings.json lub zmienne środowiskowe). Wspólna dla aplikacji WPF i testów.
    /// </summary>
    public sealed class GeoBenchContextFactory
    {
        private readonly IConfiguration _configuration;

        public GeoBenchContextFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public static string ConnectionName(DatabaseEngine engine) => engine switch
        {
            DatabaseEngine.SqlServer => GeoBenchConfiguration.SqlServerConnectionName,
            DatabaseEngine.Postgres => GeoBenchConfiguration.PostgresConnectionName,
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
        };

        /// <summary>Czy konfiguracja zawiera connection string dla silnika.</summary>
        public bool IsConfigured(DatabaseEngine engine) =>
            !string.IsNullOrWhiteSpace(_configuration.GetConnectionString(ConnectionName(engine)));

        public GeoBenchContext Create(DatabaseEngine engine)
        {
            var connectionString = GeoBenchConfiguration.RequireConnectionString(_configuration, ConnectionName(engine));
            return engine switch
            {
                DatabaseEngine.SqlServer => new SqlServerContext(SqlServerContext.CreateOptions(connectionString)),
                DatabaseEngine.Postgres => new PostgresContext(PostgresContext.CreateOptions(connectionString)),
                _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
            };
        }
    }
}
