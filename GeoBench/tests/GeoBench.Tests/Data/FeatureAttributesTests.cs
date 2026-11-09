using System.Globalization;
using GeoBench.Core.Data;
using GeoBench.Core.Model;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace GeoBench.Tests.Data
{
    /// <summary>Pola obiektu z modelu EF i formaty kopiowania. Model powstaje bez połączenia z bazą.</summary>
    public class FeatureAttributesTests
    {
        private static readonly GeometryFactory Factory = NtsGeometryServices.Instance.CreateGeometryFactory(GeoBenchContext.Srid);

        private static PostgresContext CreateContext() => new(PostgresContext.CreateOptions("Host=localhost;Database=geobench_model"));

        private static MultiPolygon Square(double x, double y, double size) => Factory.CreateMultiPolygon(new[]
        {
            Factory.CreatePolygon(new[]
            {
                new Coordinate(x, y), new Coordinate(x + size, y), new Coordinate(x + size, y + size),
                new Coordinate(x, y + size), new Coordinate(x, y)
            })
        });

        private static Dzialka Parcel() => new()
        {
            Oid = 7,
            IdDzialki = "246401_1.0219.37/6",
            NumerDzialki = "37/6",
            NazwaObrebu = "Obręb\t219\nCzęstochowa",
            PoleEwidencyjne = 0.1234m,
            Data = new DateTime(2026, 11, 7, 1, 6, 13),
            Geom = Square(514000, 324000, 10)
        };

        [Fact]
        public void FromEntity_UsesColumnNamesInMigrationOrder()
        {
            using var context = CreateContext();

            var record = FeatureAttributes.FromEntity(context, Parcel(), "246401_1.0219.37/6");

            Assert.Equal("dzialka", record.Table);
            Assert.Equal(new[]
            {
                "oid", "id_dzialki", "numer_dzialki", "numer_obrebu", "nazwa_obrebu", "numer_jednostki", "nazwa_gminy",
                "pole_ewidencyjne", "klasouzytki_egib", "grupa_rejestrowa", "data",
                "geom", "geom.czesci", "geom.wierzcholki", "geom.powierzchnia_m2", "geom.obwod_m", "geom.obwiednia", "geom.poprawna"
            }, record.Fields.Select(f => f.Name));
            Assert.NotNull(record.Geometry);
        }

        [Fact]
        public void FromEntity_FormatsValuesIndependentOfCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
            try
            {
                using var context = CreateContext();
                var values = FeatureAttributes.FromEntity(context, Parcel(), "x").Fields.ToDictionary(f => f.Name, f => f.Value);

                Assert.Equal("7", values["oid"]);
                Assert.Equal("0.1234", values["pole_ewidencyjne"]);
                Assert.Equal("2026-11-07 01:06:13", values["data"]);
                Assert.Equal("", values["numer_obrebu"]);
                Assert.Equal("Obręb 219 Częstochowa", values["nazwa_obrebu"]);
                Assert.Equal("MultiPolygon (EPSG:2180)", values["geom"]);
                Assert.Equal("1", values["geom.czesci"]);
                Assert.Equal("5", values["geom.wierzcholki"]);
                Assert.Equal("100.00", values["geom.powierzchnia_m2"]);
                Assert.Equal("40.00", values["geom.obwod_m"]);
                Assert.Equal("514000.00 324000.00, 514010.00 324010.00", values["geom.obwiednia"]);
                Assert.Equal("true", values["geom.poprawna"]);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void FromEntity_ZoneEnumAndDateOnly()
        {
            using var context = CreateContext();
            var strefa = new StrefaPlanistyczna
            {
                Oznaczenie = "601SU",
                LokalnyId = "1POG-601SU",
                Symbol = "SU",
                Nazwa = RodzajStrefyPlanistycznej.StrefaUslugowa,
                MaksWysokoscZabudowy = 12.5m,
                ObowiazujeOd = new DateOnly(2026, 1, 31)
            };

            var record = FeatureAttributes.FromEntity(context, strefa, strefa.Oznaczenie);
            var values = record.Fields.ToDictionary(f => f.Name, f => f.Value);

            Assert.Equal("strefa_planistyczna", record.Table);
            Assert.Equal("StrefaUslugowa", values["nazwa"]);
            Assert.Equal("12.5", values["maks_wysokosc_zabudowy"]);
            Assert.Equal("2026-01-31", values["obowiazuje_od"]);
            Assert.Equal("", values["geom"]);
            Assert.False(values.ContainsKey("akt_planowania"));
            Assert.Null(record.Geometry);
        }

        [Fact]
        public void CopyFormats_UseTabsAndLineBreaks()
        {
            var first = new FeatureRecord("budynek", "a", new[] { new FeatureField("oid", "1"), new FeatureField("rodzaj", "m") }, null);
            var second = new FeatureRecord("budynek", "b", new[] { new FeatureField("oid", "2"), new FeatureField("geom", "MultiPolygon (EPSG:2180)") }, null);

            Assert.Equal("rodzaj\tm", FeatureAttributes.CopyField(first.Fields[1]));
            Assert.Equal("oid\t1\r\nrodzaj\tm", FeatureAttributes.CopyRecord(first));
            Assert.Equal("oid\trodzaj\tgeom\r\n1\tm\t\r\n2\t\tMultiPolygon (EPSG:2180)",
                FeatureAttributes.CopyTable(new[] { first, second }));
        }
    }
}
