using System.Globalization;
using GeoBench.Core.Benchmark.Measurement;
using GeoBench.Core.Data;
using GeoBench.Core.Reporting;

namespace GeoBench.Tests.Reporting
{
    /// <summary>Dane wykresów: kolejność wariantów i faz, skala logarytmiczna, porównanie serii.</summary>
    public class BenchmarkChartDataTests
    {
        private static MeasurementStatistics Stats(params double[] ms) => MeasurementStatistics.FromMilliseconds(ms);

        private static StoredVariant Variant(string code, DatabaseEngine engine, string strategy, double median,
            Dictionary<string, MeasurementStatistics> phases, string? error = null) => new()
        {
            Code = code,
            Engine = engine,
            Strategy = strategy,
            Total = error == null ? Stats(median * 0.9, median, median * 1.2) : null,
            RunsMs = error == null ? [median * 0.9, median, median * 1.2] : [],
            Phases = phases,
            Error = error
        };

        private static StoredReport Report(DateTime startedAt, string edition, params StoredVariant[] variants) => new()
        {
            ScenarioId = "04",
            StartedAt = startedAt,
            Variants =
            [
                .. variants.Select(v => v.Engine == DatabaseEngine.SqlServer
                    ? new StoredVariant
                    {
                        Code = v.Code, Engine = v.Engine, Strategy = v.Strategy, Total = v.Total, RunsMs = v.RunsMs,
                        Phases = v.Phases, Error = v.Error, Server = [new("edycja", edition)]
                    }
                    : v)
            ]
        };

        private static readonly DateTime First = new(2026, 11, 12, 21, 45, 0);

        private static StoredReport SampleReport() => Report(First, "Express Edition (64-bit)",
            Variant("W4P", DatabaseEngine.Postgres, "NTS wielowątkowo", 235,
                new() { ["obliczenie"] = Stats(60), ["odczyt"] = Stats(137), ["indeks"] = Stats(31) }),
            Variant("W2", DatabaseEngine.Postgres, "w bazie", 0, new(), error: "błąd"),
            Variant("W1", DatabaseEngine.SqlServer, "w bazie", 21106, new() { ["zapytanie"] = Stats(21106) }),
            Variant("W3", DatabaseEngine.SqlServer, "NTS jednowątkowo", 675,
                new() { ["odczyt"] = Stats(401), ["indeks"] = Stats(29), ["obliczenie"] = Stats(245) }));

        [Fact]
        public void Variants_AreOrderedByCode_WithoutFailed_PhasesInExecutionOrder()
        {
            var variants = BenchmarkChartData.Variants(SampleReport());

            Assert.Equal(new[] { "W1", "W3", "W4P" }, variants.Select(v => v.Code));
            Assert.Equal(new[] { VariantKind.InDatabase, VariantKind.NtsSequential, VariantKind.NtsParallel }, variants.Select(v => v.Kind));
            Assert.Equal(new[] { "odczyt", "indeks", "obliczenie" }, variants[2].Phases.Select(p => p.Phase));
            Assert.Equal(235, variants[2].MedianMs, 6);
            Assert.Equal("W4P\nPostgreSQL\nNTS równolegle", variants[2].AxisLabel);
        }

        [Fact]
        public void LogScale_IsSuggestedForLargeSpread()
        {
            Assert.True(BenchmarkChartData.SuggestLogScale([235.4, 21106.1]));
            Assert.False(BenchmarkChartData.SuggestLogScale([235.4, 674.9]));
            Assert.False(BenchmarkChartData.SuggestLogScale([100]));
            Assert.False(BenchmarkChartData.SuggestLogScale([0, -1, double.NaN]));
        }

        [Fact]
        public void LogTicks_CoverDecades()
        {
            var ticks = BenchmarkChartData.LogTicks(218.5, 21984.8);

            Assert.Equal(new double[] { 2, 3, 4, 5 }, ticks.Select(t => t.Position));
            Assert.Equal(2, BenchmarkChartData.LogBase(218.5));
            Assert.Equal(2, BenchmarkChartData.LogTicks(150, 180).Count);
        }

        [Fact]
        public void Compare_UsesUnionOfCodes_SeriesFromOldest()
        {
            var developer = Report(First.AddDays(1), "Developer Edition (64-bit)",
                Variant("W1", DatabaseEngine.SqlServer, "w bazie", 9000, new() { ["zapytanie"] = Stats(9000) }),
                Variant("W2", DatabaseEngine.Postgres, "w bazie", 5900, new() { ["zapytanie"] = Stats(5900) }));

            var (codes, series) = BenchmarkChartData.Compare([developer, SampleReport()]);

            Assert.Equal(new[] { "W1", "W2", "W3", "W4P" }, codes);
            Assert.Equal(2, series.Count);
            Assert.EndsWith("SQL Server Express", series[0].Name);
            Assert.EndsWith("SQL Server Developer", series[1].Name);
            Assert.False(series[0].MediansMs.ContainsKey("W2"));
            Assert.Equal(9000, series[1].MediansMs["W1"], 6);
        }

        [Fact]
        public void FormatMs_SwitchesToSecondsAbove10s()
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                Assert.Equal("21.1 s", BenchmarkChartData.FormatMs(21106.082));
                Assert.Equal("235 ms", BenchmarkChartData.FormatMs(235.445));
                Assert.Equal("5.5 ms", BenchmarkChartData.FormatMs(5.46));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
    }
}
