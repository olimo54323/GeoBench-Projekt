using GeoBench.Core.Data;
using GeoBench.Core.Storage;
using GeoBench.Tests.Benchmark;

namespace GeoBench.Tests.Data
{
    /// <summary>Zestawienie zakładki Dane na magazynie w pamięci: elementy warstwy, opcjonalna poprawność, błędy, zgodność.</summary>
    public class DataOverviewTests
    {
        private static FakeSpatialStore Store() => new(new()
        {
            [SpatialLayer.Budynki] =
            [
                new("b_ok", BenchmarkFixtures.Rectangle(0, 0, 5, 5)),
                new("b_zle", GeometryValidationTests.Bowtie(10, 0))
            ],
            [SpatialLayer.Strefy] = []
        })
        {
            Index = new SpatialIndexInfo("ix_budynek_geom", new Dictionary<string, string>(), 2 * 1024 * 1024, 10)
        };

        [Fact]
        public async Task Layer_CollectsCountExtentSizeIndex_AndValidity()
        {
            var layer = await DataOverview.LayerAsync(Store(), SpatialLayer.Budynki, checkValidity: true);

            Assert.Equal(2, layer.Count);
            Assert.Equal(new NetTopologySuite.Geometries.Envelope(0, 14, 0, 5), layer.Extent);
            Assert.Equal(1, layer.InvalidCount);
            Assert.Equal(2000, layer.TableSizeBytes);
            Assert.Equal("ix_budynek_geom", layer.Index?.Name);
            Assert.Null(layer.Error);
        }

        [Fact]
        public async Task Layer_WithoutValidityCheck_LeavesInvalidUnknown()
        {
            var layer = await DataOverview.LayerAsync(Store(), SpatialLayer.Budynki, checkValidity: false);

            Assert.Null(layer.InvalidCount);
            Assert.Equal(2, layer.Count);
        }

        [Fact]
        public async Task Layer_Empty_HasNoExtentAndNoError()
        {
            var layer = await DataOverview.LayerAsync(Store(), SpatialLayer.Strefy, checkValidity: true);

            Assert.Equal(0, layer.Count);
            Assert.Null(layer.Extent);
            Assert.Equal(0, layer.InvalidCount);
            Assert.Null(layer.Error);
        }

        [Fact]
        public async Task Layer_Missing_ReportsErrorInRow()
        {
            // Magazyn testowy nie ma działek: odczyt liczności się nie udaje, jak przy braku tabeli w bazie
            var layer = await DataOverview.LayerAsync(Store(), SpatialLayer.Dzialki, checkValidity: false);

            Assert.Null(layer.Count);
            Assert.NotNull(layer.Error);
        }

        [Fact]
        public async Task Layers_CoverThreeLayers()
        {
            var layers = await DataOverview.LayersAsync(Store(), checkValidity: false);

            Assert.Equal(DataOverview.Layers, layers.Select(l => l.Layer));
        }

        [Fact]
        public void CountMismatches_OnlyForLayersReadInBothDatabases()
        {
            static LayerOverview L(DatabaseEngine engine, SpatialLayer layer, long? count) =>
                new(engine, layer, count, null, null, null, null, count == null ? "błąd" : null);

            var mismatches = DataOverview.CountMismatches(
            [
                L(DatabaseEngine.SqlServer, SpatialLayer.Budynki, 62222), L(DatabaseEngine.Postgres, SpatialLayer.Budynki, 62222),
                L(DatabaseEngine.SqlServer, SpatialLayer.Dzialki, 83635), L(DatabaseEngine.Postgres, SpatialLayer.Dzialki, 83600),
                L(DatabaseEngine.SqlServer, SpatialLayer.Strefy, 2039), L(DatabaseEngine.Postgres, SpatialLayer.Strefy, null)
            ]);

            var mismatch = Assert.Single(mismatches);
            Assert.StartsWith("działki:", mismatch);
        }
    }
}
