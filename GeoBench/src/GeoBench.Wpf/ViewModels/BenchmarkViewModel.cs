using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoBench.Core.Benchmark;
using GeoBench.Core.Benchmark.Scenarios;
using GeoBench.Core.Data;
using GeoBench.Core.Reporting;
using GeoBench.Wpf.Services;

namespace GeoBench.Wpf.ViewModels
{
    /// <summary>Wariant do zaznaczenia w zakładce Benchmark.</summary>
    public sealed partial class BenchmarkVariantItemViewModel : ObservableObject
    {
        private readonly Action _selectionChanged;

        public BenchmarkVariantItemViewModel(BenchmarkVariant variant, Action selectionChanged)
        {
            Variant = variant;
            _selectionChanged = selectionChanged;
        }

        public BenchmarkVariant Variant { get; }

        public string Title => $"{Variant.Label}: {Variant.Description}";

        [ObservableProperty]
        private bool _isSelected = true;

        partial void OnIsSelectedChanged(bool value) => _selectionChanged();
    }

    /// <summary>Wiersz tabeli wyników serii (czasy w ms).</summary>
    public sealed record BenchmarkResultRow(string Variant, string Description, string Median, string P95, string MinMax,
        string Phases, string Pairs, string Hash, string Validation, string Notes, bool IsProblem);

    /// <summary>
    /// Zakładka Benchmark: scenariusz, warianty W1–W4 (z wersjami wielowątkowymi NTS), liczba pomiarów
    /// i opcjonalny wzorzec CSV z QGIS. Wynik serii trafia do tabeli, logu i katalogu results.
    /// </summary>
    public sealed partial class BenchmarkViewModel : ObservableObject
    {
        private readonly IOperationHost _host;
        private readonly IDialogService _dialogs;
        private readonly BenchmarkRunner _runner;
        private readonly Action? _reportSaved;

        /// <param name="reportSaved">Wywoływane po zapisie raportu (np. odświeżenie listy w zakładce Wyniki).</param>
        public BenchmarkViewModel(GeoBenchContextFactory factory, IOperationHost host, IDialogService dialogs, Action? reportSaved = null)
        {
            _host = host;
            _dialogs = dialogs;
            _reportSaved = reportSaved;
            _runner = new BenchmarkRunner(factory);
            foreach (var variant in VariantFactory.All)
                Variants.Add(new BenchmarkVariantItemViewModel(variant, () => RunCommand.NotifyCanExecuteChanged()));
            SelectedScenario = Scenarios[0];
        }

        /// <summary>Scenariusze gotowe do pomiaru (kolejne: 5 i 3).</summary>
        public IReadOnlyList<IBenchmarkScenario> Scenarios { get; } = [new PointInPolygonScenario()];

        public ObservableCollection<BenchmarkVariantItemViewModel> Variants { get; } = [];

        public ObservableCollection<BenchmarkResultRow> Results { get; } = [];

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private IBenchmarkScenario? _selectedScenario;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private int _repetitions = 5;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private int _warmups = 1;

        [ObservableProperty]
        private bool _diagnostics = true;

        [ObservableProperty]
        private bool _saveResults = true;

