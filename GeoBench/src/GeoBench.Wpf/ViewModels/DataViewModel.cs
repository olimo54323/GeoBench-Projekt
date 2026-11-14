using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoBench.Core.Data;
using GeoBench.Core.Storage;

namespace GeoBench.Wpf.ViewModels
{
    /// <summary>Wiersz tabeli warstw (tekst gotowy do wyświetlenia).</summary>
    public sealed record DataLayerRow(string Layer, string Database, string Count, string Extent, string Invalid,
        string TableSize, string Index, string IndexSize, string IndexRows, string Error, bool IsProblem);

    /// <summary>Wiersz tabeli wyników: wariant w tabeli wyników jednej bazy.</summary>
    public sealed record DataResultRow(string Table, string Title, string Database, string Variant, string Rows, string Error);

    /// <summary>
    /// Zakładka Dane: warstwy obu baz obok siebie (liczności, zasięg, rozmiar, indeks przestrzenny, opcjonalnie
    /// poprawność geometrii) i warianty zapisane w tabelach wyników. Odczyt na żądanie, bez zmian w bazach.
    /// </summary>
    public sealed partial class DataViewModel : ObservableObject
    {
        private static readonly DatabaseEngine[] Engines = [DatabaseEngine.SqlServer, DatabaseEngine.Postgres];

        private readonly GeoBenchContextFactory _factory;
        private readonly IOperationHost _host;

        public DataViewModel(GeoBenchContextFactory factory, IOperationHost host)
        {
            _factory = factory;
            _host = host;
        }

        public ObservableCollection<DataLayerRow> Layers { get; } = [];

        public ObservableCollection<DataResultRow> Results { get; } = [];

        /// <summary>Sprawdzenie poprawności wszystkich geometrii (wolne, to praca scenariusza 08).</summary>
        [ObservableProperty]
        private bool _checkValidity;

        [ObservableProperty]
        private string _summary = "Kliknij Odśwież, żeby odczytać stan obu baz.";

        [ObservableProperty]
        private bool _hasMismatch;

        private bool CanRefresh() => !_host.IsBusy;

        [RelayCommand(CanExecute = nameof(CanRefresh))]
        private async Task RefreshAsync()
        {
            var checkValidity = CheckValidity;
            var layers = new List<LayerOverview>();
            var results = new List<ResultTableOverview>();
            var engineErrors = new Dictionary<DatabaseEngine, string>();

            var ok = await _host.RunAsync("Dane: stan baz", async cancellationToken =>
            {
                foreach (var engine in Engines)
                {
                    var name = DataOverview.EngineName(engine);
                    if (!_factory.IsConfigured(engine))
                    {
                        engineErrors[engine] = $"brak connection stringa '{GeoBenchContextFactory.ConnectionName(engine)}'";
                        continue;
                    }
                    _host.SetStatus($"Dane: {name}");
                    try
                    {
                        await using var context = _factory.Create(engine);
                        layers.AddRange(await DataOverview.LayersAsync(SpatialStore.Create(context), checkValidity, cancellationToken));
                        results.AddRange(await DataOverview.ResultsAsync(context, engine, cancellationToken));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        engineErrors[engine] = ex.GetBaseException().Message;
                    }
                }
            });
            if (!ok)
                return;

            Layers.Clear();
            foreach (var layer in DataOverview.Layers)
            {
                foreach (var engine in Engines)
                {
                    var overview = layers.FirstOrDefault(l => l.Layer == layer && l.Engine == engine);
                    Layers.Add(overview != null
                        ? Row(overview)
                        : new DataLayerRow(layer.Name, DataOverview.EngineName(engine), "", "", "", "", "", "", "",
                            engineErrors.GetValueOrDefault(engine, "brak odczytu"), true));
                }
            }

            Results.Clear();
            foreach (var table in results.OrderBy(r => r.Table, StringComparer.Ordinal).ThenBy(r => r.Engine))
            {
                var database = DataOverview.EngineName(table.Engine);
                if (table.Error != null || table.Variants.Count == 0)
                {
                    Results.Add(new DataResultRow(table.Table, table.Title, database, "", table.Error == null ? "0" : "", table.Error ?? "brak wyników"));
                    continue;
                }
                foreach (var variant in table.Variants)
                    Results.Add(new DataResultRow(table.Table, table.Title, database, variant.Variant, variant.Rows.ToString("N0"), ""));
            }

            var mismatches = DataOverview.CountMismatches(layers);
            HasMismatch = mismatches.Count > 0 || engineErrors.Count > 0;
            Summary = engineErrors.Count > 0
                ? "Nie odczytano: " + string.Join("; ", engineErrors.Select(e => $"{DataOverview.EngineName(e.Key)} – {e.Value}"))
                : mismatches.Count > 0
                    ? "Liczności różnią się między bazami (benchmark wymaga równych danych): " + string.Join("; ", mismatches)
                    : "Liczności warstw są równe w obu bazach.";
            _host.AddLog($"Dane: {Summary}", isError: HasMismatch);
        }

        private static DataLayerRow Row(LayerOverview o)
        {
            static string Mb(long? bytes) => bytes is { } b ? (b / 1024.0 / 1024.0).ToString("N1", CultureInfo.CurrentCulture) : "";
            var extent = o.Extent is { } e
                ? $"{e.MinX:0} {e.MinY:0} – {e.MaxX:0} {e.MaxY:0}"
                : o.Count == 0 ? "(pusta)" : "";
            var invalid = o.InvalidCount?.ToString("N0") ?? (o.Count != null ? "nie sprawdzano" : "");
            var index = o.Index?.Name ?? (o.Count != null && o.Error == null ? "brak indeksu" : "");
            return new DataLayerRow(
                o.Layer.Name,
                DataOverview.EngineName(o.Engine),
                o.Count?.ToString("N0") ?? "",
                extent,
                invalid,
                Mb(o.TableSizeBytes),
                index,
                Mb(o.Index?.SizeBytes),
                o.Index?.Rows?.ToString("N0") ?? "",
                o.Error ?? "",
                o.Error != null || o.Count == null || o.InvalidCount > 0 || (o.Count > 0 && o.Index == null));
        }

        /// <summary>Odświeżenie dostępności polecenia po zmianie zajętości okna.</summary>
        public void NotifyBusyChanged() => RefreshCommand.NotifyCanExecuteChanged();
    }
}
