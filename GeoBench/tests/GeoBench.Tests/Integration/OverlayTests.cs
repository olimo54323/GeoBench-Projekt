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
    /// Scenariusze nakładania 05a–05c: zapytanie w bazie (obie wersje: z naprawą i bez) i NTS dają te same pary
    /// i tę samą sumę pól. Dane wstawiane w transakcji wycofywanej na końcu. Tylko poprawne geometrie:
    /// zapis niepoprawnej geometrii przez EF do SQL Server nie jest tu sprawdzany.
    /// </summary>
    public abstract class OverlayTestsBase
    {
        private const double X0 = 500000, Y0 = 300000;

        protected abstract DatabaseEngine Engine { get; }

        public static TheoryData<string, bool> Cases => new()
        {
            { "05a", false }, { "05a", true },
            { "05b", false }, { "05b", true },
            { "05c", false }, { "05c", true }
        };

        private static IBenchmarkScenario Scenario(string id) => OverlayScenarios.All.Single(s => s.Id == id);

        private static async Task SeedAsync(GeoBenchContext context)
        {
            await context.TruncateAsync<Budynek>();
            await context.TruncateAsync<Dzialka>();
            await context.TruncateAsync<StrefaPlanistyczna>();

            var akt = new AktPlanowania { PrzestrzenNazw = "PL.TEST", LokalnyId = $"test-{Guid.NewGuid():N}", Tytul = "Plan testowy 05", DaneBadawcze = true };
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

            // Działki: w A, na granicy A|B, styczna z B (bez pary)
            context.Dzialki.Add(new Dzialka { IdDzialki = "d_wewnatrz", Geom = BenchmarkFixtures.Rectangle(2, 2, 8, 8, X0, Y0) });
            context.Dzialki.Add(new Dzialka { IdDzialki = "d_granica", Geom = BenchmarkFixtures.Rectangle(8, 0, 12, 10, X0, Y0) });
            context.Dzialki.Add(new Dzialka { IdDzialki = "d_styk", Geom = BenchmarkFixtures.Rectangle(20, 0, 25, 10, X0, Y0) });
            // Budynki: w d_wewnatrz, na granicy działek i stref, poza wszystkim
            context.Budynki.Add(new Budynek { IdBudynku = "b_wewnatrz", Geom = BenchmarkFixtures.Rectangle(3, 3, 5, 5, X0, Y0) });
            context.Budynki.Add(new Budynek { IdBudynku = "b_granica", Geom = BenchmarkFixtures.Rectangle(7, 4, 11, 6, X0, Y0) });
            context.Budynki.Add(new Budynek { IdBudynku = "b_poza", Geom = BenchmarkFixtures.Rectangle(50, 50, 52, 52, X0, Y0) });
            await context.SaveChangesAsync();
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task InDatabase_And_Nts_GiveSamePairsAndAreas(string id, bool repair)
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await SeedAsync(context);
            var store = SpatialStore.Create(context);
            var scenario = Scenario(id);
            scenario.Configure(new BenchmarkOptions { RepairInvalidGeometries = repair });

            var inDatabase = await new InDatabaseStrategy().RunAsync(scenario, store);
            var nts = await new NtsStrategy(parallel: false).RunAsync(scenario, store);

            Assert.NotEmpty(inDatabase.Pairs);
            var comparison = ResultComparison.Compare(nts.Pairs, inDatabase.Pairs);
            Assert.True(comparison.Equal, $"{comparison}; baza: {string.Join(", ", inDatabase.Pairs)}");
        }

        [Fact]
        public async Task Overlay05a_ExpectedPairs()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await SeedAsync(context);
            var scenario = Scenario("05a");
            scenario.Configure(new BenchmarkOptions());

            var run = await new InDatabaseStrategy().RunAsync(scenario, SpatialStore.Create(context));

            var pairs = run.Pairs.OrderBy(p => p.IdA, StringComparer.Ordinal).ThenBy(p => p.IdB, StringComparer.Ordinal)
                .Select(p => (p.IdA, p.IdB, Math.Round(p.Measure!.Value, 6))).ToArray();
            Assert.Equal(new[] { ("d_granica", "A", 20.0), ("d_granica", "B", 20.0), ("d_wewnatrz", "A", 36.0) }, pairs);
        }

        [Fact]
        public async Task InvalidIds_EmptyForValidData()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await SeedAsync(context);

            Assert.Empty(await SpatialStore.Create(context).InvalidIdsAsync(SpatialLayer.Dzialki));
        }

        /// <summary>Wymaga migracji z tabelami *_pow.</summary>
        [Fact]
        public async Task SaveResults_ReplacesRowsOfVariant()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            const string variant = "TEST";

            await OverlayScenarios.DzialkiBudynki.SaveResultsAsync(context, variant, [new("d1", "b1", 1.5), new("d1", "b2", 2)]);
            await OverlayScenarios.DzialkiBudynki.SaveResultsAsync(context, variant, [new("d2", "b3", 12.25)]);
            await OverlayScenarios.DzialkiStrefy.SaveResultsAsync(context, variant, [new("d2", "A", 3)]);
            await OverlayScenarios.BudynkiStrefy.SaveResultsAsync(context, variant, [new("b3", "A", 4)]);

            var row = Assert.Single(await context.BudynkiDzialekPow.AsNoTracking().Where(x => x.Wariant == variant).ToListAsync());
            Assert.Equal(("d2", "b3", 12.25), (row.IdDzialki, row.IdBudynku, row.PoleM2));
            Assert.Single(await context.DzialkiStrefPow.AsNoTracking().Where(x => x.Wariant == variant).ToListAsync());
            Assert.Single(await context.BudynkiStrefPow.AsNoTracking().Where(x => x.Wariant == variant).ToListAsync());
        }
    }

    [Trait("Category", "Integration")]
    public class SqlServerOverlayTests : OverlayTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.SqlServer;
    }

    [Trait("Category", "Integration")]
    public class PostgresOverlayTests : OverlayTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.Postgres;
    }
}
