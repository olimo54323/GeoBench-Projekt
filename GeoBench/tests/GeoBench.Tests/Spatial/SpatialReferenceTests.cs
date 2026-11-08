using GeoBench.Core.Spatial;

namespace GeoBench.Tests.Spatial
{
    public class SpatialReferenceTests
    {
        [Theory]
        [InlineData("urn:ogc:def:crs:EPSG::2180", 2180, true)]
        [InlineData("urn:ogc:def:crs:EPSG:6.9:2180", 2180, true)]
        [InlineData("http://www.opengis.net/def/crs/EPSG/0/2177", 2177, true)]
        [InlineData("urn:ogc:def:crs:EPSG::4326", 4326, true)]
        [InlineData("EPSG:2180", 2180, false)]
        [InlineData("EPSG:4326", 4326, false)]
        [InlineData("http://www.opengis.net/gml/srs/epsg.xml#2180", 2180, false)]
        [InlineData("urn:ogc:def:crs:EPSG::3857", 3857, false)]
        public void Parse_ReadsCodeAndAxisOrder(string srsName, int expectedEpsg, bool expectedNorthingFirst)
        {
            var reference = SpatialReference.Parse(srsName);

            Assert.Equal(expectedEpsg, reference.Epsg);
            Assert.Equal(expectedNorthingFirst, reference.NorthingFirst);
        }

        [Theory]
        [InlineData("CRS:84")]
        [InlineData("urn:ogc:def:crs:EPSG::abc")]
        public void Parse_UnknownForm_Throws(string srsName)
        {
            Assert.Throws<FormatException>(() => SpatialReference.Parse(srsName));
        }
    }
}
