using GeoBench.Core.Configuration;
using GeoBench.Core.Data;
using GeoBench.Core.Export;
using GeoBench.Core.Model;
using GeoBench.Core.Storage;
using GeoBench.Tests.Benchmark;

namespace GeoBench.Tests.Integration
{
    /// <summary>
    /// Zakładka Dane na obu bazach: rozmiar tabeli, indeks przestrzenny i warianty tabel wyników.
    /// Budynki wstawiane w transakcji wycofywanej na końcu; wymaga indeksu z importu budynków i migracji tabel wyników.
    /// </summary>
    public abstract class DataOverviewTestsBase
    {
        private const double X0 = 500000, Y0 = 300000;

        protected abstract DatabaseEngine Engine { get; }

        [Fact]
        public async Task Layer_HasSizeIndexAndValidity()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.TruncateAsync<Budynek>();
            context.Budynki.Add(new Budynek { IdBudynku = "b1", Geom = BenchmarkFixtures.Rectangle(0, 0, 10, 10, X0, Y0) });
            context.Budynki.Add(new Budynek { IdBudynku = "b2", Geom = BenchmarkFixtures.Rectangle(20, 0, 30, 10, X0, Y0) });
            await context.SaveChangesAsync();

            var layer = await DataOverview.LayerAsync(SpatialStore.Create(context), SpatialLayer.Budynki, checkValidity: true);

            Assert.Null(layer.Error);
            Assert.Equal(2, layer.Count);
            Assert.Equal(0, layer.InvalidCount);
            Assert.True(layer.TableSizeBytes > 0);
            Assert.NotNull(layer.Index);
            Assert.Equal(X0, layer.Extent!.MinX, 6);
            Assert.Equal(X0 + 30, layer.Extent.MaxX, 6);
        }

        [Fact]
        public async Task Results_ListAllResultTables_WithoutErrors()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);

            var tables = await DataOverview.ResultsAsync(context, Engine);

            Assert.Equal(ResultTableCatalog.All.Count, tables.Count);
            Assert.All(tables, t => Assert.Null(t.Error));
        }
    }

    [Trait("Category", "Integration")]
    public class SqlServerDataOverviewTests : DataOverviewTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.SqlServer;
    }

    [Trait("Category", "Integration")]
    public class PostgresDataOverviewTests : DataOverviewTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.Postgres;
    }
}
