using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoBench.Core.Reporting;
using GeoBench.Wpf.Charts;
using GeoBench.Wpf.Services;

namespace GeoBench.Wpf.ViewModels
{
    /// <summary>Raport na liście zakładki Wyniki.</summary>
    public sealed partial class ReportItemViewModel : ObservableObject
    {
        private readonly Action _selectionChanged;

        public ReportItemViewModel(StoredReport report, bool isSelected, Action selectionChanged)
        {
            Report = report;
            _isSelected = isSelected;
            _selectionChanged = selectionChanged;
        }

        public StoredReport Report { get; }

        public string Title => $"{Report.StartedAt:yyyy-MM-dd HH:mm}  scenariusz {Report.ScenarioId}";

        public string Details
        {
            get
            {
                var variants = string.Join(", ", Report.Variants.Select(v => v.Code));
                var edition = Report.SqlServerEdition is { Length: > 0 } e ? $"; SQL Server {e}" : "";
                return $"{variants}; {Report.Options.Repetitions} pomiarów{edition}";
            }
        }

        [ObservableProperty]
        private bool _isSelected;

        partial void OnIsSelectedChanged(bool value) => _selectionChanged();
    }

    /// <summary>Rodzaj wykresu z nazwą do listy.</summary>
    public sealed record ChartKindItem(ChartKind Kind, string Name, string Hint);

    /// <summary>
    /// Zakładka Wyniki: raporty z katalogu results i wykresy ScottPlot. Widok rysuje wykres po zdarzeniu
    /// <see cref="ChartInvalidated"/>; eksport rysuje ten sam wykres na nowym Plot i zapisuje PNG albo SVG.
    /// </summary>
    public sealed partial class ResultsViewModel : ObservableObject
    {
        private readonly IOperationHost _host;
        private readonly IDialogService _dialogs;
        private bool _reloading;

        public ResultsViewModel(IOperationHost host, IDialogService dialogs)
        {
            _host = host;
            _dialogs = dialogs;
            SelectedKind = ChartKinds[0];
            Reload();
        }

        /// <summary>Widok rysuje wykres od nowa.</summary>
        public event EventHandler? ChartInvalidated;

        public string ResultsRoot { get; } = BenchmarkReportWriter.ResultsRoot();

        public ObservableCollection<ReportItemViewModel> Reports { get; } = [];

        public IReadOnlyList<ChartKindItem> ChartKinds { get; } =
        [
            new(ChartKind.Medians, "Mediana i rozrzut (min–p95)", "Najnowszy zaznaczony raport."),
            new(ChartKind.Phases, "Fazy przetwarzania", "Najnowszy zaznaczony raport; skala liniowa."),
            new(ChartKind.Runs, "Wszystkie pomiary", "Najnowszy zaznaczony raport; skala liniowa."),
            new(ChartKind.SeriesComparison, "Porównanie serii", "Wszystkie zaznaczone raporty, od najstarszego."),
            new(ChartKind.WindowSizes, "Czas a rozmiar okna (scenariusz 3)", "Zaznaczone raporty 03-*; przy powtórzonym rozmiarze najnowszy.")
        ];

        [ObservableProperty]
        private ChartKindItem _selectedKind = null!; // ustawiane w konstruktorze

        [ObservableProperty]
        private bool _logScale;

        /// <summary>Czy rodzaj wykresu obsługuje skalę logarytmiczną (fazy skumulowane i punkty – nie).</summary>
        public bool CanUseLogScale => SelectedKind.Kind is ChartKind.Medians or ChartKind.SeriesComparison or ChartKind.WindowSizes;

        public IReadOnlyList<StoredReport> SelectedReports =>
            Reports.Where(r => r.IsSelected).Select(r => r.Report).ToList();

        partial void OnSelectedKindChanged(ChartKindItem value)
        {
            OnPropertyChanged(nameof(CanUseLogScale));
            Invalidate();
        }

        partial void OnLogScaleChanged(bool value) => Invalidate();

        /// <summary>Wczytuje raporty od nowa, zachowując zaznaczenie (bez zaznaczenia – najnowszy).</summary>
        [RelayCommand]
        public void Reload()
        {
            var selected = Reports.Where(r => r.IsSelected).Select(r => r.Report.Directory).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var reports = BenchmarkReportReader.LoadAll(ResultsRoot,
                (path, ex) => _host.AddLog($"Nie wczytano raportu {path}: {ex.Message}", isError: true));

            _reloading = true;
            try
            {
                Reports.Clear();
                for (var i = 0; i < reports.Count; i++)
                {
                    var isSelected = selected.Count == 0 ? i == 0 : selected.Contains(reports[i].Directory);
                    Reports.Add(new ReportItemViewModel(reports[i], isSelected, OnSelectionChanged));
                }
            }
            finally
            {
                _reloading = false;
            }
            OnSelectionChanged();
        }

        private void OnSelectionChanged()
        {
            if (_reloading)
                return;
            ExportCommand.NotifyCanExecuteChanged();
            // Skala proponowana według danych; użytkownik może ją potem przełączyć
            var values = SelectedReports.SelectMany(BenchmarkChartData.Variants).SelectMany(v => new[] { v.MinMs, v.P95Ms });
            var suggested = BenchmarkChartData.SuggestLogScale(values);
            if (suggested != LogScale)
                LogScale = suggested; // OnLogScaleChanged odświeża wykres
            else
                Invalidate();
        }

        private void Invalidate() => ChartInvalidated?.Invoke(this, EventArgs.Empty);

        /// <summary>Rysuje bieżący wykres na podanym Plot (kontrolka albo eksport).</summary>
        public void Draw(ScottPlot.Plot plot) =>
            BenchmarkChartRenderer.Draw(plot, SelectedKind.Kind, SelectedReports, LogScale && CanUseLogScale);

        /// <summary>Katalog najnowszego zaznaczonego raportu, a bez zaznaczenia katalog results.</summary>
        [RelayCommand]
        private void OpenReportFolder() =>
            _dialogs.OpenFolder(SelectedReports.OrderByDescending(r => r.StartedAt).FirstOrDefault()?.Directory ?? ResultsRoot);

        private bool CanExport() => Reports.Any(r => r.IsSelected);

        [RelayCommand(CanExecute = nameof(CanExport))]
        private void Export()
        {
            var report = SelectedReports.OrderByDescending(r => r.StartedAt).First();
            var directory = Path.Combine(report.Directory, "wykresy");
            Directory.CreateDirectory(directory);
            var name = $"{report.ScenarioId}_{SelectedKind.Kind.ToString().ToLowerInvariant()}{(LogScale && CanUseLogScale ? "_log" : "")}";

            var path = _dialogs.SaveFile("Eksport wykresu", "Obraz PNG (*.png)|*.png|Grafika wektorowa SVG (*.svg)|*.svg", name, directory);
            if (path == null)
                return;

            var plot = new ScottPlot.Plot();
            Draw(plot);
            if (string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase))
                plot.SaveSvg(path, BenchmarkChartRenderer.ExportWidth, BenchmarkChartRenderer.ExportHeight);
            else
                plot.SavePng(path, BenchmarkChartRenderer.ExportWidth, BenchmarkChartRenderer.ExportHeight);
            _host.AddLog($"Wykres zapisany: {path}");
        }
    }
}
