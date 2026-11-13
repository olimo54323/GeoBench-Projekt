using System.Runtime.CompilerServices;
using GeoBench.Core.Benchmark;
using GeoBench.Core.Data;
using GeoBench.Core.Storage;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace GeoBench.Tests.Benchmark
{
    /// <summary>
    /// Wspólne dane testów scenariusza 4 (przesunięte o x0, y0, żeby w bazie leżały w zasięgu PL-1992):
    /// strefy A i B stykają się na x = 10, strefa N wypełnia wcięcie budynku w kształcie litery L.
    /// </summary>
    public static class BenchmarkFixtures
    {
        public static readonly GeometryFactory Factory = NtsGeometryServices.Instance.CreateGeometryFactory(GeoBenchContext.Srid);

        public static MultiPolygon Rectangle(double x0, double y0, double x1, double y1, double dx = 0, double dy = 0) =>
            Polygon(dx, dy, (x0, y0), (x1, y0), (x1, y1), (x0, y1));

        /// <summary>Litera L: centroid (3,22; 3,22) od narożnika leży poza budynkiem, we wcięciu.</summary>
        public static MultiPolygon LShape(double x, double y) =>
            Polygon(x, y, (0, 0), (10, 0), (10, 2), (2, 2), (2, 10), (0, 10));

        private static MultiPolygon Polygon(double dx, double dy, params (double X, double Y)[] points)
        {
            var coordinates = points.Append(points[0]).Select(p => new Coordinate(p.X + dx, p.Y + dy)).ToArray();
            return Factory.CreateMultiPolygon([Factory.CreatePolygon(coordinates)]);
        }

        /// <summary>Strefy: A (0–10), B (10–20) wspólna granica; N we wcięciu litery L (x 100–110).</summary>
        public static IReadOnlyList<SpatialFeature> Zones(double dx = 0, double dy = 0) =>
        [
            new("A", Rectangle(0, 0, 10, 10, dx, dy)),
            new("B", Rectangle(10, 0, 20, 10, dx, dy)),
            new("N", Rectangle(103, 3, 110, 10, dx, dy))
        ];

        /// <summary>Budynki: w środku A, na granicy A|B, poza planem i litera L (geometrycznie nie dotyka N).</summary>
        public static IReadOnlyList<SpatialFeature> Buildings(double dx = 0, double dy = 0) =>
        [
            new("srodek", Rectangle(2, 2, 4, 4, dx, dy)),
            new("granica", Rectangle(9, 4, 11, 6, dx, dy)),
            new("poza", Rectangle(50, 50, 52, 52, dx, dy)),
            new("L", LShape(100 + dx, dy))
        ];
    }

    /// <summary>Magazyn w pamięci dla testów strategii NTS bez bazy.</summary>
    public sealed class FakeSpatialStore : ISpatialStore
    {
        private readonly Dictionary<SpatialLayer, IReadOnlyList<SpatialFeature>> _layers;

        public FakeSpatialStore(IReadOnlyList<SpatialFeature> buildings, IReadOnlyList<SpatialFeature> zones)
        {
            _layers = new()
            {
                [SpatialLayer.Budynki] = buildings,
                [SpatialLayer.Strefy] = zones
            };
        }

        public FakeSpatialStore(Dictionary<SpatialLayer, IReadOnlyList<SpatialFeature>> layers)
        {
            _layers = layers;
        }

        public Task<IReadOnlyList<string>> InvalidIdsAsync(SpatialLayer layer, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(_layers[layer].Where(f => !f.Geometry.IsValid).Select(f => f.Id).ToList());

        public DatabaseEngine Engine => DatabaseEngine.Postgres;

        public GeoBenchContext Context => throw new NotSupportedException();

        public Task<long> CountAsync(SpatialLayer layer, CancellationToken cancellationToken = default) =>
            Task.FromResult((long)_layers[layer].Count);

        public async IAsyncEnumerable<SpatialFeature> ReadFeaturesAsync(SpatialLayer layer,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            foreach (var feature in _layers[layer])
                yield return feature;
        }

        public Task<IReadOnlyList<ResultPair>> ExecuteScenarioAsync(string sql, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<QueryDiagnostics> DiagnoseAsync(string sql, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<KeyValuePair<string, string>>> GetServerInfoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KeyValuePair<string, string>>>([]);
    }
}
