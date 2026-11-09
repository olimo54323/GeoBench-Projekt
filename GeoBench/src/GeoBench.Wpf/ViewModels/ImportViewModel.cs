using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoBench.Core.Data;
using GeoBench.Core.Import;
using GeoBench.Core.Import.PlanOgolny;
using GeoBench.Wpf.Services;

namespace GeoBench.Wpf.ViewModels
{
    /// <summary>
    /// Zakładka Import: plik planu ogólnego (GML) do wybranych baz. Import przygotowuje dane i nie jest pomiarem.
    /// </summary>
    public sealed partial class ImportViewModel : ObservableObject
    {
        // Pełna lista ostrzeżeń i odrzuceń trafia do logu tylko do tej liczby wpisów na bazę
        private const int MaxLoggedIssues = 50;

        private readonly GeoBenchContextFactory _factory;
        private readonly IOperationHost _host;
        private readonly IDialogService _dialogs;
        private readonly PlanOgolnyImporter _importer = new();

        public ImportViewModel(GeoBenchContextFactory factory, IOperationHost host, IDialogService dialogs)
        {
            _factory = factory;
            _host = host;
            _dialogs = dialogs;
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
        private string? _filePath;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
        private bool _toSqlServer = true;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
        private bool _toPostgres = true;

        /// <summary>Plan nieobowiązujący (np. POG Częstochowy) jest oznaczany w bazie jako dane badawcze.</summary>
        [ObservableProperty]
        private bool _daneBadawcze = true;

        [ObservableProperty]
        private string? _summary;

        [RelayCommand]
        private void Browse()
        {
            var path = _dialogs.OpenFile("Wybierz plik planu ogólnego gminy", "Plan ogólny (*.gml)|*.gml|Wszystkie pliki (*.*)|*.*");
            if (path != null)
                FilePath = path;
        }

        private IEnumerable<DatabaseEngine> SelectedEngines()
        {
            if (ToSqlServer)
                yield return DatabaseEngine.SqlServer;
            if (ToPostgres)
                yield return DatabaseEngine.Postgres;
        }

        private bool CanImport() => !_host.IsBusy && File.Exists(FilePath) && SelectedEngines().Any();

        [RelayCommand(CanExecute = nameof(CanImport))]
        private async Task ImportAsync()
        {
            var path = FilePath!;
            var engines = SelectedEngines().ToList();
            var daneBadawcze = DaneBadawcze;
            var summaries = new List<string>();

            await _host.RunAsync($"Import {Path.GetFileName(path)}", async cancellationToken =>
            {
                foreach (var engine in engines)
                {
                    var name = engine == DatabaseEngine.SqlServer ? "SQL Server" : "PostgreSQL";
                    var progress = new Progress<ImportProgress>(p => _host.SetStatus($"Import do {name}: {p.Stage} ({p.Processed})"));

                    // Odczyt GML jest synchroniczny, więc import działa poza wątkiem interfejsu
                    var report = await Task.Run(async () =>
                    {
                        await using var context = _factory.Create(engine);
                        return await _importer.ImportFileAsync(context, path, daneBadawcze, progress, cancellationToken);
                    }, cancellationToken);

                    LogReport(name, report);
                    summaries.Add($"{name}: {report}");
                }
            });

            if (summaries.Count > 0)
                Summary = string.Join(Environment.NewLine, summaries);
        }

        private void LogReport(string name, ImportReport report)
        {
            _host.AddLog($"{name}: {report}");
            foreach (var (typeName, count) in report.SkippedByType)
                _host.AddLog($"{name}: pominięto {count} × {typeName} (model jeszcze tego nie obsługuje)");
            foreach (var rejected in report.Rejected.Take(MaxLoggedIssues))
                _host.AddLog($"{name}: odrzucono {rejected.TypeName} {rejected.Id}: {rejected.Reason}", isError: true);
            foreach (var warning in report.Warnings.Take(MaxLoggedIssues))
                _host.AddLog($"{name}: {warning.TypeName} {warning.Id}: {warning.Message}");
            var hidden = Math.Max(0, report.Rejected.Count - MaxLoggedIssues) + Math.Max(0, report.Warnings.Count - MaxLoggedIssues);
            if (hidden > 0)
                _host.AddLog($"{name}: … i {hidden} kolejnych uwag");
        }
    }
}
