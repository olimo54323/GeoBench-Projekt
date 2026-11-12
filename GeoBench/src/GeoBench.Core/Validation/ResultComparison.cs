using GeoBench.Core.Benchmark;

namespace GeoBench.Core.Validation
{
    /// <summary>
    /// Porównanie dwóch wyników jako multizbiorów par (id_a, id_b). Przykłady różnic trafiają do raportu,
    /// żeby rozbieżność dało się sprawdzić w bazie (tabele wyników) albo w QGIS.
    /// </summary>
    public sealed record ResultComparison(
        bool Equal,
        int OnlyInExpected,
        int OnlyInActual,
        IReadOnlyList<ResultPair> SamplesOnlyInExpected,
        IReadOnlyList<ResultPair> SamplesOnlyInActual,
        double? MeasureDifference)
    {
        public const int DefaultSamples = 20;

        /// <summary>Względna tolerancja sumy miar (pola w m² liczone różnymi bibliotekami).</summary>
        public const double DefaultRelativeTolerance = 1e-6;

        public static ResultComparison Compare(IReadOnlyList<ResultPair> expected, IReadOnlyList<ResultPair> actual,
            double relativeTolerance = DefaultRelativeTolerance, int samples = DefaultSamples)
        {
            var counts = new Dictionary<(string, string), int>();
            foreach (var p in expected)
                counts[(p.IdA, p.IdB)] = counts.GetValueOrDefault((p.IdA, p.IdB)) + 1;

            var onlyInActual = new List<ResultPair>();
            foreach (var p in actual)
            {
                var key = (p.IdA, p.IdB);
                var left = counts.GetValueOrDefault(key);
                if (left > 0)
                    counts[key] = left - 1;
                else
                    onlyInActual.Add(p);
            }
            var onlyInExpected = counts.Where(c => c.Value > 0)
                .SelectMany(c => Enumerable.Repeat(new ResultPair(c.Key.Item1, c.Key.Item2), c.Value))
                .ToList();

            double? difference = null;
            var measureEqual = true;
            if (expected.Any(p => p.Measure.HasValue) && actual.Any(p => p.Measure.HasValue))
            {
                var expectedSum = expected.Sum(p => p.Measure ?? 0);
                difference = actual.Sum(p => p.Measure ?? 0) - expectedSum;
                measureEqual = Math.Abs(difference.Value) <= relativeTolerance * Math.Max(1, Math.Abs(expectedSum));
            }

            static IReadOnlyList<ResultPair> Sample(List<ResultPair> pairs, int n) =>
                pairs.OrderBy(p => p.IdA, StringComparer.Ordinal).ThenBy(p => p.IdB, StringComparer.Ordinal).Take(n).ToList();

            return new ResultComparison(
                onlyInExpected.Count == 0 && onlyInActual.Count == 0 && measureEqual,
                onlyInExpected.Count,
                onlyInActual.Count,
                Sample(onlyInExpected, samples),
                Sample(onlyInActual, samples),
                difference);
        }

        public override string ToString() => Equal
            ? "zgodny"
            : $"różnice: {OnlyInExpected} par tylko we wzorcu, {OnlyInActual} par tylko w wyniku" +
              (MeasureDifference is { } d ? $", różnica sumy miar {d:0.###}" : "");
    }
}
