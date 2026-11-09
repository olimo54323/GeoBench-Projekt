using GeoBench.Core.Configuration;
using GeoBench.Core.Data;
using GeoBench.Core.Import.Egib;
using GeoBench.Core.Import.Wfs;
using Microsoft.EntityFrameworkCore;

namespace GeoBench.Tests.Integration
{
    /// <summary>
    /// Zapis masowy EGiB (SqlBulkCopy / COPY) do obu baz z fixture'ów. Import czyści tabelę, więc test działa
    /// w transakcji wycofywanej na końcu: TRUNCATE, ładowanie i indeks w obu bazach są transakcyjne.
    /// </summary>
    public abstract class EgibImportTestsBase
    {
        private static readonly WfsLayerOptions Dzialki = new() { Name = "dzialki", TypeName = "gmgml:dzialki", Target = EgibTarget.Dzialka };
        private static readonly WfsLayerOptions Budynki = new() { Name = "budynki", TypeName = "gmgml:budynki", Target = EgibTarget.Budynek };

        protected abstract DatabaseEngine Engine { get; }

        [Fact]
        public async Task Parcels_BulkInsert_SkipsDuplicates()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();

            // Ta sama działka w dwóch układach: druga to duplikat identyfikatora
            var records = FixtureFiles.Read("wfs_powiat_dzialka_2180.gml").Concat(FixtureFiles.Read("wfs_powiat_dzialka_2177.gml"));
            var report = await new EgibImporter().ImportAsync(context, records, Dzialki);

            Assert.Equal(2, report.Read);
            Assert.Equal(1, report.Saved);
            Assert.Equal(1, report.Duplicates);
            Assert.Empty(report.Rejected);

            var stored = await context.Dzialki.AsNoTracking().SingleAsync();
            Assert.Equal("246401_1.0219.37/6", stored.IdDzialki);
            Assert.Equal("m. Częstochowa", stored.NazwaGminy);
            Assert.Equal(new DateTime(2026, 11, 7, 1, 6, 13), stored.Data);
            Assert.NotNull(stored.Geom);
            Assert.Equal(GeoBenchContext.Srid, stored.Geom.SRID);

            // Zapytanie przestrzenne w bazie z nowym indeksem
            var punkt = stored.Geom.InteriorPoint;
            Assert.True(await context.Dzialki.AnyAsync(d => d.Geom!.Contains(punkt)));
        }

        [Fact]
        public async Task Buildings_BulkInsert_FromBothServices()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();

            var records = FixtureFiles.Read("wfs_powiat_budynek.gml").Concat(FixtureFiles.Read("wfs_zbiorcza_budynek.gml"));
            var report = await new EgibImporter().ImportAsync(context, records, Budynki);

            Assert.Equal(2, report.Saved);
            var stored = await context.Budynki.AsNoTracking().OrderBy(b => b.IdBudynku).ToListAsync();
            Assert.Equal(new[] { "246401_1.0182.197_BUD", "246401_1.0424.27_BUD" }, stored.Select(b => b.IdBudynku));
            Assert.All(stored, b => Assert.Equal(GeoBenchContext.Srid, b.Geom!.SRID));
            Assert.All(stored, b => Assert.Null(b.KondygnacjeNadziemne));
        }
    }

    [Trait("Category", "Integration")]
    public class SqlServerEgibImportTests : EgibImportTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.SqlServer;
    }

    [Trait("Category", "Integration")]
    public class PostgresEgibImportTests : EgibImportTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.Postgres;
    }
}
