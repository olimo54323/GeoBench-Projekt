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
    /// Scenariusz 07: zapytanie w bazie daje te same liczby co NTS, z granicą 50 m włącznie i zerami.
    /// Działki i budynki wstawiane w transakcji wycofywanej na końcu.
    /// </summary>
    public abstract class BuildingsWithinDistanceTestsBase
    {
        private const double X0 = 500000, Y0 = 300000;

        protected abstract DatabaseEngine Engine { get; }

        private static async Task SeedAsync(GeoBenchContext context)
        {
            await context.TruncateAsync<Dzialka>();
            await context.TruncateAsync<Budynek>();
            foreach (var parcel in BuildingsWithinDistanceTests.Parcels(X0, Y0))
                context.Dzialki.Add(new Dzialka { IdDzialki = parcel.Id, Geom = (MultiPolygon)parcel.Geometry });
            foreach (var building in BuildingsWithinDistanceTests.Buildings(X0, Y0))
                context.Budynki.Add(new Budynek { IdBudynku = building.Id, Geom = (MultiPolygon)building.Geometry });
            await context.SaveChangesAsync();
        }

        [Fact]
        public async Task InDatabase_MatchesExpectedAndNts()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await SeedAsync(context);
            var store = SpatialStore.Create(context);
            var scenario = new BuildingsWithinDistanceScenario();

            var inDatabase = await new InDatabaseStrategy().RunAsync(scenario, store);
            var nts = await new NtsStrategy(parallel: true).RunAsync(scenario, store);

            Assert.Equal(BuildingsWithinDistanceTests.Expected, BuildingsWithinDistanceTests.Sorted(inDatabase.Pairs));
            var comparison = ResultComparison.Compare(nts.Pairs, inDatabase.Pairs);
            Assert.True(comparison.Equal, comparison.ToString());
        }

        /// <summary>Wymaga migracji z tabelą dzialki_budynki_50m.</summary>
        [Fact]
        public async Task SaveResults_ReplacesRowsOfVariant()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            var scenario = new BuildingsWithinDistanceScenario();
            const string variant = "TEST";

            await scenario.SaveResultsAsync(context, variant, [new("p1", "3"), new("p2", "0")]);
            await scenario.SaveResultsAsync(context, variant, [new("p3", "12")]);

            var row = Assert.Single(await context.DzialkiBudynki50m.AsNoTracking().Where(x => x.Wariant == variant).ToListAsync());
            Assert.Equal(("p3", 12), (row.IdDzialki, row.LiczbaBudynkow));
        }
    }

    [Trait("Category", "Integration")]
    public class SqlServerBuildingsWithinDistanceTests : BuildingsWithinDistanceTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.SqlServer;
    }

    [Trait("Category", "Integration")]
    public class PostgresBuildingsWithinDistanceTests : BuildingsWithinDistanceTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.Postgres;
    }
}
