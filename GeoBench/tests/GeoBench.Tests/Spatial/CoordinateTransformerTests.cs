using GeoBench.Core.Import;
using GeoBench.Core.Spatial;
using NetTopologySuite.Geometries;

namespace GeoBench.Tests.Spatial
{
    public class CoordinateTransformerTests
    {
        private const int Pl1992 = 2180;

        // Ta sama działka (246401_1.0219.37/6) pobrana z usługi powiatu w EPSG:2177 i EPSG:2180.
        // Współrzędne w 2177 są zaokrąglone do 1 cm, stąd tolerancja 2 cm; błąd osi lub parametrów
        // układu dałby różnice rzędu setek metrów.
        [Fact]
        public void Parcel2177_MatchesServerTransformationTo2180()
        {
            var transformed = To2180(ParcelGeometry("wfs_powiat_dzialka_2177.gml"));
            var reference = To2180(ParcelGeometry("wfs_powiat_dzialka_2180.gml"));

            Assert.Equal(Pl1992, transformed.SRID);
            Assert.Equal(reference.NumPoints, transformed.NumPoints);
            for (var i = 0; i < reference.NumPoints; i++)
            {
                var distance = transformed.Coordinates[i].Distance(reference.Coordinates[i]);
                Assert.True(distance < 0.02, $"Punkt {i}: różnica {distance:F4} m");
            }
        }

        [Fact]
        public void Urn2180_SwapsAxesToEastingFirst()
        {
            var geometry = To2180(ParcelGeometry("wfs_powiat_dzialka_2180.gml"));

            // W pliku: 324673.126452263 514423.192872672 (północ, wschód)
            Assert.Equal(514423.192872672, geometry.Coordinates[0].X, 6);
            Assert.Equal(324673.126452263, geometry.Coordinates[0].Y, 6);
        }

        [Fact]
        public void PogZone_LandsInCzestochowa()
        {
            var zone = FixtureFiles.Read("pog_strefy.gml").Single(f => f.GetValue("oznaczenie") == "1SC");
            var envelope = To2180(zone.GetGeometry("geometria")!).EnvelopeInternal;

            // Przybliżony zasięg Częstochowy w PL-1992
            Assert.InRange(envelope.MinX, 495000, 525000);
            Assert.InRange(envelope.MaxX, 495000, 525000);
            Assert.InRange(envelope.MinY, 315000, 340000);
            Assert.InRange(envelope.MaxY, 315000, 340000);
        }

        [Fact]
        public void RoundTrip2180To2177_ReturnsSameCoordinates()
        {
            var original = To2180(ParcelGeometry("wfs_powiat_dzialka_2180.gml"));

            var in2177 = CoordinateTransformer.Transform(original, new SpatialReference(Pl1992, false), 2177);
            var back = CoordinateTransformer.Transform(in2177, new SpatialReference(2177, false), Pl1992);

            for (var i = 0; i < original.NumPoints; i++)
                Assert.True(back.Coordinates[i].Distance(original.Coordinates[i]) < 0.001);
        }

        [Fact]
        public void Transform_DoesNotModifySource()
        {
            var source = ParcelGeometry("wfs_powiat_dzialka_2177.gml");
            var before = source.Geometry.Coordinates[0].Copy();

            To2180(source);

            Assert.True(source.Geometry.Coordinates[0].Equals2D(before));
        }

        private static SourceGeometry ParcelGeometry(string fixture) =>
            FixtureFiles.Read(fixture).Single().GetGeometry("geom")!;

        private static Geometry To2180(SourceGeometry source) =>
            CoordinateTransformer.Transform(source.Geometry, SpatialReference.Parse(source.SrsName!), Pl1992);
    }
}
