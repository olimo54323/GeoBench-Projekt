using GeoBench.Core.Benchmark;
using GeoBench.Core.Benchmark.Scenarios;
using GeoBench.Core.Configuration;
using GeoBench.Core.Data;
using GeoBench.Core.Model;
using GeoBench.Core.Processing;
using GeoBench.Core.Storage;
using GeoBench.Tests.Benchmark;

namespace GeoBench.Tests.Integration
{
    /// <summary>
    /// Scenariusz 2 na działkach: po przebudowie indeks istnieje i ma te same parametry co wcześniej.
    /// Tabela opróżniana i wypełniana kilkoma działkami w transakcji wycofywanej na końcu, więc przebudowa
    /// nie dotyka pełnych danych, a wycofanie przywraca też indeks. Wymaga indeksu z importu działek.
    /// </summary>
    public abstract class IndexRebuildTestsBase
    {
        private const double X0 = 500000, Y0 = 300000;

        protected abstract DatabaseEngine Engine { get; }

        private BenchmarkVariant InDatabaseVariant => VariantFactory.All.Single(v => v.Engine == Engine && v.Strategy.InDatabase);

        private static async Task SeedAsync(GeoBenchContext context)
        {
            await context.TruncateAsync<Dzialka>();
            for (var i = 0; i < 20; i++)
                context.Dzialki.Add(new Dzialka { IdDzialki = $"d{i:00}", Geom = BenchmarkFixtures.Rectangle(i * 10, 0, i * 10 + 10, 10, X0, Y0) });
            await context.SaveChangesAsync();
        }

        [Fact]
        public async Task Rebuild_KeepsIndexWithSameParameters()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await SeedAsync(context);
            var store = SpatialStore.Create(context);
            var before = await store.SpatialIndexAsync(SpatialLayer.Dzialki);
            Assert.NotNull(before);
            IBenchmarkScenario scenario = IndexRebuildScenarios.Dzialki;

            var notes = await scenario.PrepareVariantAsync(store, InDatabaseVariant, CancellationToken.None);
            var run = await new InDatabaseStrategy().RunAsync(scenario, store);
            var after = await store.SpatialIndexAsync(SpatialLayer.Dzialki);

            Assert.Empty(run.Pairs);
            Assert.NotNull(after);
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.Parameters.OrderBy(p => p.Key), after.Parameters.OrderBy(p => p.Key));
            Assert.StartsWith($"indeks {before.Name}", Assert.Single(notes));
        }

        [Fact]
        public async Task Nts_BuildsTreeFromStore()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await SeedAsync(context);

            var run = await new NtsStrategy(parallel: false).RunAsync(IndexRebuildScenarios.Dzialki, SpatialStore.Create(context));

            Assert.Equal(new[] { NtsStrategy.ReadPhase, NtsStrategy.IndexPhase }, run.Phases.Select(p => p.Phase));
        }
    }

    [Trait("Category", "Integration")]
    public class SqlServerIndexRebuildTests : IndexRebuildTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.SqlServer;
    }

    [Trait("Category", "Integration")]
    public class PostgresIndexRebuildTests : IndexRebuildTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.Postgres;
    }
}
