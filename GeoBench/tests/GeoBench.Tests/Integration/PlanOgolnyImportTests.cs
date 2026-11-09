using GeoBench.Core.Configuration;
using GeoBench.Core.Data;
using GeoBench.Core.Import.PlanOgolny;
using GeoBench.Core.Model;
using Microsoft.EntityFrameworkCore;

namespace GeoBench.Tests.Integration
{
    /// <summary>
    /// Import fragmentu planu (akt testowy PL.ZIPPZP.TEST, 2 strefy, 1 OUZ) do obu baz.
    /// Test działa w transakcji wycofywanej na końcu; obejmuje ona też ponowne utworzenie indeksu
    /// przestrzennego (DDL jest transakcyjny w obu bazach), więc prawdziwy plan i jego indeks zostają bez zmian.
    /// </summary>
    public abstract class PlanOgolnyImportTestsBase
    {
        private const string PrzestrzenNazw = "PL.ZIPPZP.TEST/246401-POG";

        protected abstract DatabaseEngine Engine { get; }

        [Fact]
        public async Task Import_SavesActAndZones_AndReplacesOnReimport()
        {
            var factory = new GeoBenchContextFactory(GeoBenchConfiguration.Build());
            var importer = new PlanOgolnyImporter();

            await using var context = factory.Create(Engine);
            await using var transaction = await context.Database.BeginTransactionAsync();

            var report = await importer.ImportFileAsync(context, FixtureFiles.PathOf("pog_plan_testowy.gml"), daneBadawcze: true);

            Assert.Equal(4, report.Read);
            Assert.Equal(3, report.Saved);
            Assert.Empty(report.Rejected);
            Assert.Equal(1, report.SkippedByType["ObszarUzupelnieniaZabudowy"]);
            Assert.Equal(3, report.GeometryValid + report.GeometryRepaired);

            // Ponowny import zastępuje akt zamiast dublować strefy
            context.ChangeTracker.Clear();
            await importer.ImportFileAsync(context, FixtureFiles.PathOf("pog_plan_testowy.gml"), daneBadawcze: true);
            context.ChangeTracker.Clear();

            var akt = await context.AktyPlanowania.AsNoTracking()
                .Include(a => a.Strefy)
                .SingleAsync(a => a.PrzestrzenNazw == PrzestrzenNazw && a.LokalnyId == "1POG");
            Assert.True(akt.DaneBadawcze);
            Assert.NotNull(akt.Zasieg);
            Assert.Equal(GeoBenchContext.Srid, akt.Zasieg.SRID);
            Assert.Equal(new[] { "1SC", "70SN" }, akt.Strefy.Select(s => s.Oznaczenie).Order());

            var cmentarz = akt.Strefy.Single(s => s.Oznaczenie == "1SC");
            Assert.Equal(RodzajStrefyPlanistycznej.StrefaCmentarzy, cmentarz.Nazwa);
            Assert.Equal(10.0m, cmentarz.MaksWysokoscZabudowy);
            Assert.Equal(new DateTime(2025, 7, 4, 11, 26, 13), cmentarz.PoczatekWersjiObiektu!.Value, TimeSpan.FromSeconds(1));
            Assert.NotNull(cmentarz.Geom);
            Assert.Equal(GeoBenchContext.Srid, cmentarz.Geom.SRID);

            // Zapytanie przestrzenne w bazie (z nowym indeksem): punkt wewnątrz strefy 1SC trafia tylko w nią
            var punkt = cmentarz.Geom.InteriorPoint;
            var trafione = await context.StrefyPlanistyczne
                .Where(s => s.AktPlanowaniaOid == akt.Oid && s.Geom!.Contains(punkt))
                .Select(s => s.Oznaczenie)
                .ToListAsync();
            Assert.Equal("1SC", Assert.Single(trafione));
        }
    }

    [Trait("Category", "Integration")]
    public class SqlServerPlanOgolnyImportTests : PlanOgolnyImportTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.SqlServer;
    }

    [Trait("Category", "Integration")]
    public class PostgresPlanOgolnyImportTests : PlanOgolnyImportTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.Postgres;
    }
}
