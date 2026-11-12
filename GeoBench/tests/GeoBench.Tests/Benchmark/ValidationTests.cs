using GeoBench.Core.Benchmark;
using GeoBench.Core.Benchmark.Measurement;
using GeoBench.Core.Validation;

namespace GeoBench.Tests.Benchmark
{
    /// <summary>Odcisk i porównanie wyników, wzorzec CSV z QGIS, statystyki pomiarów.</summary>
    public class ValidationTests
    {
        private static readonly ResultPair[] Pairs =
        [
            new("b1", "A", 10.5),
            new("b2", "A", 2.25),
            new("b2", "B", 1)
        ];

        [Fact]
        public void Fingerprint_DoesNotDependOnOrder()
        {
            var a = ResultFingerprint.Compute(Pairs);
            var b = ResultFingerprint.Compute(Pairs.Reverse().ToList());

            Assert.Equal(a, b);
            Assert.Equal(3, a.Count);
            Assert.Equal(13.75, a.MeasureSum);
            Assert.Equal(64, a.Sha256.Length);
        }

        [Fact]
        public void Fingerprint_ChangesWithPairs_ButNotWithMeasure()
        {
            var original = ResultFingerprint.Compute(Pairs);
            var otherZone = ResultFingerprint.Compute([Pairs[0], Pairs[1], new("b2", "C", 1)]);
            var otherMeasure = ResultFingerprint.Compute([Pairs[0], Pairs[1], new("b2", "B", 1.0000001)]);

            Assert.NotEqual(original.Sha256, otherZone.Sha256);
            Assert.Equal(original.Sha256, otherMeasure.Sha256);
            Assert.Null(ResultFingerprint.Compute([new("b1", "A")]).MeasureSum);
        }

        [Fact]
        public void Comparison_ReportsMissingAndExtraPairs()
        {
            ResultPair[] actual = [new("b2", "A"), new("b2", "B"), new("b3", "A")];

            var comparison = ResultComparison.Compare(Pairs.Select(p => p with { Measure = null }).ToList(), actual);

            Assert.False(comparison.Equal);
            Assert.Equal(1, comparison.OnlyInExpected);
            Assert.Equal(1, comparison.OnlyInActual);
            Assert.Equal(new ResultPair("b1", "A"), Assert.Single(comparison.SamplesOnlyInExpected));
            Assert.Equal(new ResultPair("b3", "A"), Assert.Single(comparison.SamplesOnlyInActual));
        }

        [Fact]
        public void Comparison_CountsDuplicates_AndToleratesRounding()
        {
            Assert.False(ResultComparison.Compare(Pairs, [.. Pairs, Pairs[0]]).Equal);

            var rounded = Pairs.Select(p => p with { Measure = p.Measure + 1e-9 }).ToList();
            Assert.True(ResultComparison.Compare(Pairs, rounded).Equal);

            var wrongArea = Pairs.Select(p => p with { Measure = p.Measure + 0.5 }).ToList();
            Assert.False(ResultComparison.Compare(Pairs, wrongArea).Equal);
        }

        [Fact]
        public void Reference_ParsesCommaCsvWithQuotes()
        {
            var csv = "id_budynku,oznaczenie\n\"246401_1.0182.197_BUD\",601SU\n\n\"a,\"\"b\",1POG\n";

            var pairs = ReferenceResult.Parse(new StringReader(csv));

            Assert.Equal(new[] { new ResultPair("246401_1.0182.197_BUD", "601SU"), new ResultPair("a,\"b", "1POG") }, pairs);
        }

        [Fact]
        public void Reference_ParsesSemicolonCsvWithDecimalComma()
        {
            var csv = "id_dzialki;oznaczenie;pole\r\n246401_1.0219.37/6;601SU;123,45\r\n";

            var pair = Assert.Single(ReferenceResult.Parse(new StringReader(csv)));

            Assert.Equal(new ResultPair("246401_1.0219.37/6", "601SU", 123.45), pair);
        }

        [Fact]
        public void Statistics_MedianAndNearestRankPercentile()
        {
            var odd = MeasurementStatistics.FromMilliseconds([50, 10, 30, 20, 40]);
            Assert.Equal(30, odd.MedianMs);
            Assert.Equal(50, odd.P95Ms);
            Assert.Equal(10, odd.MinMs);
            Assert.Equal(30, odd.MeanMs);
            Assert.Equal(Math.Sqrt(250), odd.StdDevMs, 9);

            var even = MeasurementStatistics.FromMilliseconds([4, 1, 3, 2]);
            Assert.Equal(2.5, even.MedianMs);

            var many = MeasurementStatistics.FromMilliseconds(Enumerable.Range(1, 100).Select(i => (double)i));
            Assert.Equal(95, many.P95Ms);
            Assert.Equal(0, MeasurementStatistics.FromMilliseconds([7]).StdDevMs);
        }
    }
}
