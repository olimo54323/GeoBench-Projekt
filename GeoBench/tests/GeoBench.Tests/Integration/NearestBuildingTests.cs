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

namespace GeoBench.Tests.Integration
{
    /// <summary>
    /// Scenariusz 06: zapytanie w bazie daje te same pary i odległości co NTS (z remisami przy odległości 0).
    /// Budynki wstawiane w transakcji wycofywanej na końcu.
    /// </summary>
    public abstract class NearestBuildingTestsBase
    {
        private const double X0 = 500000, Y0 = 300000;

        protected abstract DatabaseEngine Engine { get; }

        private static async Task SeedAsync(GeoBenchContext context)
        {
            await context.TruncateAsync<Budynek>();
            foreach (var building in NearestBuildingTests.Buildings(X0, Y0))
                context.Budynki.Add(new Budynek { IdBudynku = building.Id, Geom = (NetTopologySuite.Geometries.MultiPolygon)building.Geometry });
            await context.SaveChangesAsync();
        }

        [Fact]
        public async Task InDatabase_MatchesExpectedAndNts()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await SeedAsync(context);
            var store = SpatialStore.Create(context);
            var scenario = new NearestBuildingScenario();

            var inDatabase = await new InDatabaseStrategy().RunAsync(scenario, store);
            var nts = await new NtsStrategy(parallel: true).RunAsync(scenario, store);

            Assert.Equal(NearestBuildingTests.Expected, NearestBuildingTests.Sorted(inDatabase.Pairs));
            var comparison = ResultComparison.Compare(nts.Pairs, inDatabase.Pairs);
            Assert.True(comparison.Equal, comparison.ToString());
        }

        /// <summary>Wymaga migracji z tabelą budynki_najblizsze.</summary>
        [Fact]
        public async Task SaveResults_ReplacesRowsOfVariant()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            var scenario = new NearestBuildingScenario();
            const string variant = "TEST";

            await scenario.SaveResultsAsync(context, variant, [new("b1", "b2", 0), new("b2", "b1", 0)]);
            await scenario.SaveResultsAsync(context, variant, [new("c", "d", 8.5)]);

            var row = Assert.Single(await context.BudynkiNajblizsze.AsNoTracking().Where(x => x.Wariant == variant).ToListAsync());
            Assert.Equal(("c", "d", 8.5), (row.IdBudynku, row.IdSasiada, row.OdlegloscM));
        }
    }

    [Trait("Category", "Integration")]
    public class SqlServerNearestBuildingTests : NearestBuildingTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.SqlServer;
    }

    [Trait("Category", "Integration")]
    public class PostgresNearestBuildingTests : NearestBuildingTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.Postgres;
    }
}
