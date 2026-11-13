using GeoBench.Core.Data;
using GeoBench.Core.Reporting;
using ScottPlot;

namespace GeoBench.Wpf.Charts
{
    /// <summary>
    /// Rysuje wykresy wyników na <see cref="Plot"/> ScottPlot. Ta sama metoda służy kontrolce w zakładce Wyniki
    /// i eksportowi do PNG/SVG (nowy Plot bez kontrolki).
    /// </summary>
    public static class BenchmarkChartRenderer
    {
        public const int ExportWidth = 1600;
        public const int ExportHeight = 900;

        // Silnik = barwa, miejsce przetwarzania = odcień (w bazie najciemniej)
        private static readonly Dictionary<(DatabaseEngine, VariantKind), Color> VariantColors = new()
        {
            [(DatabaseEngine.SqlServer, VariantKind.InDatabase)] = Color.FromHex("#A93226"),
            [(DatabaseEngine.SqlServer, VariantKind.NtsSequential)] = Color.FromHex("#E67E22"),
            [(DatabaseEngine.SqlServer, VariantKind.NtsParallel)] = Color.FromHex("#F5B041"),
            [(DatabaseEngine.Postgres, VariantKind.InDatabase)] = Color.FromHex("#1A5276"),
            [(DatabaseEngine.Postgres, VariantKind.NtsSequential)] = Color.FromHex("#2E86C1"),
            [(DatabaseEngine.Postgres, VariantKind.NtsParallel)] = Color.FromHex("#85C1E9")
        };

        private static readonly Dictionary<string, Color> PhaseColors = new()
        {
            ["zapytanie"] = Color.FromHex("#7F8C8D"),
            ["odczyt"] = Color.FromHex("#2874A6"),
            ["przygotowanie"] = Color.FromHex("#8E44AD"),
            ["indeks"] = Color.FromHex("#D68910"),
            ["obliczenie"] = Color.FromHex("#229954")
        };

        private static readonly Color WhiskerColor = Color.FromHex("#333333");

        /// <summary>
        /// Rysuje wykres. Wykresy jednej serii używają najnowszego z raportów, porównanie – wszystkich.
        /// </summary>
        public static void Draw(Plot plot, ChartKind kind, IReadOnlyList<StoredReport> reports, bool logScale)
        {
            // Polskie znaki w podpisach: czcionka dobierana do tekstu
            plot.Font.Automatic();

            if (reports.Count == 0)
            {
                plot.Title("Zaznacz raport na liście");
                return;
            }

            var latest = reports.OrderByDescending(r => r.StartedAt).First();
            switch (kind)
            {
                case ChartKind.Medians:
                    DrawMedians(plot, latest, logScale);
                    break;
                case ChartKind.Phases:
                    DrawPhases(plot, latest);
                    break;
                case ChartKind.Runs:
                    DrawRuns(plot, latest);
                    break;
                case ChartKind.SeriesComparison:
                    DrawComparison(plot, reports, logScale);
                    break;
            }
        }

        private static string Title(StoredReport report, string what) =>
            $"Scenariusz {report.ScenarioId}: {what} ({BenchmarkChartData.SeriesName(report)}, {report.Options.Repetitions} pomiarów)";

        private static void DrawMedians(Plot plot, StoredReport report, bool logScale)
        {
            var variants = BenchmarkChartData.Variants(report);
            plot.Title(Title(report, "mediana czasu, wąsy min–p95"));
            if (variants.Count == 0)
                return;

            var log = logScale && variants.All(v => v.MinMs > 0);
            double Y(double ms) => log ? Math.Log10(ms) : ms;
            var baseValue = log ? BenchmarkChartData.LogBase(variants.Min(v => v.MinMs)) : 0;

            var bars = variants.Select((v, i) => new Bar
            {
                Position = i,
                ValueBase = baseValue,
                Value = Y(v.MedianMs),
                FillColor = VariantColors[(v.Engine, v.Kind)],
                Label = BenchmarkChartData.FormatMs(v.MedianMs)
            }).ToList();
            plot.Add.Bars(bars);

            for (var i = 0; i < variants.Count; i++)
                AddWhisker(plot, i, Y(variants[i].MinMs), Y(variants[i].P95Ms));

            SetVariantAxis(plot, variants);
            if (log)
                SetLogAxis(plot, variants.Min(v => v.MinMs), variants.Max(v => v.P95Ms));
            plot.YLabel(log ? "czas (skala logarytmiczna)" : "czas [ms]");
            plot.Axes.AutoScale();
        }

