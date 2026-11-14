using GeoBench.Core.Configuration;
using GeoBench.Core.Data;
using GeoBench.Core.Model;
using GeoBench.Core.Storage;
using GeoBench.Tests.Benchmark;
using Microsoft.EntityFrameworkCore;

namespace GeoBench.Tests.Integration
{
    /// <summary>
    /// Czyszczenie bazy (zakładka Dane) w transakcji wycofywanej na końcu: po czyszczeniu tabele są puste,
    /// indeksów przestrzennych nie ma, a liczniki kluczy zaczynają od 1. Po wycofaniu prawdziwe dane wracają;
    /// w SQL Server test sprawdza też, że licznik akt_planowania nie został cofnięty poniżej istniejących kluczy.
    /// </summary>
    public abstract class DatabaseResetTestsBase
    {
        private const double X0 = 500000, Y0 = 300000;

        protected abstract DatabaseEngine Engine { get; }

        [Fact]
        public async Task Reset_EmptiesAllTables_DropsSpatialIndexes_AndRestartsKeys()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                var akt = new AktPlanowania { PrzestrzenNazw = "PL.TEST/RESET", LokalnyId = "1POG", Tytul = "Akt testowy czyszczenia" };
                akt.Strefy.Add(new StrefaPlanistyczna
                {
                    Oznaczenie = "1SC", LokalnyId = "1POG-1SC", Symbol = "SC", Nazwa = RodzajStrefyPlanistycznej.StrefaCmentarzy,
                    Geom = BenchmarkFixtures.Rectangle(0, 0, 10, 10, X0, Y0)
                });
                akt.Regulacje.Add(new Regulacja
                {
                    Rodzaj = RodzajRegulacji.ObszarUzupelnieniaZabudowy, Oznaczenie = "1OUZ", LokalnyId = "1POG-1OUZ", Symbol = "OUZ",
                    Geom = BenchmarkFixtures.Rectangle(0, 0, 5, 5, X0, Y0)
                });
                context.AktyPlanowania.Add(akt);
                context.Budynki.Add(new Budynek { IdBudynku = "reset_b1", Geom = BenchmarkFixtures.Rectangle(1, 1, 2, 2, X0, Y0) });
                await context.SaveChangesAsync();
                await context.CreateSpatialIndexAsync<Budynek>(nameof(Budynek.Geom), akt.Strefy[0].Geom!.EnvelopeInternal);
                context.ChangeTracker.Clear();

                await context.ResetDataAsync();

                Assert.Equal(0, await context.AktyPlanowania.CountAsync());
                Assert.Equal(0, await context.StrefyPlanistyczne.CountAsync());
                Assert.Equal(0, await context.Regulacje.CountAsync());
                Assert.Equal(0, await context.Budynki.CountAsync());
                Assert.Equal(0, await context.Dzialki.CountAsync());
                Assert.Equal(0, await context.BudynkiStref.CountAsync());
                Assert.Equal(0, await context.GeometrieNaprawione.CountAsync());
                Assert.Null(await SpatialStore.Create(context).SpatialIndexAsync(SpatialLayer.Budynki));

                // Liczniki od 1: tabela obcinana (budynek) i tabela nadrzędna czyszczona przez DELETE (akt_planowania)
                var nowyAkt = new AktPlanowania { PrzestrzenNazw = "PL.TEST/RESET", LokalnyId = "2POG", Tytul = "Akt po czyszczeniu" };
                var nowyBudynek = new Budynek { IdBudynku = "reset_b2", Geom = BenchmarkFixtures.Rectangle(1, 1, 2, 2, X0, Y0) };
                context.AktyPlanowania.Add(nowyAkt);
                context.Budynki.Add(nowyBudynek);
                await context.SaveChangesAsync();
                Assert.Equal(1, nowyAkt.Oid);
                Assert.Equal(1, nowyBudynek.Oid);
            }

            // Transakcja wycofana: licznik IDENTITY nie może zostać poniżej istniejących kluczy
            // (gdyby RESEED nie był wycofywany, kolejny import aktu trafiłby na zajęty klucz)
            if (Engine == DatabaseEngine.SqlServer)
            {
                var current = await Scalar(context, "SELECT CAST(ISNULL(IDENT_CURRENT('akt_planowania'), 0) AS bigint) AS [Value]");
                var max = await Scalar(context, "SELECT CAST(ISNULL(MAX(oid), 0) AS bigint) AS [Value] FROM akt_planowania");
                // Naprawa na wypadek błędu: bez argumentu RESEED ustawia licznik na największy klucz, gdy jest mniejszy
                await context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT (N'akt_planowania') WITH NO_INFOMSGS;");
                Assert.True(current >= max, $"IDENT_CURRENT = {current} < MAX(oid) = {max}: RESEED nie został wycofany.");
            }
        }

        [Fact]
        public async Task ModelTables_ListDataAndResultTables()
        {
            await using var context = new GeoBenchContextFactory(GeoBenchConfiguration.Build()).Create(Engine);

            var tables = context.ModelTables();

            Assert.Contains("akt_planowania", tables);
            Assert.Contains("regulacja", tables);
            Assert.Contains("budynek", tables);
            Assert.Contains("geometrie_naprawione", tables);
            Assert.DoesNotContain("__EFMigrationsHistory", tables);
        }

        private static Task<long> Scalar(GeoBenchContext context, string sql) =>
            context.Database.SqlQueryRaw<long>(sql).SingleAsync();
    }

    [Trait("Category", "Integration")]
    public class SqlServerDatabaseResetTests : DatabaseResetTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.SqlServer;
    }

    [Trait("Category", "Integration")]
    public class PostgresDatabaseResetTests : DatabaseResetTestsBase
    {
        protected override DatabaseEngine Engine => DatabaseEngine.Postgres;
    }
}
