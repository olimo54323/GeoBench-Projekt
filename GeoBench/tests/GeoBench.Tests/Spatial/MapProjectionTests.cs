using GeoBench.Core.Spatial;
using NetTopologySuite.Geometries;

namespace GeoBench.Tests.Spatial
{
    public class MapProjectionTests
    {
        private const double EarthRadius = 6378137;

        // Wierzchołek działki 246401_1.0219.37/6 w PL-1992 (X = wschód)
        private static readonly Coordinate Pl1992Point = new(514423.192872672, 324673.126452263);

        [Fact]
        public void ToWebMercator_MatchesSphericalMercatorFormula()
        {
            var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(2180);
            var point = factory.CreatePoint(Pl1992Point);

            var lonLat = CoordinateTransformer.Transform(point, new SpatialReference(2180, false), 4326).Coordinate;
            var mercator = MapProjection.ToWebMercator(point);

            // Częstochowa: ok. 19,1° E, 50,8° N
            Assert.InRange(lonLat.X, 19.0, 19.3);
            Assert.InRange(lonLat.Y, 50.7, 50.9);
            Assert.Equal(MapProjection.WebMercator, mercator.SRID);

            var expectedX = EarthRadius * lonLat.X * Math.PI / 180;
            var expectedY = EarthRadius * Math.Log(Math.Tan(Math.PI / 4 + lonLat.Y * Math.PI / 360));
            Assert.Equal(expectedX, mercator.Coordinate.X, 2);
            Assert.Equal(expectedY, mercator.Coordinate.Y, 2);
        }

        [Fact]
        public void EnvelopeRoundTrip_CoversOriginal()
        {
            var window = new Envelope(510000, 513000, 322000, 325000);

            var back = MapProjection.ToPl1992(MapProjection.ToWebMercator(window));

            // Obwiednia wygiętego czworoboku może być tylko większa, i to nieznacznie
            Assert.True(back.Covers(window), $"{back} nie obejmuje {window}");
            Assert.InRange(back.Width, window.Width, window.Width * 1.02);
            Assert.InRange(back.Height, window.Height, window.Height * 1.02);
        }

        [Fact]
        public void EmptyEnvelope_StaysEmpty()
        {
            Assert.True(MapProjection.ToPl1992(new Envelope()).IsNull);
        }
    }
}
