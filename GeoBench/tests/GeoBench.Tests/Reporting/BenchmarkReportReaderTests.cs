using GeoBench.Core.Benchmark;
using GeoBench.Core.Benchmark.Measurement;
using GeoBench.Core.Data;
using GeoBench.Core.Reporting;
using GeoBench.Core.Validation;

namespace GeoBench.Tests.Reporting
{
    /// <summary>Raport zapisany przez BenchmarkReportWriter wraca z report.json z tymi samymi wartościami.</summary>
    public class BenchmarkReportReaderTests
    {
        private static BenchmarkReport CreateReport()
        {
            var report = new BenchmarkReport
            {
                ScenarioId = "04",
                ScenarioName = "Punkt w wielokącie",
                Options = new BenchmarkOptions { Repetitions = 5, Warmups = 1 },
                StartedAt = new DateTime(2026, 11, 12, 21, 45, 25)
            };
            report.Counts.Add(new LayerCount(DatabaseEngine.SqlServer, "budynki", 62222));

            var w1 = new VariantReport(VariantFactory.Get("W1"))
            {
                Server = [new("edycja", "Express Edition (64-bit)")],
                Total = MeasurementStatistics.FromMilliseconds([21106.082, 20982.266, 21984.775]),
                Fingerprint = new ResultFingerprint(62214, null, "1FFCDB7A")
            };
            w1.RunsMs.AddRange([21106.082, 20982.266, 21984.775]);
            w1.Phases["zapytanie"] = w1.Total!;
            w1.Notes.Add("budynki w strefach: 62214 z 62222");

            var w4p = new VariantReport(VariantFactory.Get("W4P"))
            {
                Total = MeasurementStatistics.FromMilliseconds([235.445, 218.498]),
                Comparison = ResultComparison.Compare([new ResultPair("a", "b")], [new ResultPair("a", "b")]),
                ComparedWith = "W1"
            };
            w4p.Phases["odczyt"] = MeasurementStatistics.FromMilliseconds([136.685]);

            var failed = new VariantReport(VariantFactory.Get("W2")) { Error = "brak połączenia" };
            report.Variants.AddRange([w1, w4p, failed]);
            return report;
        }

        [Fact]
        public async Task WrittenReport_IsReadBack()
        {
            var root = Path.Combine(Path.GetTempPath(), $"geobench_raporty_{Guid.NewGuid():N}");
            try
            {
                var directory = await BenchmarkReportWriter.WriteAsync(CreateReport(), Path.Combine(root, "2026-11-12_21-45-25"));
                Directory.CreateDirectory(Path.Combine(root, "pusty")); // katalog bez raportu jest pomijany

                var report = Assert.Single(BenchmarkReportReader.LoadAll(root));

                Assert.Equal("04", report.ScenarioId);
                Assert.Equal(new DateTime(2026, 11, 12, 21, 45, 25), report.StartedAt);
                Assert.Equal(5, report.Options.Repetitions);
                Assert.Equal(Path.GetFullPath(directory), report.Directory);
                Assert.Equal(62222, Assert.Single(report.Counts).Count);
                Assert.Equal("Express Edition (64-bit)", report.SqlServerEdition);

                Assert.Equal(new[] { "W1", "W4P", "W2" }, report.Variants.Select(v => v.Code));
                var w1 = report.Variants[0];
                Assert.Equal(DatabaseEngine.SqlServer, w1.Engine);
                Assert.Equal(21106.082, w1.Total!.MedianMs, 6);
                Assert.Equal(3, w1.RunsMs.Count);
                Assert.Equal(21106.082, w1.Phases["zapytanie"].MedianMs, 6);
                Assert.Equal(62214, w1.Fingerprint!.Count);
                Assert.True(report.Variants[1].Comparison!.Equal);
                Assert.Equal("W1", report.Variants[1].ComparedWith);
                Assert.False(report.Variants[2].Succeeded);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void LoadAll_ReportsBrokenFile_AndContinues()
        {
            var root = Path.Combine(Path.GetTempPath(), $"geobench_raporty_{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "zly"));
                File.WriteAllText(Path.Combine(root, "zly", BenchmarkReportReader.FileName), "{ to nie jest json");
                var errors = new List<string>();

                var reports = BenchmarkReportReader.LoadAll(root, (path, _) => errors.Add(path));

                Assert.Empty(reports);
                Assert.Single(errors);
                Assert.Empty(BenchmarkReportReader.LoadAll(Path.Combine(root, "nie_istnieje")));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }
    }
}
