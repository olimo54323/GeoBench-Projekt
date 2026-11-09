using GeoBench.Core.Data;
using GeoBench.Core.Data.Filters;
using GeoBench.Core.Model;
using Microsoft.EntityFrameworkCore;

namespace GeoBench.Tests.Filters
{
    /// <summary>
    /// Czy EF umie przetłumaczyć każdy rodzaj warunku na SQL obu silników. ToQueryString nie łączy się z bazą,
    /// więc test działa bez connection stringów.
    /// </summary>
    public class WhereTranslationTests
    {
        public static TheoryData<string> BuildingFilters => new()
        {
            "rodzaj = 'm' AND kondygnacje_nadziemne >= 5",
            "rodzaj != 'm' OR rodzaj IS NULL",
            "NOT (rodzaj IN ('m', 'g'))",
            "rodzaj NOT IN ('m')",
            "id_budynku LIKE '246401_1.0424%' AND id_budynku NOT LIKE '%x%'",
            "kondygnacje_nadziemne BETWEEN 2 AND 4 OR kondygnacje_nadziemne NOT BETWEEN 10 AND 20",
            "id_budynku > 'B' AND id_budynku <= 'C'",
            "data >= '2026-01-01' AND data < '2026-12-31 23:59:59'",
            "kondygnacje_podziemne IS NOT NULL"
        };

        public static TheoryData<string> ZoneFilters => new()
        {
            "symbol = 'SO'",
            "nazwa = 'StrefaOtwarta' OR nazwa IN ('StrefaUslugowa')",
            "nazwa != 'StrefaOtwarta'",
            "maks_wysokosc_zabudowy > 12.5 AND obowiazuje_od >= '2026-01-01'",
            "poczatek_wersji_obiektu >= '2026-01-01 00:00'"
        };

        [Theory]
        [MemberData(nameof(BuildingFilters))]
        public void Buildings_TranslateInBothEngines(string where)
        {
            foreach (var context in Contexts())
            {
                using (context)
                {
                    var filter = WhereFilter.Compile<Budynek>(where, WhereSchema.For<Budynek>(context))!;
                    var sql = context.Budynki.Where(filter).ToQueryString();
                    Assert.Contains("budynek", sql);
                }
            }
        }

        [Theory]
        [MemberData(nameof(ZoneFilters))]
        public void Zones_TranslateInBothEngines(string where)
        {
            foreach (var context in Contexts())
            {
                using (context)
                {
                    var filter = WhereFilter.Compile<StrefaPlanistyczna>(where, WhereSchema.For<StrefaPlanistyczna>(context))!;
                    var sql = context.StrefyPlanistyczne.Where(filter).ToQueryString();
                    Assert.Contains("strefa_planistyczna", sql);
                }
            }
        }

        [Fact]
        public void Like_UsesDatabaseLike()
        {
            foreach (var context in Contexts())
            {
                using (context)
                {
                    var filter = WhereFilter.Compile<Dzialka>("nazwa_obrebu LIKE 'Rak%'", WhereSchema.For<Dzialka>(context))!;
                    Assert.Contains("LIKE", context.Dzialki.Where(filter).ToQueryString());
                }
            }
        }

        private static IEnumerable<GeoBenchContext> Contexts()
        {
            yield return TestModel.Create();
            yield return TestModel.CreateSqlServer();
        }
    }
}
