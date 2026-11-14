using GeoBench.Core.Benchmark.Scenarios;
using GeoBench.Core.Configuration;
using GeoBench.Core.Data;
using GeoBench.Core.Export;
using GeoBench.Core.Model;
using Microsoft.EntityFrameworkCore;

namespace GeoBench.Tests.Integration
{
    /// <summary>
    /// Eksport z bazy: lista wariantów z liczbą wierszy i odczyt wybranych kolumn w wybranej kolejności,
    /// posortowany po identyfikatorach. Wiersze zapisywane w transakcji wycofywanej na końcu.
    /// Wymaga migracji z tabelą budynki_najblizsze.
    /// </summary>
    public abstract class ResultExportTestsBase
    {
        private const string Variant = "TEST_EKSPORT";

        protected abstract DatabaseEngine Engine { get; }

        private static readonly ResultTable Nearest = ResultTableCatalog.All.Single(t => t.EntityType == typeof(BudynekNajblizszy));

        [Fact]
        public async Task Variants_And_SelectedColumns_InChosenOrder()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await new NearestBuildingScenario().SaveResultsAsync(context, Variant,
                [new("b2", "b1", 2.5), new("b1", "b2", 2.5), new("b3", "b1", 10)]);
            var reader = new ResultExportReader(context);

            var variants = await reader.VariantsAsync(Nearest);
            var all = ResultTableCatalog.Columns(context, Nearest);
            var columns = new[] { all.Single(c => c.Name == "odleglosc_m"), all.Single(c => c.Name == "id_budynku") };
            var data = await reader.ReadAsync(Nearest, Variant, columns);

            Assert.Contains(new ExportVariant(Variant, 3), variants);
            Assert.Equal(new[] { "odleglosc_m", "id_budynku" }, data.Columns.Select(c => c.Name));
            Assert.Equal(new[] { "b1", "b2", "b3" }, data.Rows.Select(r => (string)r[1]!));
            Assert.Equal(10.0, Convert.ToDouble(data.Rows[2][0]));
        }

        [Fact]
        public async Task Read_UnknownColumn_Throws()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                new ResultExportReader(context).ReadAsync(Nearest, Variant, [new ResultColumn("nie_ma", typeof(string))]));
        }
    }

    [Trait("Category", "Integration")]
    public class SqlServerResultExportTests : ResultExportTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.SqlServer;
    }

    [Trait("Category", "Integration")]
    public class PostgresResultExportTests : ResultExportTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.Postgres;
    }
}
