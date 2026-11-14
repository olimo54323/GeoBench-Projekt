using GeoBench.Core.Benchmark;
using GeoBench.Core.Benchmark.Scenarios;
using GeoBench.Core.Data;
using GeoBench.Core.Processing;
using GeoBench.Core.Storage;

namespace GeoBench.Tests.Benchmark
{
    /// <summary>Scenariusz 07 w NTS: granica 50 m włącznie, działka bez budynków z liczbą 0, narożnik po przekątnej.</summary>
    public class BuildingsWithinDistanceTests
    {
        /// <summary>p1 (0–100) z budynkami wokół, p2 daleko, bez budynków w promieniu.</summary>
        public static IReadOnlyList<SpatialFeature> Parcels(double dx = 0, double dy = 0) =>
        [
            new("p1", BenchmarkFixtures.Rectangle(0, 0, 100, 100, dx, dy)),
            new("p2", BenchmarkFixtures.Rectangle(1000, 0, 1100, 100, dx, dy))
        ];

        /// <summary>
        /// Na działce (0 m), dokładnie 50 m, 50,5 m, po przekątnej 42,4 m i 56,6 m
        /// (ten ostatni mieści się w obwiedni powiększonej o 50 m, ale odpada w dokładnym teście).
        /// </summary>
        public static IReadOnlyList<SpatialFeature> Buildings(double dx = 0, double dy = 0) =>
        [
            new("na_dzialce", BenchmarkFixtures.Rectangle(10, 10, 20, 20, dx, dy)),
            new("rowno_50", BenchmarkFixtures.Rectangle(150, 0, 160, 10, dx, dy)),
            new("dalej_50_5", BenchmarkFixtures.Rectangle(100, 150.5, 110, 160, dx, dy)),
            new("ukos_42", BenchmarkFixtures.Rectangle(130, 130, 140, 140, dx, dy)),
            new("ukos_57", BenchmarkFixtures.Rectangle(140, 140, 150, 150, dx, dy))
        ];

        public static readonly (string, string)[] Expected = [("p1", "3"), ("p2", "0")];

        public static (string, string)[] Sorted(IEnumerable<ResultPair> pairs) =>
            pairs.OrderBy(p => p.IdA, StringComparer.Ordinal).Select(p => (p.IdA, p.IdB)).ToArray();

        private static FakeSpatialStore Store() => new(new()
        {
            [SpatialLayer.Dzialki] = Parcels(),
            [SpatialLayer.Budynki] = Buildings()
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Nts_CountsWithinDistance_InclusiveWithZeros(bool parallel)
        {
            var run = await new NtsStrategy(parallel).RunAsync(new BuildingsWithinDistanceScenario(), Store());

            Assert.Equal(Expected, Sorted(run.Pairs));
            Assert.All(run.Pairs, p => Assert.Null(p.Measure));
        }

        [Fact]
        public void Describe_ReportsZerosAndDistribution()
        {
            var notes = new BuildingsWithinDistanceScenario().Describe([new("p1", "3"), new("p2", "0"), new("p3", "5")], countA: 3);

            Assert.StartsWith("działki: 3 z 3; bez budynku w promieniu 50 m: 1", notes[0]);
            Assert.Equal("budynków w promieniu: mediana 3, maksimum 5, razem par działka–budynek 8", notes[1]);
        }

        [Theory]
        [InlineData(DatabaseEngine.SqlServer, "STDistance(d.geom) <= 50")]
        [InlineData(DatabaseEngine.Postgres, "ST_DWithin(d.geom, b.geom, 50)")]
        public void Sql_IsEmbedded_WithDistanceCondition(DatabaseEngine engine, string condition)
        {
            var sql = ScenarioSql.Load(engine, new BuildingsWithinDistanceScenario().SqlFileName);

            Assert.Contains(condition, sql);
            Assert.DoesNotContain("Buffer", sql, StringComparison.OrdinalIgnoreCase);
        }
    }
}
