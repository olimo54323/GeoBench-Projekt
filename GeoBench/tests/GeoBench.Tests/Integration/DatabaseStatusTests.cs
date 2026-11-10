using GeoBench.Core.Configuration;
using GeoBench.Core.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace GeoBench.Tests.Integration
{
    /// <summary>
    /// Rozpoznanie stanu bazy na serwerze: istniejąca baza z appsettings.json jest gotowa, a baza o losowej
    /// nazwie na tym samym serwerze to „brak bazy” (nie błąd połączenia). Test niczego nie tworzy.
    /// </summary>
    public abstract class DatabaseStatusTestsBase
    {
        protected abstract DatabaseEngine Engine { get; }

        /// <summary>Connection string z podmienioną nazwą bazy.</summary>
        protected abstract string WithDatabase(string connectionString, string database);

        [Fact]
        public async Task ConfiguredDatabase_IsReady()
        {
            var status = await DatabaseStatus.CheckAsync(new GeoBenchContextFactory(GeoBenchConfiguration.Build()), Engine);

            Assert.True(status.CanConnect, status.Error);
            Assert.True(status.DatabaseExists);
            Assert.Empty(status.PendingMigrations);
            Assert.True(status.IsReady);
            Assert.False(status.CanMigrate);
            Assert.False(string.IsNullOrWhiteSpace(status.ServerVersion));
        }

        [Fact]
        public async Task MissingDatabase_IsReportedAsMissing_NotAsConnectionError()
        {
            var configuration = GeoBenchConfiguration.Build();
            var name = GeoBenchContextFactory.ConnectionName(Engine);
            var missing = $"geobench_brak_{Guid.NewGuid():N}";
            var changed = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"ConnectionStrings:{name}"] = WithDatabase(GeoBenchConfiguration.RequireConnectionString(configuration, name), missing)
                })
                .Build();

            var status = await DatabaseStatus.CheckAsync(new GeoBenchContextFactory(changed), Engine);

            Assert.True(status.CanConnect, status.Error);
            Assert.False(status.DatabaseExists);
            Assert.Equal(missing, status.DatabaseName);
            Assert.NotEmpty(status.PendingMigrations);
            Assert.True(status.CanMigrate);
            Assert.Null(status.Error);
        }
    }

    [Trait("Category", "Integration")]
    public class SqlServerDatabaseStatusTests : DatabaseStatusTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.SqlServer;

        protected override string WithDatabase(string connectionString, string database) =>
            new SqlConnectionStringBuilder(connectionString) { InitialCatalog = database }.ConnectionString;
    }

    [Trait("Category", "Integration")]
    public class PostgresDatabaseStatusTests : DatabaseStatusTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.Postgres;

        protected override string WithDatabase(string connectionString, string database) =>
            new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;
    }
}
