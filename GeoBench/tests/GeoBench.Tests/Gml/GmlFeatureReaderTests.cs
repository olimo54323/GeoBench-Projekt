namespace GeoBench.Tests.Gml
{
    public class GmlFeatureReaderTests
    {
        [Fact]
        public void PowiatParcel_ReadsAttributesWithoutCaseSensitivity()
        {
            var feature = Assert.Single(FixtureFiles.Read("wfs_powiat_dzialka_2180.gml"));

            Assert.Equal("dzialki", feature.TypeName);
            Assert.Equal("dzialki.246401_1_0219_37_6", feature.GmlId);
            Assert.Equal("246401_1.0219.37/6", feature.GetValue("id_dzialki"));
            Assert.Equal("m. Częstochowa", feature.GetValue("NAZWA_GMINY"));
            // Usługa pomija puste pola zamiast zwracać puste elementy
            Assert.Null(feature.GetValue("POLE_EWIDENCYJNE"));

            var geometry = feature.GetGeometry("GEOM");
            Assert.NotNull(geometry);
            Assert.Equal("urn:ogc:def:crs:EPSG::2180", geometry.SrsName);
        }

        [Fact]
        public void ZbiorczaBuilding_GeometryFirstAndEmptyElements()
        {
            var feature = Assert.Single(FixtureFiles.Read("wfs_zbiorcza_budynek.gml"));

            Assert.Equal("budynki", feature.TypeName);
            Assert.Equal("246401_1.0182.197_BUD", feature.GetValue("ID_BUDYNKU"));
            Assert.Equal("m", feature.GetValue("RODZAJ"));

            var floors = feature.Find("KONDYGNACJE_NADZIEMNE");
            Assert.NotNull(floors);
            Assert.Null(floors.Value);

            // gml:boundedBy nie trafia do atrybutów
            Assert.Null(feature.Find("lowerCorner"));
            Assert.Single(feature.Geometries);
            Assert.NotNull(feature.GetGeometry("geom"));
        }

        [Fact]
        public void PogFile_ReadsNestedAndXlinkProperties()
        {
            var features = FixtureFiles.Read("pog_strefy.gml");

            Assert.Equal(3, features.Count);
            Assert.Equal(2, features.Count(f => f.TypeName == "StrefaPlanistyczna"));

            var zone = features.Single(f => f.GetValue("oznaczenie") == "1SC");
            Assert.Equal("1POG-1SC", zone.GetValue("lokalnyId"));
            Assert.Equal("SC", zone.GetValue("symbol"));
            Assert.Equal("10.0", zone.GetValue("maksWysokoscZabudowy"));
            Assert.EndsWith("/strefaCmentarzy", zone.Find("nazwa")?.Href);

            var basicProfiles = zone.FindAll("profilPodstawowy").ToList();
            Assert.Equal(5, basicProfiles.Count);
            Assert.EndsWith("#KPT-MPZP-C", basicProfiles[0].Href);
            Assert.Equal("teren cmentarza", basicProfiles[0].Title);
            Assert.Equal(3, zone.FindAll("profilDodatkowy").Count());
            Assert.NotNull(zone.GetGeometry("geometria"));

            var ouz = features.Single(f => f.TypeName == "ObszarUzupelnieniaZabudowy");
            Assert.Equal("Obszar uzupełnienia zabudowy", ouz.GetValue("nazwa"));
            Assert.Equal("58OUZ", ouz.GetValue("oznaczenie"));
        }

        [Fact]
        public void Gml31FeatureMember_IsRead()
        {
            var feature = Assert.Single(FixtureFiles.Read("powierzchnia_z_lukiem.gml"));

            Assert.Equal("Polkole", feature.TypeName);
            Assert.Equal("polkole.1", feature.GmlId);
            Assert.Equal("półkole", feature.GetValue("nazwa"));
            Assert.NotNull(feature.GetGeometry("geometria"));
        }
    }
}
