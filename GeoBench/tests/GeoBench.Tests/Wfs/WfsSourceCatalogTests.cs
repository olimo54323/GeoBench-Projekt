using GeoBench.Core.Import.Wfs;

namespace GeoBench.Tests.Wfs
{
    /// <summary>Sprawdza plik sources.json z repozytorium (szukany w górę od katalogu testów).</summary>
    public class WfsSourceCatalogTests
    {
        [Fact]
        public void SourcesJson_ContainsPowiatWithParcelsAndBuildings()
        {
            var sources = WfsSourceCatalog.Load();

            var powiat = Assert.Single(sources, s => s.Name == "powiat-2464");
            Assert.Equal(5000, powiat.PageSize);
            Assert.Equal(2, powiat.MaxParallelRequests);

            var dzialki = Assert.Single(powiat.Layers, l => l.Target == EgibTarget.Dzialka);
            Assert.Equal("dzialki", dzialki.LocalTypeName);
            Assert.Equal("ID_DZIALKI", dzialki.SortBy);

            var budynki = Assert.Single(powiat.Layers, l => l.Target == EgibTarget.Budynek);
            Assert.Equal("budynki", budynki.LocalTypeName);
        }
    }
}
