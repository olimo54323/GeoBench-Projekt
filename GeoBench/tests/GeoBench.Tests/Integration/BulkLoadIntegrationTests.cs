using GeoBench.Core.Benchmark;
using GeoBench.Core.Benchmark.Scenarios;
using GeoBench.Core.Configuration;
using GeoBench.Core.Data;
using GeoBench.Core.Model;
using GeoBench.Core.Processing;
using GeoBench.Core.Storage;
using GeoBench.Tests.Benchmark;
using Microsoft.EntityFrameworkCore;

namespace GeoBench.Tests.Integration
{
    /// <summary>
    /// Scenariusz 1 na obu bazach w transakcji wycofywanej na końcu: po dwóch przebiegach ładowania tabela ma te same
    /// budynki z atrybutami, klucze od 1 i indeks przestrzenny.
    /// </summary>
    public abstract class BulkLoadIntegrationTestsBase
    {
        private const double X0 = 500000, Y0 = 300000;

        protected abstract DatabaseEngine Engine { get; }

        private string VariantCode => Engine == DatabaseEngine.SqlServer ? "W1" : "W2";

        [Fact]
        public async Task Load_ReplacesRowsWithSameData_AndRebuildsIndex()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.TruncateAsync<Budynek>();
            context.Budynki.Add(new Budynek { IdBudynku = "b1", Rodzaj = "mieszkalny", Geom = BenchmarkFixtures.Rectangle(0, 0, 10, 10, X0, Y0) });
            context.Budynki.Add(new Budynek { IdBudynku = "b2", Geom = BenchmarkFixtures.Rectangle(20, 0, 30, 10, X0, Y0) });
            context.Budynki.Add(new Budynek { IdBudynku = "b3", Geom = BenchmarkFixtures.LShape(40 + X0, Y0) });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var store = SpatialStore.Create(context);
            var scenario = BulkLoadScenarios.Budynki;
            var notes = await scenario.PrepareVariantAsync(store, VariantFactory.Get(VariantCode), CancellationToken.None);
            Assert.Contains(notes, n => n.Contains("3 wierszy"));

            // Dwa przebiegi jak rozgrzewka i pomiar: drugi zaczyna od danych zapisanych przez pierwszy
            var strategy = new InDatabaseStrategy();
            await strategy.RunAsync(scenario, store);
            var run = await strategy.RunAsync(scenario, store);

            Assert.Empty(run.Pairs);
            var budynki = await context.Budynki.AsNoTracking().OrderBy(b => b.Oid).ToListAsync();
            Assert.Equal(new[] { "b1", "b2", "b3" }, budynki.Select(b => b.IdBudynku));
            Assert.Equal(new[] { 1, 2, 3 }, budynki.Select(b => b.Oid));
            Assert.Equal("mieszkalny", budynki[0].Rodzaj);
            Assert.True(budynki[2].Geom!.EqualsTopologically(BenchmarkFixtures.LShape(40 + X0, Y0)));
            Assert.NotNull(await store.SpatialIndexAsync(SpatialLayer.Budynki));
        }

        [Fact]
        public async Task Prepare_EmptyTable_Throws()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.TruncateAsync<Budynek>();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                BulkLoadScenarios.Budynki.PrepareVariantAsync(SpatialStore.Create(context), VariantFactory.Get(VariantCode), CancellationToken.None));

            Assert.Contains("pusta", ex.Message);
        }
    }

    [Trait("Category", "Integration")]
    public class SqlServerBulkLoadIntegrationTests : BulkLoadIntegrationTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.SqlServer;
    }

    [Trait("Category", "Integration")]
    public class PostgresBulkLoadIntegrationTests : BulkLoadIntegrationTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.Postgres;
    }
}
