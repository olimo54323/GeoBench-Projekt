using GeoBench.Core.Benchmark;
using GeoBench.Core.Benchmark.Scenarios;
using GeoBench.Core.Configuration;
using GeoBench.Core.Data;
using GeoBench.Core.Model;
using GeoBench.Core.Processing;
using GeoBench.Core.Storage;
using GeoBench.Core.Validation;
using GeoBench.Tests.Benchmark;

namespace GeoBench.Tests.Integration
{
    /// <summary>
    /// Scenariusz 3: zasięg warstwy z bazy, zapytania z parametrami (po jednym na okno) i NTS dają te same pary.
    /// Siatka działek 100 × 100 m (4 × 4 km) wstawiana w transakcji wycofywanej na końcu.
    /// </summary>
    public abstract class WindowQueryTestsBase
    {
        private const double X0 = 500000, Y0 = 300000;
        private const int Cells = 40;
        private const double Cell = 100;

        protected abstract DatabaseEngine Engine { get; }

        private static async Task SeedAsync(GeoBenchContext context)
        {
            await context.TruncateAsync<Dzialka>();
            for (var i = 0; i < Cells; i++)
                for (var j = 0; j < Cells; j++)
                    context.Dzialki.Add(new Dzialka
                    {
                        IdDzialki = $"d{i:00}_{j:00}",
                        Geom = BenchmarkFixtures.Rectangle(i * Cell, j * Cell, (i + 1) * Cell, (j + 1) * Cell, X0, Y0)
                    });
            await context.SaveChangesAsync();
        }

        [Fact]
        public async Task Extent_CoversSeededGrid()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await SeedAsync(context);

            var extent = await SpatialStore.Create(context).ExtentAsync(SpatialLayer.Dzialki);

            Assert.Equal(X0, extent.MinX, 6);
            Assert.Equal(Y0, extent.MinY, 6);
            Assert.Equal(X0 + Cells * Cell, extent.MaxX, 6);
            Assert.Equal(Y0 + Cells * Cell, extent.MaxY, 6);
        }

        [Theory]
        [InlineData(100)]
        [InlineData(2000)]
        public async Task InDatabase_And_Nts_GiveSamePairs(double size)
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await SeedAsync(context);
            var store = SpatialStore.Create(context);
            IBenchmarkScenario scenario = new WindowQueryScenario(size);
            await scenario.PrepareAsync(store, CancellationToken.None);

            var inDatabase = await new InDatabaseStrategy().RunAsync(scenario, store);
            var nts = await new NtsStrategy(parallel: true).RunAsync(scenario, store);

            Assert.NotEmpty(inDatabase.Pairs);
            var comparison = ResultComparison.Compare(nts.Pairs, inDatabase.Pairs);
            Assert.True(comparison.Equal, comparison.ToString());
            // Każde okno trafia w co najmniej jedną działkę (okna w zasięgu siatki bez dziur)
            Assert.Equal(WindowQueryScenario.WindowCount, inDatabase.Pairs.Select(p => p.IdA).Distinct().Count());
        }
    }

    [Trait("Category", "Integration")]
    public class SqlServerWindowQueryTests : WindowQueryTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.SqlServer;
    }

    [Trait("Category", "Integration")]
    public class PostgresWindowQueryTests : WindowQueryTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.Postgres;
    }
}
