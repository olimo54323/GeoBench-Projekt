using System.Globalization;
using GeoBench.Core.Data;
using GeoBench.Core.Processing;

namespace GeoBench.Core.Reporting
{
    /// <summary>Rodzaj wykresu w zakładce Wyniki.</summary>
    public enum ChartKind
    {
        /// <summary>Mediana z wąsami min–p95 dla każdego wariantu (jedna seria).</summary>
        Medians,

        /// <summary>Mediany faz skumulowane (zapytanie albo odczyt / indeks / obliczenie).</summary>
        Phases,

        /// <summary>Wszystkie pomiary jako punkty z medianą (rozrzut, stabilność serii).</summary>
        Runs,

        /// <summary>Mediany wariantów z kilku serii obok siebie.</summary>
        SeriesComparison,

        /// <summary>Scenariusz 3: mediana wariantów w funkcji rozmiaru okna (najnowszy raport na rozmiar).</summary>
        WindowSizes
    }

    /// <summary>Linia wykresu rozmiarów okna: wariant i mediany dla kolejnych rozmiarów (NaN, gdy brak pomiaru).</summary>
    public sealed record WindowSizeLine(ChartVariant Variant, IReadOnlyList<double> MediansMs);

    /// <summary>Miejsce przetwarzania wariantu (wpływa na odcień koloru).</summary>
    public enum VariantKind
    {
        InDatabase,
        NtsSequential,
        NtsParallel
    }

    /// <summary>Faza z medianą czasu w ms.</summary>
    public readonly record struct PhaseMedian(string Phase, double MedianMs);

    /// <summary>Udany wariant serii przygotowany do wykresu (czasy w ms).</summary>
    public sealed record ChartVariant(
        string Code,
        string Description,
        DatabaseEngine Engine,
        VariantKind Kind,
        double MedianMs,
        double MinMs,
        double P95Ms,
        IReadOnlyList<PhaseMedian> Phases,
        IReadOnlyList<double> RunsMs)
    {
        /// <summary>Podpis osi: kod, silnik i miejsce przetwarzania w trzech wierszach.</summary>
        public string AxisLabel => $"{Code}\n{(Engine == DatabaseEngine.SqlServer ? "SQL Server" : "PostgreSQL")}\n" + Kind switch
        {
            VariantKind.InDatabase => "w bazie",
            VariantKind.NtsSequential => "NTS 1 wątek",
            _ => "NTS równolegle"
        };
    }

    /// <summary>Jedna seria (raport) w porównaniu serii: mediany według kodu wariantu.</summary>
    public sealed record ChartSeries(string Name, IReadOnlyDictionary<string, double> MediansMs);

    /// <summary>
    /// Dane wykresów niezależne od biblioteki rysującej: warianty w stałej kolejności, fazy w kolejności wykonania,
    /// skala logarytmiczna i porównanie serii. Rysowanie (ScottPlot) jest w projekcie WPF.
    /// </summary>
    public static class BenchmarkChartData
    {
        /// <summary>Kolejność faz na wykresie skumulowanym (od dołu).</summary>
        public static IReadOnlyList<string> PhaseOrder { get; } =
            [InDatabaseStrategy.QueryPhase, NtsStrategy.ReadPhase, NtsStrategy.PreparePhase, NtsStrategy.IndexPhase, NtsStrategy.ComputePhase];

        /// <summary>Stosunek największej do najmniejszej wartości, od którego proponowana jest skala logarytmiczna.</summary>
        public const double LogScaleRatio = 20;

        /// <summary>Udane warianty raportu w kolejności W1, W2, W3, W3P, W4, W4P (nieznane kody na końcu).</summary>
        public static IReadOnlyList<ChartVariant> Variants(StoredReport report) =>
            report.Variants.Where(v => v.Succeeded)
                .OrderBy(v => CodeOrder(v.Code)).ThenBy(v => v.Code, StringComparer.Ordinal)
                .Select(v => new ChartVariant(
                    v.Code,
                    v.Description,
                    v.Engine,
                    KindOf(v),
                    v.Total!.MedianMs,
                    v.Total.MinMs,
                    v.Total.P95Ms,
                    v.Phases.OrderBy(p => PhaseIndex(p.Key)).Select(p => new PhaseMedian(p.Key, p.Value.MedianMs)).ToList(),
                    v.RunsMs))
                .ToList();

        public static VariantKind KindOf(StoredVariant variant) =>
            variant.Phases.ContainsKey(InDatabaseStrategy.QueryPhase) ? VariantKind.InDatabase
            : variant.Strategy.Contains("wielowątk", StringComparison.OrdinalIgnoreCase) ? VariantKind.NtsParallel
            : VariantKind.NtsSequential;

