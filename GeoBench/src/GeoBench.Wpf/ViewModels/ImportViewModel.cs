using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoBench.Core.Data;
using GeoBench.Core.Import;
using GeoBench.Core.Import.Egib;
using GeoBench.Core.Import.PlanOgolny;
using GeoBench.Core.Import.Wfs;
using GeoBench.Wpf.Services;

namespace GeoBench.Wpf.ViewModels
{
    /// <summary>Warstwa WFS do zaznaczenia w zakładce Import ze stanem cache.</summary>
    public sealed partial class WfsLayerItemViewModel : ObservableObject
    {
        private readonly Action _selectionChanged;

        public WfsLayerItemViewModel(WfsLayerOptions layer, Action selectionChanged)
        {
            Layer = layer;
            _selectionChanged = selectionChanged;
        }

        public WfsLayerOptions Layer { get; }

        public string Title => Layer.Title ?? Layer.Name;

        [ObservableProperty]
        private bool _isSelected = true;

        [ObservableProperty]
        private string? _cacheState;

        partial void OnIsSelectedChanged(bool value) => _selectionChanged();
    }

    /// <summary>
    /// Zakładka Import: plik planu ogólnego (GML) oraz warstwy EGiB z usługi WFS (przez cache stron)
    /// do wybranych baz. Import przygotowuje dane i nie jest pomiarem.
    /// </summary>
    public sealed partial class ImportViewModel : ObservableObject
    {
        // Pełna lista ostrzeżeń i odrzuceń trafia do logu tylko do tej liczby wpisów na bazę
        private const int MaxLoggedIssues = 50;

        private readonly GeoBenchContextFactory _factory;
        private readonly IOperationHost _host;
        private readonly IDialogService _dialogs;
        private readonly PlanOgolnyImporter _importer = new();
        private readonly EgibImporter _egibImporter = new();
        private readonly HttpClient _http = WfsClient.CreateHttpClient();

        public ImportViewModel(GeoBenchContextFactory factory, IOperationHost host, IDialogService dialogs)
        {
            _factory = factory;
            _host = host;
            _dialogs = dialogs;

            try
            {
                Sources = WfsSourceCatalog.Load();
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                Sources = [];
                host.AddLog($"Nie wczytano {WfsSourceCatalog.FileName}: {ex.Message}", isError: true);
            }
            if (Sources.Count == 0)
                host.AddLog($"Brak źródeł WFS: plik {WfsSourceCatalog.FileName} nie został znaleziony albo jest pusty.", isError: true);
            SelectedSource = Sources.FirstOrDefault();
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
        private string? _filePath;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ImportCommand), nameof(WfsImportCommand))]
        private bool _toSqlServer = true;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ImportCommand), nameof(WfsImportCommand))]
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
                    var progress = CreateProgress($"Import do {name}");

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

        // ---------- Usługa WFS (działki, budynki) ----------

        /// <summary>Źródła z sources.json.</summary>
        public IReadOnlyList<WfsSourceOptions> Sources { get; }

        public string CacheDirectory { get; } = WfsSourceCatalog.DefaultCacheDirectory();

        public ObservableCollection<WfsLayerItemViewModel> Layers { get; } = [];

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(WfsImportCommand))]
        private WfsSourceOptions? _selectedSource;

        /// <summary>Pobiera wszystkie strony od nowa zamiast korzystać z cache.</summary>
        [ObservableProperty]
        private bool _refreshCache;

        partial void OnSelectedSourceChanged(WfsSourceOptions? value)
        {
            Layers.Clear();
            if (value == null)
                return;
            foreach (var layer in value.Layers)
                Layers.Add(new WfsLayerItemViewModel(layer, () => WfsImportCommand.NotifyCanExecuteChanged()));
            UpdateCacheStates();
        }

        private void UpdateCacheStates()
        {
            if (SelectedSource == null)
                return;
            var cache = new WfsCache(CacheDirectory);
            foreach (var item in Layers)
            {
                var manifest = cache.ReadManifest(SelectedSource, item.Layer);
                item.CacheState = manifest == null
                    ? "brak w cache"
                    : cache.IsComplete(SelectedSource, item.Layer)
                        ? $"w cache: {manifest.NumberMatched} obiektów, {manifest.PageCount} stron, pobrano {manifest.CreatedUtc.ToLocalTime():g}"
                        : $"w cache niekompletna: brakuje {cache.MissingPages(SelectedSource, item.Layer, manifest).Count} z {manifest.PageCount} stron";
            }
        }

        private bool CanImportWfs() =>
            !_host.IsBusy && SelectedSource != null && Layers.Any(l => l.IsSelected) && SelectedEngines().Any();

        [RelayCommand(CanExecute = nameof(CanImportWfs))]
        private async Task WfsImportAsync()
        {
            var source = SelectedSource!;
            var layers = Layers.Where(l => l.IsSelected).Select(l => l.Layer).ToList();
            var engines = SelectedEngines().ToList();
            var refresh = RefreshCache;
            var cache = new WfsCache(CacheDirectory);
            var downloader = new WfsDownloader(new WfsClient(_http), cache);
            var summaries = new List<string>();

            await _host.RunAsync($"Import z {source.Title ?? source.Name}", async cancellationToken =>
            {
                foreach (var layer in layers)
                {
                    // Faza A: strony do cache (brakujące albo wszystkie przy odświeżeniu)
                    var downloadProgress = CreateProgress(source.Name);
                    var manifest = await Task.Run(() => downloader.EnsureDownloadedAsync(source, layer, refresh, downloadProgress, cancellationToken), cancellationToken);
                    _host.AddLog($"{layer.Title ?? layer.Name}: {manifest.NumberMatched} obiektów w {manifest.PageCount} stronach w cache ({cache.LayerDirectory(source, layer)})");

                    // Faza B: te same pliki do każdej bazy
                    foreach (var engine in engines)
                    {
                        var name = $"{EngineName(engine)} / {layer.Title ?? layer.Name}";
                        var progress = CreateProgress($"Import do {EngineName(engine)}");
                        var report = await Task.Run(async () =>
                        {
                            await using var context = _factory.Create(engine);
                            return await _egibImporter.ImportAsync(context, cache.ReadFeatures(source, layer), layer, progress, cancellationToken);
                        }, cancellationToken);

                        LogReport(name, report);
                        summaries.Add($"{name}: {report}");
                    }
                }
            });

            UpdateCacheStates();
            if (summaries.Count > 0)
                Summary = string.Join(Environment.NewLine, summaries);
        }

        private Progress<ImportProgress> CreateProgress(string prefix) =>
            new(p =>
            {
                _host.SetStatus(p.Total is { } total ? $"{prefix}: {p.Stage} {p.Processed}/{total}" : $"{prefix}: {p.Stage} ({p.Processed})");
                _host.SetProgress(p.Processed, p.Total);
            });

        private static string EngineName(DatabaseEngine engine) => engine == DatabaseEngine.SqlServer ? "SQL Server" : "PostgreSQL";

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
