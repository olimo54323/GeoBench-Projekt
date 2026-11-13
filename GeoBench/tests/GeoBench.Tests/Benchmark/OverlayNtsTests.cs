using GeoBench.Core.Benchmark;
using GeoBench.Core.Benchmark.Scenarios;
using GeoBench.Core.Data;
using GeoBench.Core.Processing;
using GeoBench.Core.Storage;
using GeoBench.Core.Validation;
using NetTopologySuite.Geometries;

namespace GeoBench.Tests.Benchmark
{
    /// <summary>
    /// Scenariusze nakładania w NTS na magazynie w pamięci: próg pola, styczność, naprawa albo odrzucenie
    /// niepoprawnej geometrii, zgodność wersji wielowątkowej.
    /// </summary>
    public class OverlayNtsTests
    {
        /// <summary>Kokardka (samoprzecięcie w punkcie 3,3): niepoprawna, po naprawie dwa trójkąty o łącznym polu 8 m².</summary>
        private static MultiPolygon Bowtie(double x, double y)
        {
            var f = BenchmarkFixtures.Factory;
            var ring = new[]
            {
                new Coordinate(x, y), new Coordinate(x + 4, y + 4), new Coordinate(x + 4, y),
                new Coordinate(x, y + 4), new Coordinate(x, y)
            };
            return f.CreateMultiPolygon([f.CreatePolygon(ring)]);
        }

        /// <summary>Działki: wewnątrz A, na granicy A|B, styczna z B, wąski pasek w B (pole 0,005 m²), kokardka w A.</summary>
        private static IReadOnlyList<SpatialFeature> Parcels() =>
        [
            new("wewnatrz", BenchmarkFixtures.Rectangle(2, 2, 8, 8)),
            new("granica", BenchmarkFixtures.Rectangle(8, 0, 12, 10)),
            new("styk", BenchmarkFixtures.Rectangle(20, 0, 25, 10)),
            new("pasek", BenchmarkFixtures.Rectangle(19.9995, 0, 21, 10)),
            new("kokardka", Bowtie(1, 1))
        ];

        private static FakeSpatialStore Store(IReadOnlyList<SpatialFeature> parcels) => new(new()
        {
            [SpatialLayer.Dzialki] = parcels,
            [SpatialLayer.Strefy] = BenchmarkFixtures.Zones()
        });

        private static OverlayScenario<Core.Model.DzialkaStrefaPow> Scenario(bool repair)
        {
            var scenario = OverlayScenarios.DzialkiStrefy;
            scenario.Configure(new BenchmarkOptions { RepairInvalidGeometries = repair });
            return scenario;
        }

        private static (string, string, double)[] Sorted(IEnumerable<ResultPair> pairs) =>
            pairs.OrderBy(p => p.IdA, StringComparer.Ordinal).ThenBy(p => p.IdB, StringComparer.Ordinal)
                .Select(p => (p.IdA, p.IdB, Math.Round(p.Measure ?? -1, 6))).ToArray();

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task WithoutRepair_InvalidRejected_TouchAndSliverSkipped(bool parallel)
        {
            var run = await new NtsStrategy(parallel).RunAsync(Scenario(repair: false), Store(Parcels()));

            Assert.Equal(new[]
            {
                ("granica", "A", 20.0),
                ("granica", "B", 20.0),
                ("wewnatrz", "A", 36.0)
            }, Sorted(run.Pairs));
            Assert.Equal(new[] { NtsStrategy.ReadPhase, NtsStrategy.PreparePhase, NtsStrategy.IndexPhase, NtsStrategy.ComputePhase },
                run.Phases.Select(p => p.Phase));
        }

        [Fact]
        public async Task WithRepair_BowtieIsFixed()
        {
            var run = await new NtsStrategy(false).RunAsync(Scenario(repair: true), Store(Parcels()));

            Assert.Contains(("kokardka", "A", 8.0), Sorted(run.Pairs));
            Assert.Equal(4, run.Pairs.Count);
        }

        [Fact]
        public async Task AreaAtThreshold_IsIncluded()
        {
            // Pasek 0,001 m × 10 m = 0,01 m² w strefie B
            var parcels = new[] { new SpatialFeature("prog", BenchmarkFixtures.Rectangle(19.999, 0, 21, 10)) };

            var run = await new NtsStrategy(false).RunAsync(Scenario(repair: false), Store(parcels));

            var pair = Assert.Single(run.Pairs);
            Assert.Equal("B", pair.IdB);
            Assert.True(pair.Measure >= OverlayScenario<Core.Model.DzialkaStrefaPow>.MinArea);
        }

        [Fact]
        public async Task ParallelAndSequential_GiveSameFingerprint()
        {
            var parcels = new List<SpatialFeature>();
            for (var i = 0; i < 40; i++)
                for (var j = 0; j < 30; j++)
                    parcels.Add(new SpatialFeature($"d{i}_{j}", BenchmarkFixtures.Rectangle(i * 0.6, j * 0.5, i * 0.6 + 0.7, j * 0.5 + 0.6)));
            var scenario = Scenario(repair: false);

            var sequential = await new NtsStrategy(false).RunAsync(scenario, Store(parcels));
            var parallel = await new NtsStrategy(true).RunAsync(scenario, Store(parcels));

            Assert.NotEmpty(sequential.Pairs);
            var a = ResultFingerprint.Compute(sequential.Pairs);
            var b = ResultFingerprint.Compute(parallel.Pairs);
            Assert.Equal(a.Sha256, b.Sha256);
            Assert.Equal(a.MeasureSum!.Value, b.MeasureSum!.Value, 6);
        }

        [Fact]
        public void Describe_ReportsAreaAndMode()
        {
            var notes = Scenario(repair: true).Describe([new("d1", "A", 10), new("d1", "B", 2.5), new("d2", "A", 1)], countA: 5);

            Assert.StartsWith("pary: 3, działki z przecięciem: 2 z 5", notes[0]);
            Assert.Equal("niepoprawne geometrie naprawiane", notes[2]);
        }

        [Fact]
        public void Scenarios_HaveSqlForBothEnginesAndModes()
        {
            foreach (var scenario in OverlayScenarios.All)
                foreach (var repair in new[] { false, true })
                {
                    scenario.Configure(new BenchmarkOptions { RepairInvalidGeometries = repair });
                    foreach (var engine in new[] { DatabaseEngine.SqlServer, DatabaseEngine.Postgres })
                    {
                        var sql = ScenarioSql.Load(engine, scenario.SqlFileName);
                        Assert.Contains(">= 0.01", sql);
                        Assert.Equal(repair, sql.Contains("MakeValid", StringComparison.OrdinalIgnoreCase)
                            && sql.Contains(engine == DatabaseEngine.SqlServer ? ".MakeValid()" : "ST_MakeValid("));
                    }
                }
            Assert.Equal(new[] { "05a", "05b", "05c" }, OverlayScenarios.All.Select(s => s.Id));
        }
    }
}