        /// <summary>Czy wartości różnią się na tyle, że skala liniowa spłaszczy małe słupki.</summary>
        public static bool SuggestLogScale(IEnumerable<double> values)
        {
            var positive = values.Where(v => v > 0 && double.IsFinite(v)).ToList();
            return positive.Count > 1 && positive.Max() / positive.Min() >= LogScaleRatio;
        }

        /// <summary>
        /// Podziałka skali logarytmicznej: dekady (1, 10, 100 … ms) obejmujące wartości, pozycje w log10.
        /// </summary>
        public static IReadOnlyList<(double Position, string Label)> LogTicks(double minMs, double maxMs)
        {
            if (minMs <= 0 || maxMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(minMs), "Skala logarytmiczna wymaga wartości dodatnich.");
            var from = (int)Math.Floor(Math.Log10(minMs));
            var to = (int)Math.Ceiling(Math.Log10(maxMs));
            if (to == from)
                to++;
            return Enumerable.Range(from, to - from + 1)
                .Select(e => ((double)e, FormatMs(Math.Pow(10, e))))
                .ToList();
        }

        /// <summary>Dolna krawędź słupków w skali log: dekada poniżej najmniejszej wartości.</summary>
        public static double LogBase(double minMs) => Math.Floor(Math.Log10(minMs));

        /// <summary>Mediany z kilku raportów (od najstarszego) i kody wariantów obecnych w którejkolwiek serii.</summary>
        public static (IReadOnlyList<string> Codes, IReadOnlyList<ChartSeries> Series) Compare(IEnumerable<StoredReport> reports)
        {
            var ordered = reports.OrderBy(r => r.StartedAt).ToList();
            var series = ordered.Select(r => new ChartSeries(
                    SeriesName(r),
                    Variants(r).ToDictionary(v => v.Code, v => v.MedianMs, StringComparer.OrdinalIgnoreCase)))
                .ToList();
            var codes = series.SelectMany(s => s.MediansMs.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(CodeOrder).ThenBy(c => c, StringComparer.Ordinal).ToList();
            return (codes, series);
        }

        /// <summary>
        /// Raporty scenariusza 3 (03-100, 03-500 …): rozmiary rosnąco i po jednej linii na wariant.
        /// Przy kilku raportach tego samego rozmiaru liczy się najnowszy.
        /// </summary>
        public static (IReadOnlyList<double> Sizes, IReadOnlyList<WindowSizeLine> Lines) WindowSizes(IEnumerable<StoredReport> reports)
        {
            var bySize = reports
                .Select(r => (Size: Benchmark.Scenarios.WindowQueryScenarios.SizeOf(r.ScenarioId), Report: r))
                .Where(x => x.Size != null)
                .GroupBy(x => x.Size!.Value)
                .OrderBy(g => g.Key)
                .Select(g => (Size: g.Key, Variants: Variants(g.OrderByDescending(x => x.Report.StartedAt).First().Report)))
                .ToList();

            var sizes = bySize.Select(x => x.Size).ToList();
            var lines = bySize.SelectMany(x => x.Variants)
                .GroupBy(v => v.Code, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => CodeOrder(g.Key))
                .Select(g => new WindowSizeLine(g.First(), bySize
                    .Select(x => x.Variants.FirstOrDefault(v => string.Equals(v.Code, g.Key, StringComparison.OrdinalIgnoreCase))?.MedianMs ?? double.NaN)
                    .ToList()))
                .ToList();
            return (sizes, lines);
        }

        /// <summary>Nazwa serii w legendzie: scenariusz, data i edycja SQL Server (gdy ją mierzono).</summary>
        public static string SeriesName(StoredReport report)
        {
            var edition = report.SqlServerEdition;
            var shortEdition = string.IsNullOrEmpty(edition) ? "" : $", SQL Server {edition.Split(' ')[0]}";
            return $"{report.ScenarioId} {report.StartedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}{shortEdition}";
        }

        /// <summary>Czas do podpisu: do 10 s w ms, powyżej w sekundach.</summary>
        public static string FormatMs(double ms) => ms >= 10_000
            ? (ms / 1000).ToString("0.#", CultureInfo.CurrentCulture) + " s"
            : ms.ToString(ms >= 100 ? "N0" : ms >= 1 ? "0.#" : "0.###", CultureInfo.CurrentCulture) + " ms";

        private static int CodeOrder(string code)
        {
            var index = Benchmark.VariantFactory.All.ToList().FindIndex(v => string.Equals(v.Code, code, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? int.MaxValue : index;
        }

        private static int PhaseIndex(string phase)
        {
            var index = PhaseOrder.ToList().IndexOf(phase);
            return index < 0 ? int.MaxValue : index;
        }
    }
}
