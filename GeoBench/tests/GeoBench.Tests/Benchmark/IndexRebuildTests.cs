using GeoBench.Core.Benchmark;
using GeoBench.Core.Benchmark.Scenarios;
using GeoBench.Core.Data;
using GeoBench.Core.Processing;
using GeoBench.Core.Storage;

namespace GeoBench.Tests.Benchmark
{
    /// <summary>Scenariusz 2: szablony przebudowy, warianty „nie dotyczy”, przebieg NTS bez obliczenia, DDL z definicji indeksu.</summary>
    public class IndexRebuildTests
    {
        private static readonly Dictionary<string, string> SqlServerParameters = new()
        {
            ["index"] = "six_dzialka_geom", ["table"] = "dzialka", ["column"] = "geom",
            ["xmin"] = "170000.5", ["ymin"] = "130000", ["xmax"] = "870000", ["ymax"] = "790000.25",
            ["level_1"] = "MEDIUM", ["level_2"] = "MEDIUM", ["level_3"] = "MEDIUM", ["level_4"] = "MEDIUM",
            ["cells_per_object"] = "16", ["tessellation_scheme"] = "GEOMETRY_GRID"
        };

        private static readonly Dictionary<string, string> PostgresParameters = new()
        {
            ["index"] = "ix_budynek_geom", ["schema"] = "public", ["table"] = "budynek", ["column"] = "geom",
            ["definition"] = "CREATE INDEX ix_budynek_geom ON public.budynek USING gist (geom)"
        };

        [Fact]
        public void SqlServerTemplate_UsesCurrentGridParameters()
        {
            var sql = IndexRebuildScenario.FillTemplate(ScenarioSql.Load(DatabaseEngine.SqlServer, "02_rebuild_index.sql"), SqlServerParameters);

            Assert.Contains("DROP INDEX [six_dzialka_geom] ON [dzialka];", sql);
            Assert.Contains("BOUNDING_BOX = (170000.5, 130000, 870000, 790000.25)", sql);
            Assert.Contains("LEVEL_4 = MEDIUM", sql);
            Assert.Contains("CELLS_PER_OBJECT = 16", sql);
            Assert.DoesNotContain("{", sql);
        }

        [Fact]
        public void PostgresTemplate_UsesIndexDefinition()
        {
            var sql = IndexRebuildScenario.FillTemplate(ScenarioSql.Load(DatabaseEngine.Postgres, "02_rebuild_index.sql"), PostgresParameters);

            Assert.Contains("DROP INDEX \"public\".\"ix_budynek_geom\";", sql);
            // ScenarioSql.Load usuwa średnik na końcu pliku
            Assert.EndsWith("\nCREATE INDEX ix_budynek_geom ON public.budynek USING gist (geom)", sql.ReplaceLineEndings("\n"));
        }

        [Fact]
        public void Template_WithoutValue_Throws()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                IndexRebuildScenario.FillTemplate("DROP INDEX [{index}] ON [{table}];", new Dictionary<string, string> { ["index"] = "a" }));

            Assert.Contains("{table}", ex.Message);
        }

        [Fact]
        public void ParallelNts_IsNotApplicable()
        {
            IBenchmarkScenario scenario = IndexRebuildScenarios.Budynki;

            var notApplicable = VariantFactory.All.Where(v => scenario.NotApplicable(v) != null).Select(v => v.Code);

            Assert.Equal(new[] { "W3P", "W4P" }, notApplicable);
        }

        [Fact]
        public void Scenarios_HaveIdsLayersAndNoPairs()
        {
            var scenarios = IndexRebuildScenarios.All;

            Assert.Equal(new[] { "02a", "02b", "02c" }, scenarios.Select(s => s.Id));
            Assert.Equal(new[] { SpatialLayer.Budynki, SpatialLayer.Dzialki, SpatialLayer.Strefy }, scenarios.Select(s => s.Layer));
            Assert.All(scenarios.Cast<IBenchmarkScenario>(), s =>
            {
                Assert.False(s.HasResultPairs);
                Assert.False(s.HasResultTable);
                Assert.False(s.HasDiagnostics);
            });
        }

        [Fact]
        public async Task Nts_ReadsAndBuildsIndex_WithoutComputation()
        {
            var store = new FakeSpatialStore(BenchmarkFixtures.Buildings(), BenchmarkFixtures.Zones());

            var run = await new NtsStrategy(parallel: false).RunAsync(IndexRebuildScenarios.Budynki, store);

            Assert.Empty(run.Pairs);
            Assert.Equal(new[] { NtsStrategy.ReadPhase, NtsStrategy.IndexPhase }, run.Phases.Select(p => p.Phase));
        }

        [Fact]
        public async Task InDatabase_ExecutesTemplateFilledFromIndexDefinition()
        {
            // Magazyn testowy udaje PostgreSQL
            var store = new FakeSpatialStore(BenchmarkFixtures.Buildings(), BenchmarkFixtures.Zones())
            {
                Index = new SpatialIndexInfo("ix_budynek_geom", PostgresParameters, 3 * 1024 * 1024, 62222)
            };
            IBenchmarkScenario scenario = IndexRebuildScenarios.Budynki;
            var w2 = VariantFactory.All.Single(v => v.Code == "W2");

            var notes = await scenario.PrepareVariantAsync(store, w2, CancellationToken.None);
            var run = await new InDatabaseStrategy().RunAsync(scenario, store);

            Assert.Empty(run.Pairs);
            var ddl = Assert.Single(store.ExecutedDdl);
            Assert.EndsWith("\nCREATE INDEX ix_budynek_geom ON public.budynek USING gist (geom)", ddl.ReplaceLineEndings("\n"));
            Assert.Contains("3.0 MB", Assert.Single(notes));
        }

        [Fact]
        public async Task InDatabase_WithoutIndex_Throws()
        {
            var store = new FakeSpatialStore(BenchmarkFixtures.Buildings(), BenchmarkFixtures.Zones());
            IBenchmarkScenario scenario = IndexRebuildScenarios.Budynki;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scenario.PrepareVariantAsync(store, VariantFactory.All.Single(v => v.Code == "W2"), CancellationToken.None));

            Assert.Contains("Brak indeksu przestrzennego", ex.Message);
        }
    }
}
