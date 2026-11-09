using GeoBench.Core.Import.Wfs;

namespace GeoBench.Tests.Wfs
{
    public class WfsClientTests
    {
        private static readonly WfsSourceOptions Source = new()
        {
            Name = "powiat-2464",
            Url = "https://geoportal.czestochowa.pl/wfs2464_egib/service.svc/get",
            PageSize = 5000,
            Namespaces = "xmlns(gmgml,http://www.intergraph.com/geomedia/gml)"
        };

        private static readonly WfsLayerOptions Layer = new()
        {
            Name = "dzialki",
            TypeName = "gmgml:dzialki",
            Target = EgibTarget.Dzialka,
            SortBy = "ID_DZIALKI"
        };

        [Fact]
        public void GetFeatureUri_ContainsPagingSortingAndNamespaces()
        {
            var uri = WfsClient.BuildGetFeatureUri(Source, Layer, 10000, 5000);
            var query = Uri.UnescapeDataString(uri.Query);

            Assert.StartsWith(Source.Url + "?", uri.AbsoluteUri);
            Assert.Contains("REQUEST=GetFeature", query);
            Assert.Contains("TYPENAMES=gmgml:dzialki", query);
            Assert.Contains("SRSNAME=urn:ogc:def:crs:EPSG::2180", query);
            Assert.Contains("COUNT=5000", query);
            Assert.Contains("STARTINDEX=10000", query);
            Assert.Contains("SORTBY=ID_DZIALKI", query);
            Assert.Contains("NAMESPACES=xmlns(gmgml,http://www.intergraph.com/geomedia/gml)", query);
        }

        [Fact]
        public void GetFeatureUri_AppendsToExistingQuery()
        {
            var source = new WfsSourceOptions { Name = "x", Url = "https://example.test/wfs?map=egib", ExtraQuery = "&BBOX=1,2,3,4" };

            var uri = WfsClient.BuildGetFeatureUri(source, Layer, 0, 1);

            Assert.Contains("?map=egib&SERVICE=WFS", uri.AbsoluteUri);
            Assert.EndsWith("&BBOX=1,2,3,4", uri.AbsoluteUri);
        }

        [Fact]
        public void ValidatePage_ReturnsNumberReturned()
        {
            Assert.Equal(1, WfsClient.ValidatePage(FixtureFiles.PathOf("wfs_powiat_dzialka_2180.gml")));
        }

        [Fact]
        public void ValidatePage_ExceptionReport_Throws()
        {
            var path = Path.Combine(Path.GetTempPath(), $"geobench_{Guid.NewGuid():N}.gml");
            File.WriteAllText(path,
                """
                <ows:ExceptionReport xmlns:ows="http://www.opengis.net/ows/1.1" version="2.0.0">
                  <ows:Exception exceptionCode="InvalidParameterValue"><ows:ExceptionText>Błąd parsowania filtra</ows:ExceptionText></ows:Exception>
                </ows:ExceptionReport>
                """);
            try
            {
                var ex = Assert.Throws<WfsException>(() => WfsClient.ValidatePage(path));
                Assert.Contains("Błąd parsowania filtra", ex.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ValidatePage_TruncatedDocument_ThrowsXmlException()
        {
            var path = Path.Combine(Path.GetTempPath(), $"geobench_{Guid.NewGuid():N}.gml");
            var content = File.ReadAllText(FixtureFiles.PathOf("wfs_powiat_dzialka_2180.gml"));
            File.WriteAllText(path, content[..(content.Length / 2)]);
            try
            {
                Assert.Throws<System.Xml.XmlException>(() => WfsClient.ValidatePage(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
