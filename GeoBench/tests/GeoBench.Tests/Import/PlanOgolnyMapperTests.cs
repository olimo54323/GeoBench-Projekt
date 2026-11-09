using GeoBench.Core.Data;
using GeoBench.Core.Import;
using GeoBench.Core.Import.PlanOgolny;
using GeoBench.Core.Model;
using GeoBench.Core.Spatial;

namespace GeoBench.Tests.Import
{
    public class PlanOgolnyMapperTests
    {
        [Fact]
        public void Strefa1SC_MapsAttributesAndCodes()
        {
            var mapped = PlanOgolnyMapper.MapStrefa(Strefa("1SC"));
            var strefa = mapped.Entity;

            Assert.Equal("1SC", strefa.Oznaczenie);
            Assert.Equal("1POG-1SC", strefa.LokalnyId);
            Assert.Equal("SC", strefa.Symbol);
            Assert.Equal(RodzajStrefyPlanistycznej.StrefaCmentarzy, strefa.Nazwa);
            Assert.Equal("elaboration", strefa.Status);
            Assert.Equal("generallyBinding", strefa.CharakterUstalenia);
            Assert.Equal(0.1m, strefa.MaksNadziemnaIntensywnoscZabudowy);
            Assert.Equal(10.0m, strefa.MaksUdzialPowierzchniZabudowy);
            Assert.Equal(10.0m, strefa.MaksWysokoscZabudowy);
            Assert.Equal(25.0m, strefa.MinUdzialPowierzchniBiologicznieCzynnej);
            Assert.Equal(new DateOnly(2025, 7, 4), strefa.ObowiazujeOd);
        }

        [Fact]
        public void PoczatekWersjiObiektu_IsUtc()
        {
            var strefa = PlanOgolnyMapper.MapStrefa(Strefa("1SC")).Entity;

            Assert.Equal(new DateTime(2025, 7, 4, 11, 26, 13, DateTimeKind.Utc), strefa.PoczatekWersjiObiektu);
            Assert.Equal(DateTimeKind.Utc, strefa.PoczatekWersjiObiektu!.Value.Kind);
        }

        [Fact]
        public void Strefa70SN_GeometryIn2180WithHole()
        {
            var mapped = PlanOgolnyMapper.MapStrefa(Strefa("70SN"));

            Assert.Equal(RodzajStrefyPlanistycznej.StrefaZieleniIRekreacji, mapped.Entity.Nazwa);
            Assert.Equal(GeometryStatus.Valid, mapped.GeometryStatus);
            Assert.Equal(0, mapped.ArcCount);
            var geometry = mapped.Entity.Geom;
            Assert.NotNull(geometry);
            Assert.Equal(GeoBenchContext.Srid, geometry.SRID);
            Assert.Equal(1, geometry.NumGeometries);
            Assert.Equal(1, ((NetTopologySuite.Geometries.Polygon)geometry.GetGeometryN(0)).NumInteriorRings);
            // Po zamianie osi X to wschód: Częstochowa w PL-1992 ma X ok. 500 tys., Y ok. 320–340 tys.
            Assert.InRange(geometry.EnvelopeInternal.MinX, 495000, 525000);
            Assert.InRange(geometry.EnvelopeInternal.MinY, 315000, 340000);
        }

        [Fact]
        public void Akt_MapsIdentifierAndExtent()
        {
            var record = FixtureFiles.Read("pog_plan_testowy.gml").Single(f => f.TypeName == PlanOgolnyMapper.AktTypeName);

            var mapped = PlanOgolnyMapper.MapAkt(record, daneBadawcze: true);
            var akt = mapped.Entity;

            Assert.Equal("PL.ZIPPZP.TEST/246401-POG", akt.PrzestrzenNazw);
            Assert.Equal("1POG", akt.LokalnyId);
            Assert.Equal("POG miasta Częstochowy", akt.TytulAlternatywny);
            Assert.Equal("elaboration", akt.Status);
            Assert.Equal(new DateOnly(2026, 7, 14), akt.ObowiazujeOd);
            Assert.True(akt.DaneBadawcze);
            Assert.Equal(GeometryStatus.Valid, mapped.GeometryStatus);
            Assert.NotNull(akt.Zasieg);
            Assert.Equal(GeoBenchContext.Srid, akt.Zasieg.SRID);
        }

        [Fact]
        public void PlanReference_PointsToAct()
        {
            Assert.EndsWith("/AktPlanowaniaPrzestrzennego/PL.ZIPPZP.9398/246401-POG/1POG", PlanOgolnyMapper.PlanReference(Strefa("1SC")));
        }

        [Fact]
        public void UnknownZoneKind_IsRejected()
        {
            var record = WithProperty(Strefa("1SC"), "nazwa",
                new FeatureProperty("nazwa", null, "https://www.gov.pl/static/zagospodarowanieprzestrzenne/codelist/RodzajStrefyPlanistycznejKod/strefaNieistniejaca", null));

            var ex = Assert.Throws<FeatureMappingException>(() => PlanOgolnyMapper.MapStrefa(record));
            Assert.Contains("strefaNieistniejaca", ex.Message);
        }

        [Fact]
        public void MissingOznaczenie_IsRejected()
        {
            var record = WithProperty(Strefa("1SC"), "oznaczenie", null);

            Assert.Throws<FeatureMappingException>(() => PlanOgolnyMapper.MapStrefa(record));
        }

        [Fact]
        public void InvalidNumber_IsRejected()
        {
            var record = WithProperty(Strefa("1SC"), "maksWysokoscZabudowy", new FeatureProperty("maksWysokoscZabudowy", "10,5", null, null));

            Assert.Throws<FeatureMappingException>(() => PlanOgolnyMapper.MapStrefa(record));
        }

        [Fact]
        public void MissingGeometry_KeepsZoneWithoutGeometry()
        {
            var source = Strefa("1SC");
            var record = new FeatureRecord(source.TypeName, source.GmlId, source.Properties, new Dictionary<string, SourceGeometry>());

            var mapped = PlanOgolnyMapper.MapStrefa(record);

            Assert.Null(mapped.Entity.Geom);
            Assert.Equal(GeometryStatus.Rejected, mapped.GeometryStatus);
            Assert.NotNull(mapped.GeometryReason);
        }

        private static FeatureRecord Strefa(string oznaczenie) =>
            FixtureFiles.Read("pog_strefy.gml").Single(f => f.TypeName == PlanOgolnyMapper.StrefaTypeName && f.GetValue("oznaczenie") == oznaczenie);

        /// <summary>Kopia obiektu z podmienioną (albo usuniętą, gdy null) właściwością.</summary>
        private static FeatureRecord WithProperty(FeatureRecord source, string name, FeatureProperty? replacement)
        {
            var properties = source.Properties
                .Where(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (replacement != null)
                properties.Add(replacement);
            return new FeatureRecord(source.TypeName, source.GmlId, properties, source.Geometries);
        }
    }
}
