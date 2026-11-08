using GeoBench.Core.Data;
using GeoBench.Core.Spatial;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace GeoBench.Tests.Spatial
{
    public class GeometryNormalizerTests
    {
        private readonly GeometryFactory _factory = NtsGeometryServices.Instance.CreateGeometryFactory(GeoBenchContext.Srid);

        [Fact]
        public void ValidPolygon_BecomesMultiPolygon()
        {
            var result = GeometryNormalizer.Normalize(Square(0, 0, 10));

            Assert.Equal(GeometryStatus.Valid, result.Status);
            Assert.NotNull(result.Geometry);
            Assert.Equal(GeoBenchContext.Srid, result.Geometry.SRID);
            Assert.Equal(1, result.Geometry.NumGeometries);
            Assert.Equal(100, result.Geometry.Area);
        }

        [Fact]
        public void SelfIntersectingPolygon_IsRepaired()
        {
            // "Motylek": pierścień przecina sam siebie w punkcie (5, 5)
            var bowtie = _factory.CreatePolygon(
            [
                new Coordinate(0, 0), new Coordinate(10, 10), new Coordinate(10, 0), new Coordinate(0, 10), new Coordinate(0, 0)
            ]);

            var result = GeometryNormalizer.Normalize(bowtie);

            Assert.Equal(GeometryStatus.Repaired, result.Status);
            Assert.NotNull(result.Geometry);
            Assert.True(result.Geometry.IsValid);
            Assert.Equal(50, result.Geometry.Area, 6);
            Assert.NotNull(result.Reason);
        }

        [Fact]
        public void OverlappingPolygons_AreMergedIntoValidMultiPolygon()
        {
            var overlapping = _factory.CreateMultiPolygon([Square(0, 0, 10), Square(5, 0, 10)]);

            var result = GeometryNormalizer.Normalize(overlapping);

            Assert.Equal(GeometryStatus.Repaired, result.Status);
            Assert.NotNull(result.Geometry);
            Assert.True(result.Geometry.IsValid);
            Assert.Equal(150, result.Geometry.Area, 6);
        }

        [Fact]
        public void LineString_IsRejected()
        {
            var line = _factory.CreateLineString([new Coordinate(0, 0), new Coordinate(10, 10)]);

            var result = GeometryNormalizer.Normalize(line);

            Assert.Equal(GeometryStatus.Rejected, result.Status);
            Assert.Null(result.Geometry);
        }

        [Fact]
        public void MissingGeometry_IsRejected()
        {
            var result = GeometryNormalizer.Normalize(null);

            Assert.Equal(GeometryStatus.Rejected, result.Status);
            Assert.Null(result.Geometry);
        }

        [Fact]
        public void GeometryInOtherSrid_Throws()
        {
            var factory2177 = NtsGeometryServices.Instance.CreateGeometryFactory(2177);
            var polygon = factory2177.CreatePolygon(
            [
                new Coordinate(0, 0), new Coordinate(10, 0), new Coordinate(10, 10), new Coordinate(0, 0)
            ]);

            Assert.ThrowsAny<ArgumentException>(() => GeometryNormalizer.Normalize(polygon));
        }

        private Polygon Square(double x, double y, double size) =>
            _factory.CreatePolygon(
            [
                new Coordinate(x, y), new Coordinate(x + size, y), new Coordinate(x + size, y + size),
                new Coordinate(x, y + size), new Coordinate(x, y)
            ]);
    }
}
