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
    /// Scenariusz 08: walidacja w bazie znajduje tę samą niepoprawną geometrię co NTS, z tym samym polem po naprawie.
    /// Kokardka wstawiana surowym SQL z WKT (zapis niepoprawnej geometrii przez EF nie jest tu sprawdzany).
    /// Tabele opróżniane i wypełniane w transakcji wycofywanej na końcu.
    /// </summary>
    public abstract class GeometryValidationTestsBase
    {
        private const double X0 = 500000, Y0 = 300000;

        protected abstract DatabaseEngine Engine { get; }

        private static async Task SeedValidAsync(GeoBenchContext context)
        {
            await context.TruncateAsync<Budynek>();
            await context.TruncateAsync<Dzialka>();
            await context.TruncateAsync<StrefaPlanistyczna>();
            context.Budynki.Add(new Budynek { IdBudynku = "b_ok", Geom = BenchmarkFixtures.Rectangle(0, 0, 5, 5, X0, Y0) });
            context.Dzialki.Add(new Dzialka { IdDzialki = "d_ok", Geom = BenchmarkFixtures.Rectangle(0, 0, 50, 50, X0, Y0) });
            await context.SaveChangesAsync();
        }

        private Task InsertBowtieBuildingAsync(GeoBenchContext context)
        {
            var wkt = GeometryValidationTests.Bowtie(X0 + 10, Y0).AsText();
            var geometry = Engine == DatabaseEngine.SqlServer
                ? $"geometry::STGeomFromText(N'{wkt}', {GeoBenchContext.Srid})"
                : $"ST_GeomFromText('{wkt}', {GeoBenchContext.Srid})";
            var id = Engine == DatabaseEngine.SqlServer ? "N'b_zle'" : "'b_zle'";
            return context.Database.ExecuteSqlRawAsync($"INSERT INTO budynek (id_budynku, geom) VALUES ({id}, {geometry})");
        }

        [Fact]
        public async Task InDatabase_FindsInvalid_SameAsNts()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await SeedValidAsync(context);
            await InsertBowtieBuildingAsync(context);
            var store = SpatialStore.Create(context);
            var scenario = new GeometryValidationScenario();

            var inDatabase = await new InDatabaseStrategy().RunAsync(scenario, store);
            var nts = await new NtsStrategy(parallel: false).RunAsync(scenario, store);

            var pair = Assert.Single(inDatabase.Pairs);
            Assert.Equal(("budynek", "b_zle"), (pair.IdA, pair.IdB));
            Assert.Equal(8, pair.Measure!.Value, 6);
            var comparison = ResultComparison.Compare(nts.Pairs, inDatabase.Pairs);
            Assert.True(comparison.Equal, comparison.ToString());
        }

        [Fact]
        public async Task InDatabase_ValidData_GivesEmptyResult()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await SeedValidAsync(context);

            var run = await new InDatabaseStrategy().RunAsync(new GeometryValidationScenario(), SpatialStore.Create(context));

            Assert.Empty(run.Pairs);
        }

        /// <summary>Wymaga migracji z tabelą geometrie_naprawione.</summary>
        [Fact]
        public async Task SaveResults_ReplacesRowsOfVariant()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            var scenario = new GeometryValidationScenario();
            const string variant = "TEST";

            await scenario.SaveResultsAsync(context, variant, [new("budynek", "b1", 8), new("dzialka", "d1", 2)]);
            await scenario.SaveResultsAsync(context, variant, [new("strefa_planistyczna", "S1", 3.5)]);

            var row = Assert.Single(await context.GeometrieNaprawione.AsNoTracking().Where(x => x.Wariant == variant).ToListAsync());
            Assert.Equal(("strefa_planistyczna", "S1", 3.5), (row.Warstwa, row.IdObiektu, row.PoleM2));
        }
    }

    [Trait("Category", "Integration")]
    public class SqlServerGeometryValidationTests : GeometryValidationTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.SqlServer;
    }

    [Trait("Category", "Integration")]
    public class PostgresGeometryValidationTests : GeometryValidationTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.Postgres;
    }
}
