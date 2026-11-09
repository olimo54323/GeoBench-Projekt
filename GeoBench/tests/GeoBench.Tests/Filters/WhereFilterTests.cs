using GeoBench.Core.Data;
using GeoBench.Core.Data.Filters;
using GeoBench.Core.Model;

namespace GeoBench.Tests.Filters
{
    /// <summary>Znaczenie warunków na danych w pamięci (LIKE przez dopasowanie w pamięci). Model EF bez połączenia z bazą.</summary>
    public class WhereFilterTests
    {
        private static readonly Budynek[] Budynki =
        [
            new() { Oid = 1, IdBudynku = "A_BUD", Rodzaj = "m", KondygnacjeNadziemne = 2, Data = new DateTime(2026, 1, 10) },
            new() { Oid = 2, IdBudynku = "B_BUD", Rodzaj = "g", KondygnacjeNadziemne = 6, Data = new DateTime(2026, 3, 1, 12, 0, 0) },
            new() { Oid = 3, IdBudynku = "C_BUD", Rodzaj = null, KondygnacjeNadziemne = null },
            new() { Oid = 4, IdBudynku = "D'BUD", Rodzaj = "m", KondygnacjeNadziemne = 11 }
        ];

        private static int[] Budynek(string where)
        {
            using var context = TestModel.Create();
            var filter = WhereFilter.Compile<Budynek>(where, WhereSchema.For<Budynek>(context), inMemory: true)!.Compile();
            return Budynki.Where(filter).Select(b => b.Oid).ToArray();
        }

        [Theory]
        [InlineData("rodzaj = 'm'", new[] { 1, 4 })]
        [InlineData("RODZAJ = 'm'", new[] { 1, 4 })]
        [InlineData("rodzaj != 'm'", new[] { 2 })]               // NULL nie spełnia nierówności (jak w SQL)
        [InlineData("NOT rodzaj = 'm'", new[] { 2 })]            // ani jej zaprzeczenia
        [InlineData("rodzaj <> 'm' OR rodzaj IS NULL", new[] { 2, 3 })]
        [InlineData("kondygnacje_nadziemne > 2", new[] { 2, 4 })]
        [InlineData("kondygnacje_nadziemne >= 2 AND kondygnacje_nadziemne < 11", new[] { 1, 2 })]
        [InlineData("kondygnacje_nadziemne <= 6", new[] { 1, 2 })]
        [InlineData("kondygnacje_nadziemne BETWEEN 2 AND 6", new[] { 1, 2 })]
        [InlineData("kondygnacje_nadziemne NOT BETWEEN 2 AND 6", new[] { 4 })]
        [InlineData("rodzaj IN ('g', 'x')", new[] { 2 })]
        [InlineData("rodzaj NOT IN ('g')", new[] { 1, 4 })]
        [InlineData("NOT (rodzaj IN ('g'))", new[] { 1, 4 })]
        [InlineData("rodzaj IS NULL", new[] { 3 })]
        [InlineData("NOT rodzaj IS NULL", new[] { 1, 2, 4 })]
        [InlineData("id_budynku LIKE '_\\_BUD'", new int[0])]
        [InlineData("id_budynku LIKE 'A%'", new[] { 1 })]
        [InlineData("id_budynku LIKE '_''BUD'", new[] { 4 })]
        [InlineData("id_budynku NOT LIKE '%_BUD'", new int[0])]
        [InlineData("id_budynku LIKE 'a%'", new int[0])]         // w pamięci z rozróżnianiem wielkości liter
        [InlineData("id_budynku > 'B_BUD'", new[] { 3, 4 })]
        [InlineData("oid IN (1, 3) OR kondygnacje_nadziemne > 10", new[] { 1, 3, 4 })]
        [InlineData("(rodzaj = 'm' OR rodzaj = 'g') AND kondygnacje_nadziemne > 5", new[] { 2, 4 })]
        [InlineData("data >= '2026-02-01'", new[] { 2 })]
        [InlineData("data = '2026-03-01 12:00'", new[] { 2 })]
        [InlineData("data < '2026-03-01 12:00:00'", new[] { 1 })]
        [InlineData("kondygnacje_podziemne IS NULL AND oid > -1", new[] { 1, 2, 3, 4 })]
        public void Buildings(string where, int[] expected)
        {
            Assert.Equal(expected, Budynek(where));
        }

