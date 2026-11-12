using GeoBench.Core.Benchmark;
using GeoBench.Core.Benchmark.Scenarios;
using GeoBench.Core.Data;
using GeoBench.Core.Processing;
using GeoBench.Core.Storage;
using GeoBench.Core.Validation;

namespace GeoBench.Tests.Benchmark
{
    /// <summary>Scenariusz 4 w NTS na magazynie w pamięci: semantyka centroidu i Intersects, zgodność wersji wielowątkowej.</summary>
    public class PointInPolygonNtsTests
    {
        private static readonly PointInPolygonScenario Scenario = new();

        private static readonly (string, string)[] Expected =
        [
            ("L", "N"),
            ("granica", "A"),
            ("granica", "B"),
            ("srodek", "A")
        ];

        private static (string, string)[] Sorted(IEnumerable<ResultPair> pairs) =>
            pairs.Select(p => (p.IdA, p.IdB)).OrderBy(p => p.IdA, StringComparer.Ordinal).ThenBy(p => p.IdB, StringComparer.Ordinal).ToArray();

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CentroidOnBoundary_BelongsToBothZones_LShapeUsesCentroid(bool parallel)
        {
            var store = new FakeSpatialStore(BenchmarkFixtures.Buildings(), BenchmarkFixtures.Zones());

            var run = await new NtsStrategy(parallel).RunAsync(Scenario, store);

            Assert.Equal(Expected, Sorted(run.Pairs));
            Assert.Equal(new[] { NtsStrategy.ReadPhase, NtsStrategy.IndexPhase, NtsStrategy.ComputePhase }, run.Phases.Select(p => p.Phase));
        }

        [Fact]
        public async Task ParallelAndSequential_GiveSameFingerprint()
        {
            // Siatka 60 × 40 budynków przez strefy A i B (część poza planem)
            var buildings = new List<SpatialFeature>();
            for (var i = 0; i < 60; i++)
                for (var j = 0; j < 40; j++)
                    buildings.Add(new SpatialFeature($"b{i}_{j}", BenchmarkFixtures.Rectangle(i * 0.4, j * 0.4, i * 0.4 + 0.3, j * 0.4 + 0.3)));
            var store = new FakeSpatialStore(buildings, BenchmarkFixtures.Zones());

            var sequential = await new NtsStrategy(false).RunAsync(Scenario, store);
            var parallel = await new NtsStrategy(true).RunAsync(Scenario, store);

            Assert.NotEmpty(sequential.Pairs);
            Assert.Equal(ResultFingerprint.Compute(sequential.Pairs), ResultFingerprint.Compute(parallel.Pairs));
        }

        [Fact]
        public void Describe_CountsBuildingsOutsideAndInSeveralZones()
        {
            var pairs = Expected.Select(p => new ResultPair(p.Item1, p.Item2)).ToList();

            var notes = Scenario.Describe(pairs, countA: 4);

            Assert.Equal(new[]
            {
                "budynki w strefach: 3 z 4",
                "budynki bez strefy (centroid poza planem): 1",
                "budynki w kilku strefach (centroid na granicy albo strefy nakładające się): 1"
            }, notes);
        }

        [Fact]
        public void Variants_HaveUniqueAsciiCodes()
        {
            var codes = VariantFactory.All.Select(v => v.Code).ToList();

            Assert.Equal(codes.Count, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(codes, c => Assert.True(c.All(char.IsAscii) && c.Length <= 16, c));
            Assert.Equal(2, VariantFactory.All.Count(v => v.Strategy.InDatabase));
        }

        [Theory]
        [InlineData(DatabaseEngine.SqlServer, "STIntersects")]
        [InlineData(DatabaseEngine.Postgres, "ST_Intersects")]
        public void ScenarioSql_IsEmbedded_WithoutTrailingSemicolon(DatabaseEngine engine, string function)
        {
            var sql = ScenarioSql.Load(engine, Scenario.SqlFileName);

            Assert.Contains(function, sql);
            Assert.Contains("id_a", sql);
            Assert.False(sql.EndsWith(';'));
        }
    }
}