        [ObservableProperty]
        private bool _allowCountMismatch;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ClearReferenceCommand))]
        private string? _referencePath;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OpenResultsDirectoryCommand))]
        private string? _resultsDirectory;

        [ObservableProperty]
        private string? _summary;

        [RelayCommand]
        private void BrowseReference()
        {
            var path = _dialogs.OpenFile("Wybierz wynik wzorcowy z QGIS", "CSV (*.csv)|*.csv|Wszystkie pliki (*.*)|*.*");
            if (path != null)
                ReferencePath = path;
        }

        private bool CanClearReference() => ReferencePath != null;

        [RelayCommand(CanExecute = nameof(CanClearReference))]
        private void ClearReference() => ReferencePath = null;

        private bool CanRun() =>
            !_host.IsBusy && SelectedScenario != null && Repetitions >= 1 && Warmups >= 0 && Variants.Any(v => v.IsSelected);

        [RelayCommand(CanExecute = nameof(CanRun))]
        private async Task RunAsync()
        {
            var scenario = SelectedScenario!;
            var variants = Variants.Where(v => v.IsSelected).Select(v => v.Variant).ToList();
            var options = new BenchmarkOptions
            {
                Warmups = Warmups,
                Repetitions = Repetitions,
                Diagnostics = Diagnostics,
                SaveResults = SaveResults,
                AllowCountMismatch = AllowCountMismatch,
                ReferencePath = string.IsNullOrWhiteSpace(ReferencePath) ? null : ReferencePath
            };
            BenchmarkReport? report = null;
            string? directory = null;

            await _host.RunAsync($"Benchmark: scenariusz {scenario.Id}", async cancellationToken =>
            {
                // Progress powstaje w wątku interfejsu, więc zgłoszenia z pomiaru wracają do niego
                var progress = new Progress<BenchmarkProgress>(p =>
                {
                    _host.SetStatus($"Scenariusz {scenario.Id}: {p.Message}");
                    _host.SetProgress(p.Step, p.TotalSteps);
                });
                // Pomiar poza wątkiem interfejsu (NTS liczy synchronicznie)
                report = await Task.Run(() => _runner.RunAsync(scenario, variants, options, progress, cancellationToken), cancellationToken);
                directory = await BenchmarkReportWriter.WriteAsync(report, BenchmarkReportWriter.DefaultDirectory(report.StartedAt), cancellationToken);
            });

            if (report == null)
                return;
            ShowReport(report);
            if (directory != null)
            {
                ResultsDirectory = directory;
                _host.AddLog($"Raport zapisany w {directory}");
                _reportSaved?.Invoke();
            }
        }

        private void ShowReport(BenchmarkReport report)
        {
            Results.Clear();
            foreach (var v in report.Variants)
            {
                var validation = v.Error != null
                    ? "błąd"
                    : v.Comparison?.ToString() ?? "wzorzec";
                if (v.Succeeded && !v.ConsistentAcrossRuns)
                    validation += ", niestabilny";
                var row = new BenchmarkResultRow(
                    v.Label,
                    v.Description,
                    Ms(v.Total?.MedianMs),
                    Ms(v.Total?.P95Ms),
                    v.Total is { } t ? $"{t.MinMs:N1} – {t.MaxMs:N1}" : "",
                    string.Join(", ", v.Phases.Select(p => $"{p.Key} {p.Value.MedianMs:N1}")),
                    v.Fingerprint?.Count.ToString("N0") ?? "",
                    v.Fingerprint?.ShortHash ?? "",
                    validation,
                    v.Error ?? string.Join("; ", v.Notes) + (v.Saved ? "; zapisano w tabeli wyników" : ""),
                    v.Error != null || v.Comparison is { Equal: false } || !v.ConsistentAcrossRuns);
                Results.Add(row);

                if (v.Error != null)
                    _host.AddLog($"{v.Label}: {v.Error}", isError: true);
                else
                    _host.AddLog($"{v.Label} ({v.Description}): mediana {row.Median} ms, p95 {row.P95} ms, {row.Pairs} par, {validation}",
                        isError: row.IsProblem);
                foreach (var note in v.Notes)
                    _host.AddLog($"{v.Label}: {note}");
            }

            var counts = string.Join(", ", report.Counts.Select(c => $"{(c.Engine == DatabaseEngine.SqlServer ? "SQL Server" : "PostgreSQL")} {c.Layer} {c.Count:N0}"));
            Summary = $"{report.ScenarioId}. {report.ScenarioName}. {report.Options.Repetitions} pomiarów po {report.Options.Warmups} rozgrzewce. " +
                      $"Dane: {counts}." + (report.ReferencePairs is { } r ? $" Wzorzec CSV: {r:N0} par." : "");
        }

        private static string Ms(double? value) => value?.ToString("N1") ?? "";

        private bool CanOpenResultsDirectory() => ResultsDirectory != null && Directory.Exists(ResultsDirectory);

        [RelayCommand(CanExecute = nameof(CanOpenResultsDirectory))]
        private void OpenResultsDirectory() =>
            Process.Start(new ProcessStartInfo(ResultsDirectory!) { UseShellExecute = true });
    }
}
