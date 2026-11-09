using GeoBench.Core.Import.Wfs;

namespace GeoBench.Tests.Wfs
{
    /// <summary>Cache w katalogu tymczasowym usuwanym po teście; strony to kopie fixture'ów.</summary>
    public sealed class WfsCacheTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"geobench_cache_{Guid.NewGuid():N}");
        private readonly WfsCache _cache;

        private static readonly WfsSourceOptions Source = new() { Name = "powiat-2464", Url = "https://example.test/wfs", PageSize = 1 };
        private static readonly WfsLayerOptions Layer = new() { Name = "dzialki", TypeName = "gmgml:dzialki", SortBy = "ID_DZIALKI" };

        public WfsCacheTests()
        {
            _cache = new WfsCache(_root);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        [Fact]
        public void Manifest_RoundTrip()
        {
            _cache.WriteManifest(Source, Layer, Manifest(numberMatched: 3));

            var manifest = _cache.ReadManifest(Source, Layer);

            Assert.NotNull(manifest);
            Assert.Equal(3, manifest.NumberMatched);
            Assert.Equal(3, manifest.PageCount);
            Assert.True(manifest.Matches(Source, Layer));
        }

        [Fact]
        public void PageCount_AndLastPageSize()
        {
            var manifest = new WfsManifest
            {
                Source = "s", Layer = "l", TypeName = "t", SrsName = "x", NumberMatched = 83635, PageSize = 5000
            };

            Assert.Equal(17, manifest.PageCount);
            Assert.Equal(5000, manifest.ExpectedCount(0));
            Assert.Equal(3635, manifest.ExpectedCount(16));
        }

        [Fact]
        public void MissingPages_AndCompleteness()
        {
            var manifest = Manifest(numberMatched: 2);
            _cache.WriteManifest(Source, Layer, manifest);
            CopyPage(0, "wfs_powiat_dzialka_2180.gml");

            Assert.Equal(new[] { 1 }, _cache.MissingPages(Source, Layer, manifest));
            Assert.False(_cache.IsComplete(Source, Layer));
            Assert.Throws<InvalidOperationException>(() => _cache.ReadFeatures(Source, Layer).ToList());

            CopyPage(1, "wfs_powiat_dzialka_2177.gml");

            Assert.True(_cache.IsComplete(Source, Layer));
        }

        [Fact]
        public void ReadFeatures_ReadsPagesInOrder()
        {
            _cache.WriteManifest(Source, Layer, Manifest(numberMatched: 2));
            CopyPage(0, "wfs_powiat_dzialka_2180.gml");
            CopyPage(1, "wfs_powiat_dzialka_2177.gml");

            var features = _cache.ReadFeatures(Source, Layer).ToList();

            Assert.Equal(2, features.Count);
            Assert.Equal("urn:ogc:def:crs:EPSG::2180", features[0].GetGeometry("geom")!.SrsName);
            Assert.Equal("urn:ogc:def:crs:EPSG::2177", features[1].GetGeometry("geom")!.SrsName);
        }

        [Fact]
        public void ChangedPageSize_InvalidatesManifest()
        {
            _cache.WriteManifest(Source, Layer, Manifest(numberMatched: 2));
            var bigger = new WfsSourceOptions { Name = Source.Name, Url = Source.Url, PageSize = 1000 };

            Assert.False(_cache.ReadManifest(Source, Layer)!.Matches(bigger, Layer));
        }

        private WfsManifest Manifest(int numberMatched) => new()
        {
            Source = Source.Name,
            Layer = Layer.Name,
            TypeName = Layer.TypeName,
            SrsName = Source.SrsName,
            SortBy = Layer.SortBy,
            NumberMatched = numberMatched,
            PageSize = Source.PageSize,
            CreatedUtc = DateTime.UtcNow
        };

        private void CopyPage(int page, string fixture) =>
            File.Copy(FixtureFiles.PathOf(fixture), _cache.PagePath(Source, Layer, page));
    }
}
