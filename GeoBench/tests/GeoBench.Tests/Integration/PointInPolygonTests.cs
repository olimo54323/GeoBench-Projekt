using GeoBench.Core.Benchmark;
using GeoBench.Core.Benchmark.Scenarios;
using GeoBench.Core.Configuration;
using GeoBench.Core.Data;
using GeoBench.Core.Model;
using GeoBench.Core.Processing;
using GeoBench.Core.Storage;
using GeoBench.Core.Validation;
using GeoBench.Tests.Benchmark;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace GeoBench.Tests.Integration
{
    /// <summary>
    /// Scenariusz 4 w bazie (SQL z zasobów) i w NTS na tych samych danych daje te same pary.
    /// Tabele są czyszczone i wypełniane w transakcji wycofywanej na końcu testu, więc dane z importu zostają.
    /// Punkt na granicy stref jest sprawdzany tylko w testach jednostkowych: centroid liczony przez silnik
    /// może różnić się od granicy o błąd zaokrąglenia.
    /// </summary>
    public abstract class PointInPolygonTestsBase
    {
        // Przesunięcie do zasięgu PL-1992 (okolice Częstochowy)
        private const double X0 = 500000, Y0 = 300000;

        private static readonly PointInPolygonScenario Scenario = new();

        protected abstract DatabaseEngine Engine { get; }

        private static async Task SeedAsync(GeoBenchContext context)
        {
            await context.TruncateAsync<Budynek>();
            await context.TruncateAsync<StrefaPlanistyczna>();

            var akt = new AktPlanowania
            {
                PrzestrzenNazw = "PL.TEST",
                LokalnyId = $"test-{Guid.NewGuid():N}",
                Tytul = "Plan testowy scenariusza 4",
                DaneBadawcze = true
            };
            // Bez budynku na granicy A|B (patrz opis klasy)
            foreach (var zone in BenchmarkFixtures.Zones(X0, Y0))
                akt.Strefy.Add(new StrefaPlanistyczna
                {
                    Oznaczenie = zone.Id,
                    LokalnyId = $"{akt.LokalnyId}-{zone.Id}",
                    Symbol = "SU",
                    Nazwa = RodzajStrefyPlanistycznej.StrefaUslugowa,
                    Geom = (MultiPolygon)zone.Geometry
                });
            context.AktyPlanowania.Add(akt);
            foreach (var building in BenchmarkFixtures.Buildings(X0, Y0).Where(b => b.Id != "granica"))
                context.Budynki.Add(new Budynek { IdBudynku = building.Id, Geom = (MultiPolygon)building.Geometry });
            await context.SaveChangesAsync();
        }

        [Fact]
        public async Task InDatabase_And_Nts_GiveSamePairs()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await SeedAsync(context);
            var store = SpatialStore.Create(context);

            var inDatabase = await new InDatabaseStrategy().RunAsync(Scenario, store);
            var nts = await new NtsStrategy(parallel: false).RunAsync(Scenario, store);

            var expected = new[] { new ResultPair("L", "N"), new ResultPair("srodek", "A") };
            Assert.True(ResultComparison.Compare(expected, inDatabase.Pairs).Equal, string.Join(", ", inDatabase.Pairs));
            Assert.Equal(ResultFingerprint.Compute(inDatabase.Pairs), ResultFingerprint.Compute(nts.Pairs));
            Assert.Equal(3, await store.CountAsync(SpatialLayer.Budynki));
        }

        [Fact]
        public async Task Diagnostics_ReturnPlan_AndServerInfo()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await SeedAsync(context);
            var store = SpatialStore.Create(context);

            var diagnostics = await store.DiagnoseAsync(ScenarioSql.Load(Engine, Scenario.SqlFileName));
            var server = await store.GetServerInfoAsync();

            Assert.False(string.IsNullOrWhiteSpace(diagnostics.Plan));
            Assert.Contains(server, s => s.Key == "wersja");
        }

        /// <summary>Wymaga migracji z tabelą budynki_stref.</summary>
        [Fact]
        public async Task SaveResults_ReplacesRowsOfVariant()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            const string variant = "TEST";

            await Scenario.SaveResultsAsync(context, variant, [new("x", "A"), new("y", "B")]);
            await Scenario.SaveResultsAsync(context, variant, [new("L", "N")]);

            var stored = await context.BudynkiStref.AsNoTracking().Where(x => x.Wariant == variant).ToListAsync();
            var row = Assert.Single(stored);
            Assert.Equal(("L", "N"), (row.IdBudynku, row.Oznaczenie));
        }
    }

    [Trait("Category", "Integration")]
    public class SqlServerPointInPolygonTests : PointInPolygonTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.SqlServer;
    }

    [Trait("Category", "Integration")]
    public class PostgresPointInPolygonTests : PointInPolygonTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.Postgres;
    }
}
