using GeoBench.Core.Benchmark;
using GeoBench.Core.Benchmark.Scenarios;
using GeoBench.Core.Data;
using GeoBench.Core.Processing;
using GeoBench.Core.Storage;

namespace GeoBench.Tests.Benchmark
{
    /// <summary>Scenariusz 1: identyfikatory i warstwy, warianty NTS „nie dotyczy”, brak par, wymaganie przygotowania.</summary>
    public class BulkLoadTests
    {
        [Fact]
        public void Scenarios_HaveIdsLayersAndNoPairs()
        {
            var scenarios = BulkLoadScenarios.All;

            Assert.Equal(new[] { "01a", "01b", "01c" }, scenarios.Select(s => s.Id));
            Assert.Equal(new[] { SpatialLayer.Budynki, SpatialLayer.Dzialki, SpatialLayer.Strefy }, scenarios.Select(s => s.LayerA));
            Assert.All(scenarios, s =>
            {
                Assert.Equal("01_bulk_load.sql", s.SqlFileName);
                Assert.False(s.HasResultPairs);
                Assert.False(s.HasResultTable);
                Assert.False(s.HasDiagnostics);
                Assert.Equal(new[] { s.LayerA }, s.Layers.Distinct());
            });
        }

        [Fact]
        public void OnlyInDatabaseVariants_AreApplicable()
        {
            var scenario = BulkLoadScenarios.Budynki;

            var applicable = VariantFactory.All.Where(v => scenario.NotApplicable(v) == null).Select(v => v.Code);

            Assert.Equal(new[] { "W1", "W2" }, applicable);
        }

        [Theory]
        [InlineData(DatabaseEngine.SqlServer)]
        [InlineData(DatabaseEngine.Postgres)]
        public void SqlFile_IsEmbeddedForBothEngines(DatabaseEngine engine)
        {
            Assert.Contains("SELECT 1", ScenarioSql.Load(engine, "01_bulk_load.sql"));
        }

        [Fact]
        public async Task InDatabase_WithoutPreparation_Throws()
        {
            var store = new FakeSpatialStore(BenchmarkFixtures.Buildings(), BenchmarkFixtures.Zones());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new InDatabaseStrategy().RunAsync(BulkLoadScenarios.Budynki, store));

            Assert.Contains("przygotowania", ex.Message);
        }

        [Fact]
        public async Task NtsVariant_PreparationReturnsNoNotes()
        {
            var store = new FakeSpatialStore(BenchmarkFixtures.Buildings(), BenchmarkFixtures.Zones());

            var notes = await BulkLoadScenarios.Budynki.PrepareVariantAsync(store, VariantFactory.Get("W3"), CancellationToken.None);

            Assert.Empty(notes);
        }
    }
}
