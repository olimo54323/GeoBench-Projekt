using GeoBench.Core.Benchmark;
using GeoBench.Core.Benchmark.Measurement;
using GeoBench.Core.Benchmark.Scenarios;
using GeoBench.Core.Data;
using GeoBench.Core.Processing;
using GeoBench.Core.Reporting;
using GeoBench.Core.Storage;
using GeoBench.Core.Validation;
using NetTopologySuite.Geometries;

namespace GeoBench.Tests.Benchmark
{
    /// <summary>Scenariusz 3: powtarzalne okna w zasięgu, wynik NTS zgodny z pełnym przeglądem, zapytanie diagnostyczne, wykres.</summary>
    public class WindowQueryTests
    {
        private static readonly Envelope Extent = new(500000, 520000, 300000, 315000);

        /// <summary>Siatka działek 50 × 50 m w zasięgu 3 × 3 km.</summary>
        private static IReadOnlyList<SpatialFeature> Parcels()
        {
            var parcels = new List<SpatialFeature>();
            for (var i = 0; i < 60; i++)
                for (var j = 0; j < 60; j++)
                    parcels.Add(new SpatialFeature($"d{i}_{j}",
                        BenchmarkFixtures.Rectangle(i * 50, j * 50, i * 50 + 50, j * 50 + 50, 500000, 300000)));
            return parcels;
        }

        private static FakeSpatialStore Store() => new(new() { [SpatialLayer.Dzialki] = Parcels() });

        [Fact]
        public void Windows_AreRepeatable_InsideExtent_AndNestedAcrossSizes()
        {
            var small = WindowQueryScenario.Generate(Extent, 100, 100, WindowQueryScenario.Seed);
            var again = WindowQueryScenario.Generate(Extent, 100, 100, WindowQueryScenario.Seed);
            var large = WindowQueryScenario.Generate(Extent, 2000, 100, WindowQueryScenario.Seed);

            Assert.Equal(100, small.Count);
            Assert.Equal(small, again);
            Assert.All(large, w => Assert.True(Extent.Contains(w.Envelope), w.Id));
            Assert.All(small.Zip(large), p => Assert.True(p.First.Envelope.Centre.Distance(p.Second.Envelope.Centre) < 1e-6, p.First.Id));
            Assert.Equal("okno001", small[0].Id);
            Assert.Equal(100, small[0].Envelope.Width, 9);
        }

        [Theory]
        [InlineData(100, false)]
        [InlineData(500, true)]
        public async Task Nts_MatchesBruteForce(double size, bool parallel)
        {
            var store = Store();
            var scenario = new WindowQueryScenario(size);
            await ((IBenchmarkScenario)scenario).PrepareAsync(store, CancellationToken.None);

            var run = await new NtsStrategy(parallel).RunAsync(scenario, store);

            // Pełny przegląd bez indeksu: każda działka z każdym oknem
            var parcels = Parcels();
            var expected = scenario.Windows
                .SelectMany(w => parcels
                    .Where(p => BenchmarkFixtures.Factory.ToGeometry(w.Envelope).Intersects(p.Geometry))
                    .Select(p => new ResultPair(w.Id, p.Id)))
                .ToList();
            Assert.NotEmpty(expected);
            Assert.True(ResultComparison.Compare(expected, run.Pairs).Equal);
            Assert.Equal(new[] { NtsStrategy.ReadPhase, NtsStrategy.IndexPhase, NtsStrategy.ComputePhase }, run.Phases.Select(p => p.Phase));
        }

        [Theory]
        [InlineData(DatabaseEngine.SqlServer)]
        [InlineData(DatabaseEngine.Postgres)]
        public void DiagnosticSql_HasValuesInsteadOfParameters(DatabaseEngine engine)
        {
            var scenario = new WindowQueryScenario(500);
            scenario.UseExtent(Extent);

            var sql = ((IBenchmarkScenario)scenario).DiagnosticSql(engine);

            Assert.DoesNotContain("@", sql);
            Assert.Contains(scenario.Windows[0].Envelope.MinX.ToString("R", System.Globalization.CultureInfo.InvariantCulture), sql);
        }

        [Fact]
        public void Scenarios_HaveIdsAndNoResultTable()
        {
            var scenarios = WindowQueryScenarios.All;

            Assert.Equal(new[] { "03-100", "03-500", "03-2000" }, scenarios.Select(s => s.Id));
            Assert.All(scenarios, s => Assert.False(((IBenchmarkScenario)s).HasResultTable));
            Assert.Equal(500, WindowQueryScenarios.SizeOf("03-500"));
            Assert.Null(WindowQueryScenarios.SizeOf("05a"));
        }

        [Fact]
        public void ChartData_WindowSizes_UsesLatestReportPerSize()
        {
            static StoredReport Report(string id, DateTime at, double w1, double w4p) => new()
            {
                ScenarioId = id,
                StartedAt = at,
                Variants =
                [
                    new StoredVariant { Code = "W1", Engine = DatabaseEngine.SqlServer, Strategy = "w bazie",
                        Total = MeasurementStatistics.FromMilliseconds([w1]), Phases = new() { ["zapytanie"] = MeasurementStatistics.FromMilliseconds([w1]) } },
                    new StoredVariant { Code = "W4P", Engine = DatabaseEngine.Postgres, Strategy = "NTS wielowątkowo",
                        Total = MeasurementStatistics.FromMilliseconds([w4p]) }
                ]
            };
            var t = new DateTime(2026, 11, 13, 10, 0, 0);

            var (sizes, lines) = BenchmarkChartData.WindowSizes(
            [
                Report("03-2000", t, 900, 300),
                Report("03-100", t, 50, 250),
                Report("03-100", t.AddHours(1), 40, 240),
                Report("04", t, 1, 1)
            ]);

            Assert.Equal(new double[] { 100, 2000 }, sizes);
            Assert.Equal(new[] { "W1", "W4P" }, lines.Select(l => l.Variant.Code));
            Assert.Equal(new double[] { 40, 900 }, lines[0].MediansMs);
        }
    }
}