        private static void DrawPhases(Plot plot, StoredReport report)
        {
            var variants = BenchmarkChartData.Variants(report);
            plot.Title(Title(report, "mediany faz"));
            if (variants.Count == 0)
                return;

            // Jedna grupa słupków na fazę, żeby legenda miała po jednej pozycji na fazę
            foreach (var phase in BenchmarkChartData.PhaseOrder)
            {
                var bars = new List<Bar>();
                for (var i = 0; i < variants.Count; i++)
                {
                    var phases = variants[i].Phases;
                    var index = phases.ToList().FindIndex(p => p.Phase == phase);
                    if (index < 0)
                        continue;
                    var below = phases.Take(index).Sum(p => p.MedianMs);
                    bars.Add(new Bar
                    {
                        Position = i,
                        ValueBase = below,
                        Value = below + phases[index].MedianMs,
                        FillColor = PhaseColors.GetValueOrDefault(phase, Colors.Gray)
                    });
                }
                if (bars.Count == 0)
                    continue;
                var group = plot.Add.Bars(bars);
                group.LegendText = phase;
            }

            SetVariantAxis(plot, variants);
            plot.YLabel("czas [ms] (suma median faz)");
            plot.ShowLegend(Alignment.UpperRight);
            plot.Axes.AutoScale();
        }

        private static void DrawRuns(Plot plot, StoredReport report)
        {
            var variants = BenchmarkChartData.Variants(report);
            plot.Title(Title(report, "wszystkie pomiary i mediana"));
            if (variants.Count == 0)
                return;

            for (var i = 0; i < variants.Count; i++)
            {
                var v = variants[i];
                var runs = v.RunsMs.ToArray();
                // Lekkie rozsunięcie punktów w poziomie, żeby równe czasy się nie zasłaniały
                var xs = runs.Select((_, k) => i + (runs.Length > 1 ? (k / (double)(runs.Length - 1) - 0.5) * 0.3 : 0)).ToArray();
                var points = plot.Add.Scatter(xs, runs);
                points.LineWidth = 0;
                points.MarkerSize = 7;
                points.Color = VariantColors[(v.Engine, v.Kind)];

                var median = plot.Add.Scatter(new[] { i - 0.25, i + 0.25 }, new[] { v.MedianMs, v.MedianMs });
                median.MarkerSize = 0;
                median.LineWidth = 2;
                median.Color = WhiskerColor;
            }

            SetVariantAxis(plot, variants);
            plot.YLabel("czas [ms]");
            plot.Axes.AutoScale();
        }

        private static void DrawComparison(Plot plot, IReadOnlyList<StoredReport> reports, bool logScale)
        {
            var (codes, series) = BenchmarkChartData.Compare(reports);
            plot.Title($"Porównanie serii: mediana czasu ({series.Count} serii)");
            if (codes.Count == 0)
                return;

            var values = series.SelectMany(s => s.MediansMs.Values).Where(v => v > 0).ToList();
            var log = logScale && values.Count > 0;
            var baseValue = log ? BenchmarkChartData.LogBase(values.Min()) : 0;
            double Y(double ms) => log ? Math.Log10(ms) : ms;

            // Grupa = wariant, w grupie po jednym słupku na serię
            var groupWidth = series.Count + 1;
            var palette = new ScottPlot.Palettes.Category10();
            for (var s = 0; s < series.Count; s++)
            {
                var bars = new List<Bar>();
                for (var c = 0; c < codes.Count; c++)
                {
                    if (!series[s].MediansMs.TryGetValue(codes[c], out var median) || median <= 0)
                        continue;
                    bars.Add(new Bar
                    {
                        Position = c * groupWidth + s,
                        ValueBase = baseValue,
                        Value = Y(median),
                        FillColor = palette.GetColor(s)
                    });
                }
                if (bars.Count == 0)
                    continue;
                var group = plot.Add.Bars(bars);
                group.LegendText = series[s].Name;
            }

            var positions = codes.Select((_, c) => c * groupWidth + (series.Count - 1) / 2.0).ToArray();
            plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(positions, codes.ToArray());
            if (log)
                SetLogAxis(plot, values.Min(), values.Max());
            plot.YLabel(log ? "mediana (skala logarytmiczna)" : "mediana [ms]");
            plot.ShowLegend(Alignment.UpperRight);
            plot.Axes.AutoScale();
        }

        /// <summary>Wąs od min do p95 z poprzeczkami na końcach.</summary>
        private static void AddWhisker(Plot plot, double x, double low, double high)
        {
            const double cap = 0.12;
            foreach (var (xs, ys) in new[]
            {
                (new[] { x, x }, new[] { low, high }),
                (new[] { x - cap, x + cap }, new[] { low, low }),
                (new[] { x - cap, x + cap }, new[] { high, high })
            })
            {
                var line = plot.Add.Scatter(xs, ys);
                line.MarkerSize = 0;
                line.LineWidth = 1.5f;
                line.Color = WhiskerColor;
            }
        }

        private static void SetVariantAxis(Plot plot, IReadOnlyList<ChartVariant> variants)
        {
            var positions = variants.Select((_, i) => (double)i).ToArray();
            plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
                positions, variants.Select(v => v.AxisLabel).ToArray());
        }

        private static void SetLogAxis(Plot plot, double minMs, double maxMs)
        {
            var ticks = BenchmarkChartData.LogTicks(minMs, maxMs);
            plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
                ticks.Select(t => t.Position).ToArray(), ticks.Select(t => t.Label).ToArray());
        }
    }
}
