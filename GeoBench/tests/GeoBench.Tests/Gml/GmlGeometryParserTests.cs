using System.Xml.Linq;
using GeoBench.Core.Import.Gml;
using NetTopologySuite.Geometries;

namespace GeoBench.Tests.Gml
{
    public class GmlGeometryParserTests
    {
        [Fact]
        public void PogZone_PolygonWithHole()
        {
            var zone = FixtureFiles.Read("pog_strefy.gml").Single(f => f.GetValue("oznaczenie") == "70SN");
            var source = zone.GetGeometry("geometria");

            Assert.NotNull(source);
            var polygon = Assert.IsType<Polygon>(source.Geometry);
            Assert.Equal(1, polygon.NumInteriorRings);
            Assert.Equal(0, source.ArcCount);
            Assert.Equal("http://www.opengis.net/def/crs/EPSG/0/2177", source.SrsName);
        }

        [Fact]
        public void SurfaceWithArc_IsLinearizedOnCircle()
        {
            var source = FixtureFiles.Read("powierzchnia_z_lukiem.gml").Single().GetGeometry("geometria");

            Assert.NotNull(source);
            Assert.Equal(1, source.ArcCount);
            var polygon = Assert.IsType<Polygon>(source.Geometry);
            var ring = polygon.ExteriorRing;
            Assert.True(ring.IsClosed);
            Assert.True(ring.NumPoints > 20, $"Za mało punktów łuku: {ring.NumPoints}");

            // Współrzędne w kolejności z pliku (X = północ): środek okręgu N=327000, E=508050, promień 50 m
            var center = new Coordinate(327000, 508050);
            foreach (var point in ring.Coordinates.Where(c => c.X > 327000 + 1e-9))
                Assert.InRange(point.Distance(center), 50 - 1e-6, 50 + 1e-6);

            // Odcinki leżą wewnątrz łuku, więc pole jest nieco mniejsze od pola półkola
            var semicircleArea = Math.PI * 50 * 50 / 2;
            Assert.InRange(polygon.Area, semicircleArea - 1, semicircleArea);
        }

        [Fact]
        public void ThreeDimensionalCoordinates_KeepOnlyXY()
        {
            var source = FixtureFiles.Read("powierzchnia_z_lukiem.gml").Single().GetGeometry("geometria");

            Assert.NotNull(source);
            Assert.All(source.Geometry.Coordinates, c => Assert.True(double.IsNaN(c.Z)));
        }

        [Fact]
        public void Gml2Coordinates_AreRead()
        {
            var element = XElement.Parse(
                """
                <gml:Polygon xmlns:gml="http://www.opengis.net/gml">
                  <gml:outerBoundaryIs><gml:LinearRing><gml:coordinates>0,0 10,0 10,10 0,10 0,0</gml:coordinates></gml:LinearRing></gml:outerBoundaryIs>
                </gml:Polygon>
                """);

            var source = new GmlGeometryParser().Parse(element);

            Assert.Equal(100, source.Geometry.Area);
            Assert.Null(source.SrsName);
        }

        [Fact]
        public void UnsupportedGeometry_Throws()
        {
            var element = XElement.Parse("""<gml:Point xmlns:gml="http://www.opengis.net/gml/3.2"><gml:pos>1 2</gml:pos></gml:Point>""");

            Assert.True(GmlGeometryParser.IsGeometryElement(element));
            Assert.Throws<NotSupportedException>(() => new GmlGeometryParser().Parse(element));
        }
    }
}