        [Fact]
        public void Decimals_AndEnums()
        {
            using var context = TestModel.Create();
            var strefy = new[]
            {
                new StrefaPlanistyczna { Oid = 1, Oznaczenie = "1SO", LokalnyId = "a", Symbol = "SO", Nazwa = RodzajStrefyPlanistycznej.StrefaOtwarta, MaksWysokoscZabudowy = 12.5m, ObowiazujeOd = new DateOnly(2026, 1, 31) },
                new StrefaPlanistyczna { Oid = 2, Oznaczenie = "2SU", LokalnyId = "b", Symbol = "SU", Nazwa = RodzajStrefyPlanistycznej.StrefaUslugowa, MaksWysokoscZabudowy = 30 }
            };
            var schema = WhereSchema.For<StrefaPlanistyczna>(context);
            int[] Run(string where) => strefy.Where(WhereFilter.Compile<StrefaPlanistyczna>(where, schema, inMemory: true)!.Compile()).Select(s => s.Oid).ToArray();

            Assert.Equal(new[] { 1 }, Run("maks_wysokosc_zabudowy < 12.6"));
            Assert.Equal(new[] { 1 }, Run("nazwa = 'strefaotwarta'"));
            Assert.Equal(new[] { 2 }, Run("nazwa != 'StrefaOtwarta'"));
            Assert.Equal(new[] { 1, 2 }, Run("nazwa IN ('StrefaOtwarta', 'StrefaUslugowa')"));
            Assert.Equal(new[] { 1 }, Run("obowiazuje_od = '2026-01-31'"));
            Assert.Equal(new[] { 2 }, Run("symbol = 'SU'"));
        }

        [Theory]
        [InlineData("rodzja = 'm'", 0, "Nieznana kolumna")]
        [InlineData("kondygnacje_nadziemne = '5'", 24, "liczba całkowita")]
        [InlineData("kondygnacje_nadziemne = 2.5", 24, "część ułamkową")]
        [InlineData("rodzaj = 5", 9, "tekstowa")]
        [InlineData("kondygnacje_nadziemne LIKE '5%'", 0, "LIKE")]
        [InlineData("data > '10.01.2026'", 7, "data i czas")]
        [InlineData("geom IS NULL", 0, "Nieznana kolumna")]
        public void TypeErrors_PointAtCulprit(string where, int position, string fragment)
        {
            using var context = TestModel.Create();
            var error = WhereFilter.Validate(where, WhereSchema.For<Budynek>(context));

            Assert.NotNull(error);
            Assert.Equal(position, error.Position);
            Assert.Contains(fragment, error.Message);
        }

        [Fact]
        public void EnumOrdering_IsRejected()
        {
            using var context = TestModel.Create();
            var error = WhereFilter.Validate("nazwa > 'StrefaOtwarta'", WhereSchema.For<StrefaPlanistyczna>(context));
            Assert.NotNull(error);
            Assert.Contains("tylko =", error.Message);

            Assert.NotNull(WhereFilter.Validate("nazwa = 'Nieznana'", WhereSchema.For<StrefaPlanistyczna>(context)));
        }

        [Fact]
        public void Schema_UsesColumnNamesWithoutGeometry()
        {
            using var context = TestModel.Create();
            var schema = WhereSchema.ForTable(context, "dzialka");

            Assert.Equal(typeof(Dzialka), schema.EntityType);
            Assert.Equal("oid", schema.Columns[0].Name);
            Assert.DoesNotContain(schema.Columns, c => c.Name == "geom");
            Assert.Equal(WhereColumnKind.Decimal, schema.Find("POLE_EWIDENCYJNE")!.Kind);
            Assert.False(schema.Find("id_dzialki")!.IsNullable);
            Assert.True(schema.Find("numer_obrebu")!.IsNullable);
            Assert.True(WhereSchema.For<StrefaPlanistyczna>(context).Find("poczatek_wersji_obiektu")!.IsUtc);
        }
    }

    /// <summary>Model EF do testów bez bazy: kontekst nie otwiera połączenia, dopóki nie wykona zapytania.</summary>
    internal static class TestModel
    {
        public static PostgresContext Create() => new(PostgresContext.CreateOptions("Host=localhost;Database=geobench_model"));

        public static SqlServerContext CreateSqlServer() => new(SqlServerContext.CreateOptions("Server=localhost;Database=geobench_model;TrustServerCertificate=True"));
    }
}
