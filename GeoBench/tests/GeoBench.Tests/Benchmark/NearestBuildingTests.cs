using GeoBench.Core.Benchmark;
using GeoBench.Core.Benchmark.Scenarios;
using GeoBench.Core.Data;
using GeoBench.Core.Processing;
using GeoBench.Core.Storage;

namespace GeoBench.Tests.Benchmark
{
    /// <summary>Scenariusz 06 w NTS: najbliższy budynek, remis przy odległości 0 rozstrzyga mniejsze id.</summary>
    public class NearestBuildingTests
    {
        /// <summary>
        /// b1 styka się bokiem z b0 i b2, b0 i b2 stykają się narożnikiem (10, 10): wszystkie odległości 0, więc remisy.
        /// c i d leżą osobno, 8 m od siebie.
        /// </summary>
        public static IReadOnlyList<SpatialFeature> Buildings(double dx = 0, double dy = 0) =>
        [
            new("b2", BenchmarkFixtures.Rectangle(10, 0, 20, 10, dx, dy)),
            new("b1", BenchmarkFixtures.Rectangle(0, 0, 10, 10, dx, dy)),
            new("b0", BenchmarkFixtures.Rectangle(0, 10, 10, 20, dx, dy)),
            new("c", BenchmarkFixtures.Rectangle(50, 0, 52, 2, dx, dy)),
            new("d", BenchmarkFixtures.Rectangle(50, 10, 52, 12, dx, dy))
        ];

        public static readonly (string, string, double)[] Expected =
        [
            ("b0", "b1", 0), ("b1", "b0", 0), ("b2", "b0", 0), ("c", "d", 8), ("d", "c", 8)
        ];

        public static (string, string, double)[] Sorted(IEnumerable<ResultPair> pairs) =>
            pairs.OrderBy(p => p.IdA, StringComparer.Ordinal)
                .Select(p => (p.IdA, p.IdB, Math.Round(p.Measure ?? -1, 6))).ToArray();

        private static FakeSpatialStore Store() => new(new() { [SpatialLayer.Budynki] = Buildings() });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Nts_FindsNearest_TieBreaksBySmallerId(bool parallel)
        {
            var run = await new NtsStrategy(parallel).RunAsync(new NearestBuildingScenario(), Store());

            Assert.Equal(Expected, Sorted(run.Pairs));
        }

        [Fact]
        public async Task Nts_SingleBuilding_HasNoPair()
        {
            var store = new FakeSpatialStore(new() { [SpatialLayer.Budynki] = [Buildings()[0]] });

            var run = await new NtsStrategy(false).RunAsync(new NearestBuildingScenario(), store);

            Assert.Empty(run.Pairs);
        }

        [Fact]
        public void Describe_CountsTouchingAndDistances()
        {
            var notes = new NearestBuildingScenario().Describe(
                [new("b0", "b1", 0), new("b1", "b0", 0), new("c", "d", 8)], countA: 4);

            Assert.StartsWith("pary: 3 z 4 budynków; stykające się z sąsiadem (odległość 0): 2", notes[0]);
        }

        [Theory]
        [InlineData(DatabaseEngine.SqlServer, "Latin1_General_BIN2")]
        [InlineData(DatabaseEngine.Postgres, "COLLATE \"C\"")]
        public void Sql_IsEmbedded_WithBinaryTieBreak(DatabaseEngine engine, string collation)
        {
            var sql = ScenarioSql.Load(engine, new NearestBuildingScenario().SqlFileName);

            Assert.Contains(collation, sql);
            Assert.Contains("id_a", sql);
        }
    }
}
