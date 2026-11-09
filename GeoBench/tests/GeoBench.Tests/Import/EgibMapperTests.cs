using GeoBench.Core.Data;
using GeoBench.Core.Import;
using GeoBench.Core.Import.Egib;
using GeoBench.Core.Spatial;

namespace GeoBench.Tests.Import
{
    public class EgibMapperTests
    {
        [Fact]
        public void PowiatParcel_MapsAttributes()
        {
            var mapped = EgibMapper.MapDzialka(Single("wfs_powiat_dzialka_2180.gml"));
            var dzialka = mapped.Entity;

            Assert.Equal("246401_1.0219.37/6", dzialka.IdDzialki);
            Assert.Equal("37/6", dzialka.NumerDzialki);
            Assert.Equal("0219", dzialka.NumerObrebu);
            Assert.Equal("219", dzialka.NazwaObrebu);
            Assert.Equal("246401_1", dzialka.NumerJednostki);
            Assert.Equal("m. Częstochowa", dzialka.NazwaGminy);
            Assert.Null(dzialka.PoleEwidencyjne);
        }

        [Fact]
        public void Data_IsLocalWithoutTimeZone()
        {
            var dzialka = EgibMapper.MapDzialka(Single("wfs_powiat_dzialka_2180.gml")).Entity;

            Assert.Equal(new DateTime(2026, 11, 7, 1, 6, 13), dzialka.Data);
            // timestamp without time zone w PostgreSQL odrzuca DateTimeKind.Utc
            Assert.Equal(DateTimeKind.Unspecified, dzialka.Data!.Value.Kind);
        }

        [Theory]
        [InlineData("wfs_powiat_dzialka_2180.gml")]
        [InlineData("wfs_powiat_dzialka_2177.gml")]
        public void ParcelGeometry_IsIn2180WithEastingFirst(string fixture)
        {
            var mapped = EgibMapper.MapDzialka(Single(fixture));

            Assert.Equal(GeometryStatus.Valid, mapped.GeometryStatus);
            Assert.NotNull(mapped.Entity.Geom);
            Assert.Equal(GeoBenchContext.Srid, mapped.Entity.Geom.SRID);
            Assert.InRange(mapped.Entity.Geom.EnvelopeInternal.MinX, 514000, 515000);
            Assert.InRange(mapped.Entity.Geom.EnvelopeInternal.MinY, 324000, 325000);
        }

        [Fact]
        public void PoleEwidencyjne_ReadsAliasFromZbiorcza()
        {
            var record = WithProperty(Single("wfs_powiat_dzialka_2180.gml"), new FeatureProperty("POLE_EWIDENYJNE", "0.1234", null, null));

            Assert.Equal(0.1234m, EgibMapper.MapDzialka(record).Entity.PoleEwidencyjne);
        }

        [Fact]
        public void PowiatBuilding_MapsAttributes()
        {
            var budynek = EgibMapper.MapBudynek(Single("wfs_powiat_budynek.gml")).Entity;

            Assert.Equal("246401_1.0424.27_BUD", budynek.IdBudynku);
            Assert.Equal("m", budynek.Rodzaj);
            Assert.Null(budynek.KondygnacjeNadziemne);
            Assert.Equal(new DateTime(2026, 11, 7, 1, 0, 22), budynek.Data);
            Assert.NotNull(budynek.Geom);
        }

        [Fact]
        public void ZbiorczaBuilding_EmptyFloorsAreNull()
        {
            var budynek = EgibMapper.MapBudynek(Single("wfs_zbiorcza_budynek.gml")).Entity;

            Assert.Equal("246401_1.0182.197_BUD", budynek.IdBudynku);
            Assert.Null(budynek.KondygnacjeNadziemne);
            Assert.Null(budynek.KondygnacjePodziemne);
            Assert.NotNull(budynek.Geom);
        }

        [Theory]
        [InlineData("3", 3)]
        [InlineData("2.0", 2)]
        public void Floors_AcceptIntegralNumbers(string value, int expected)
        {
            var record = WithProperty(Single("wfs_powiat_budynek.gml"), new FeatureProperty("KONDYGNACJE_NADZIEMNE", value, null, null));

            Assert.Equal(expected, EgibMapper.MapBudynek(record).Entity.KondygnacjeNadziemne);
        }

        [Fact]
        public void FractionalFloors_AreRejected()
        {
            var record = WithProperty(Single("wfs_powiat_budynek.gml"), new FeatureProperty("KONDYGNACJE_NADZIEMNE", "2.5", null, null));

            Assert.Throws<FeatureMappingException>(() => EgibMapper.MapBudynek(record));
        }

        [Fact]
        public void MissingIdentifier_IsRejected()
        {
            var source = Single("wfs_powiat_dzialka_2180.gml");
            var record = new FeatureRecord(source.TypeName, source.GmlId,
                source.Properties.Where(p => p.Name != "ID_DZIALKI").ToList(), source.Geometries);

            Assert.Throws<FeatureMappingException>(() => EgibMapper.MapDzialka(record));
        }

        private static FeatureRecord Single(string fixture) => Assert.Single(FixtureFiles.Read(fixture));

        private static FeatureRecord WithProperty(FeatureRecord source, FeatureProperty property)
        {
            var properties = source.Properties
                .Where(p => !string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase))
                .Append(property)
                .ToList();
            return new FeatureRecord(source.TypeName, source.GmlId, properties, source.Geometries);
        }
    }
}
