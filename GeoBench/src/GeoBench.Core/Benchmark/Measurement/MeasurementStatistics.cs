namespace GeoBench.Core.Benchmark.Measurement
{
    /// <summary>
    /// Statystyki serii pomiarów w milisekundach. Główną miarą jest mediana (odporna na pojedyncze zakłócenia),
    /// p95 to percentyl metodą najbliższej pozycji (dla 5 pomiarów równy maksimum).
    /// </summary>
    public sealed record MeasurementStatistics(int Count, double MinMs, double MaxMs, double MeanMs, double MedianMs,
        double P95Ms, double StdDevMs)
    {
        public static MeasurementStatistics From(IEnumerable<TimeSpan> samples) =>
            FromMilliseconds(samples.Select(s => s.TotalMilliseconds));

        public static MeasurementStatistics FromMilliseconds(IEnumerable<double> samples)
        {
            var sorted = samples.OrderBy(s => s).ToArray();
            if (sorted.Length == 0)
                throw new ArgumentException("Brak pomiarów.", nameof(samples));

            var n = sorted.Length;
            var median = n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
            var mean = sorted.Average();
            // Odchylenie standardowe z próby (n − 1); dla jednego pomiaru 0
            var stdDev = n > 1 ? Math.Sqrt(sorted.Sum(s => (s - mean) * (s - mean)) / (n - 1)) : 0;
            return new MeasurementStatistics(n, sorted[0], sorted[^1], mean, median, Percentile(sorted, 95), stdDev);
        }

        /// <summary>Percentyl metodą najbliższej pozycji: najmniejsza wartość, od której nie więcej niż p% jest większych.</summary>
        public static double Percentile(IReadOnlyList<double> sorted, double percent)
        {
            var rank = (int)Math.Ceiling(percent / 100 * sorted.Count);
            return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
        }
    }
}
