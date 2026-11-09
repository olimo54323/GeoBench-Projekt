using System.Collections.ObjectModel;
using System.IO;
using System.Linq.Expressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoBench.Core.Data;
using GeoBench.Core.Data.Filters;
using GeoBench.Core.Model;
using GeoBench.Core.Spatial;
using GeoBench.Core.Styles;
using GeoBench.Wpf.Services;
using GeoBench.Wpf.Styles;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using Mapsui.Tiling;
using NetTopologySuite.Geometries;

namespace GeoBench.Wpf.ViewModels
{
    /// <summary>Pozycja legendy stref; Active = false, gdy rodzaj nie spełnia aktywnego filtra stref (wyszarzony).</summary>
    public sealed record LegendItem(string Symbol, string Name, SolidColorBrush Fill, SolidColorBrush Outline, bool Active)
    {
        public double TextOpacity => Active ? 1 : 0.45;
    }

    /// <summary>Baza do wyboru na mapie.</summary>
    public sealed record EngineOption(DatabaseEngine Engine, string Name);

    /// <summary>
    /// Zakładka Mapa: podkład OSM i warstwy z bazy dla bieżącego okna (strefy zawsze, działki i budynki po przybliżeniu).
    /// Każda warstwa może mieć zapisane filtry WHERE; aktywny filtr działa w zapytaniu do bazy.
    /// Zapytania mają własne anulowanie i nie blokują innych operacji; podgląd nie jest pomiarem.
    /// </summary>
    public sealed partial class MapViewModel : ObservableObject
    {
        private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(300);

        /// <summary>Promień wskazania prawym przyciskiem w pikselach ekranu.</summary>
        private const double IdentifyTolerancePixels = 4;

        private readonly GeoBenchContextFactory _factory;
        private readonly IOperationHost _host;
        private readonly IDialogService _dialogs;
        private readonly FilterStore _filters = new(FilterStore.DefaultRoot);
        private readonly Dispatcher _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        private readonly ILayer _osm;
        private readonly MemoryLayer _strefy = new("Strefy planistyczne");
        private readonly MemoryLayer _dzialki = new("Działki");
        private readonly MemoryLayer _budynki = new("Budynki");
        private readonly MemoryLayer _podswietlenie = new("Podświetlenie");
        private readonly LayerNodeViewModel _strefyNode;
        private readonly LayerNodeViewModel _dzialkiNode;
        private readonly LayerNodeViewModel _budynkiNode;
        private CancellationTokenSource? _refresh;
        private CancellationTokenSource? _identify;
        private FeatureInfoViewModel? _info;
        private string? _lastError;
        private bool _zoomedToStart;

        /// <summary>Czy ostatnie odświeżenie pobrało działki i budynki (okno widoku poniżej progu przybliżenia).</summary>
        private bool _detailed;

        /// <summary>Symbole stref spełniających aktywny filtr stref; null = brak filtra (legenda w pełnych kolorach).</summary>
        private HashSet<string>? _zoneSymbols;

        public MapViewModel(GeoBenchContextFactory factory, IOperationHost host, IDialogService dialogs)
        {
            _factory = factory;
            _host = host;
            _dialogs = dialogs;
            Settings = MapStyleSettings.Load();
            AppliedSettings = Settings;

            _osm = OpenStreetMap.CreateTileLayer("GeoBench/1.0 (badanie przetwarzania przestrzennego)");
            Map = new Map();
            Map.Layers.Add(_osm);
            Map.Layers.Add(_strefy);
            Map.Layers.Add(_dzialki);
            Map.Layers.Add(_budynki);
            Map.Layers.Add(_podswietlenie);
            Map.Navigator.FetchRequested += (_, _) => ScheduleRefresh();

            Engines =
            [
                new EngineOption(DatabaseEngine.Postgres, "PostgreSQL + PostGIS"),
                new EngineOption(DatabaseEngine.SqlServer, "SQL Server")
            ];
            _selectedEngine = Engines[0];

            // Nazwy tabel z modelu (te same, pod którymi leżą pliki filtrów)
            _strefyNode = new LayerNodeViewModel(this, MapLayerKind.Strefy, "strefa_planistyczna", "Strefy planu ogólnego", null);
            _dzialkiNode = new LayerNodeViewModel(this, MapLayerKind.Dzialki, "dzialka", "Działki", MapStyleSettings.DefaultDzialki);
            _budynkiNode = new LayerNodeViewModel(this, MapLayerKind.Budynki, "budynek", "Budynki", MapStyleSettings.DefaultBudynki);
            Layers = [_strefyNode, _dzialkiNode, _budynkiNode];

            LoadFilters();
            ApplySettings(Settings);
        }

        /// <summary>Mapa Mapsui przypinana do MapControl w widoku.</summary>
        public Map Map { get; }

        /// <summary>Zapisane ustawienia mapy.</summary>
        public MapStyleSettings Settings { get; private set; }

        /// <summary>Ustawienia, którymi mapa jest teraz narysowana (w oknie stylu: edytowana kopia).</summary>
        public MapStyleSettings AppliedSettings { get; private set; }

        public IReadOnlyList<EngineOption> Engines { get; }

        /// <summary>Warstwy danych w panelu (kolejność panelu), każda z listą filtrów.</summary>
        public IReadOnlyList<LayerNodeViewModel> Layers { get; }

        public ObservableCollection<LegendItem> Legend { get; } = [];

        [ObservableProperty]
        private EngineOption _selectedEngine;

        [ObservableProperty]
        private bool _showOsm = true;

        [ObservableProperty]
        private string _status = "Mapa nie została jeszcze wczytana.";

        partial void OnSelectedEngineChanged(EngineOption value)
        {
            _zoomedToStart = false;
            ScheduleRefresh();
        }

        partial void OnShowOsmChanged(bool value)
        {
            _osm.Enabled = value;
            Map.RefreshGraphics();
        }

        /// <summary>Wywoływane przez widok po wczytaniu kontrolki mapy.</summary>
        public void OnViewLoaded() => ScheduleRefresh();

        [RelayCommand]
        private void Refresh() => ScheduleRefresh();

        /// <summary>Powrót do zasięgu startowego (akt planowania z wybranej bazy albo domyślny zasięg Częstochowy).</summary>
        [RelayCommand]
        private void CenterMap()
        {
            _zoomedToStart = false;
            ScheduleRefresh();
        }

        // Warstwy i filtry (wywołania z węzłów panelu)

        /// <summary>Zmiana widoczności warstwy albo aktywnego filtra.</summary>
        internal void OnLayerChanged(LayerNodeViewModel node)
        {
            Layer(node).Enabled = node.IsVisible;
            if (node.Kind == MapLayerKind.Strefy)
                _zoneSymbols = null;
            ApplyLayerStyles();
            ScheduleRefresh();
        }

        internal void OnLayerStyleChanged(LayerNodeViewModel node) => ApplyLayerStyles();

        /// <summary>Styl warstwy bazowej z bieżących ustawień; null dla stref (kolory według rodzaju).</summary>
        internal LayerStyle? StyleOf(LayerNodeViewModel node) => node.Kind switch
        {
            MapLayerKind.Dzialki => AppliedSettings.Dzialki,
            MapLayerKind.Budynki => AppliedSettings.Budynki,
            _ => null
        };

        internal void SaveSettings()
        {
            try
            {
                Settings.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _host.AddLog($"Nie zapisano stylu mapy ({MapStyleSettings.FilePath}): {ex.Message}", isError: true);
            }
        }

        internal void SaveFilter(FilterNodeViewModel node)
        {
            try
            {
                _filters.Save(node.Filter, node.Filter.Name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                _host.AddLog($"Nie zapisano filtra „{node.Name}”: {ex.Message}", isError: true);
            }
        }

        internal void RenameFilter(FilterNodeViewModel node, string newName)
        {
            var oldName = node.Filter.Name;
            newName = newName.Trim();
            if (newName.Length == 0 || newName == oldName)
                return;
            try
            {
                node.Filter.Name = newName;
                _filters.Save(node.Filter, oldName);
                node.NameChanged();
                RefreshStatusFilters();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                node.Filter.Name = oldName;
                _dialogs.ShowError(ex.Message, "Zmiana nazwy filtra");
            }
        }

        [RelayCommand]
        private void OpenFilters() => OpenFilterBuilder(null);

        /// <summary>Builder filtrów (modalny); po zamknięciu listy filtrów są wczytywane od nowa z dysku.</summary>
        internal void OpenFilterBuilder(FilterNodeViewModel? select)
        {
            var tables = Layers.Select(l => new FilterTableOption(l.Table, l.Title)).ToList();
            var builder = new FilterBuilderViewModel(_filters, _factory, SelectedEngine, tables, _dialogs,
                select?.Layer.Table ?? tables[0].Table, select?.Name);
            _dialogs.ShowFilterBuilder(builder);
            LoadFilters();
            ApplyLayerStyles();
            ScheduleRefresh();
        }

        /// <summary>Filtry z dysku pod każdą warstwą; zaznaczenie zostaje przy filtrze o tej samej nazwie.</summary>
        private void LoadFilters()
        {
            var errors = new List<string>();
            foreach (var layer in Layers)
            {
                var active = layer.ActiveFilter?.Name;
                layer.Filters.Clear();
                foreach (var filter in _filters.Load(layer.Table, errors))
                {
                    var node = new FilterNodeViewModel(this, layer, filter);
                    if (string.Equals(filter.Name, active, StringComparison.OrdinalIgnoreCase))
                        node.SetActiveSilently(true);
                    layer.Filters.Add(node);
                }
            }
            _zoneSymbols = null;
            foreach (var error in errors)
                _host.AddLog($"Pominięto uszkodzony plik filtra: {error}", isError: true);
        }

        private MemoryLayer Layer(LayerNodeViewModel node) => node.Kind switch
        {
            MapLayerKind.Strefy => _strefy,
            MapLayerKind.Dzialki => _dzialki,
            _ => _budynki
        };

        // Style

        [RelayCommand]
        private void EditStyle()
        {
            // Okno edytuje kopię; Settings zostają nietknięte, więc Anuluj to powrót do nich
            var editor = new MapStyleViewModel(Settings.Clone(), ApplySettings);
            if (_dialogs.ShowMapStyle(editor))
            {
                Settings = editor.Settings;
                SaveSettings();
                ApplySettings(Settings);
                ScheduleRefresh();
            }
            else
            {
                ApplySettings(Settings);
            }
        }

        /// <summary>Style warstw i legenda; wywoływane też na żywo z okna stylu.</summary>
        public void ApplySettings(MapStyleSettings settings)
        {
            AppliedSettings = settings;
            _strefy.Opacity = Math.Clamp(settings.PlanOpacity, 0, 1);
            _podswietlenie.Style = MapsuiStyles.ToVectorStyle(settings.Podswietlenie);
            foreach (var layer in Layers)
                layer.RefreshStyle();
            ApplyLayerStyles();
        }

        /// <summary>Styl każdej warstwy: własny styl aktywnego filtra albo styl warstwy bazowej.</summary>
        private void ApplyLayerStyles()
        {
            var settings = AppliedSettings;
            _strefy.Style = StyleFor(_strefyNode) ?? MapsuiStyles.ZoneStyle(settings);
            _dzialki.Style = StyleFor(_dzialkiNode) ?? MapsuiStyles.ToVectorStyle(settings.Dzialki);
            _budynki.Style = StyleFor(_budynkiNode) ?? MapsuiStyles.ToVectorStyle(settings.Budynki);
            UpdateLegend();
            Map.RefreshGraphics();
        }

        private static IStyle? StyleFor(LayerNodeViewModel node) =>
            node.ActiveFilter is { UseLayerStyle: false } filter ? MapsuiStyles.ToVectorStyle(filter.Filter.Style) : null;

        /// <summary>
        /// Legenda stref: bez filtra wszystkie rodzaje w swoich kolorach; z filtrem tylko rodzaje spełniające filtr
        /// (w kolorze filtra albo rodzaju), pozostałe wyszarzone.
        /// </summary>
        private void UpdateLegend()
        {
            var settings = AppliedSettings;
            var filter = _strefyNode.ActiveFilter;
            var grey = Frozen("#E6E6E6");
            var greyOutline = Frozen("#BDBDBD");
            Legend.Clear();
            foreach (var kind in MapStyleSettings.ZoneKinds)
            {
                var style = filter is { UseLayerStyle: false } ? filter.Filter.Style : settings.Strefa(kind.Symbol);
                var active = filter == null || _zoneSymbols == null || _zoneSymbols.Contains(kind.Symbol);
                Legend.Add(active
                    ? new LegendItem(kind.Symbol, kind.Name, Frozen(style.FillColor), Frozen(style.OutlineColor), true)
                    : new LegendItem(kind.Symbol, kind.Name, grey, greyOutline, false));
            }
        }

        private static SolidColorBrush Frozen(string hex)
        {
            var brush = new SolidColorBrush(MapsuiStyles.ParseColor(hex));
            brush.Freeze();
            return brush;
        }

        // Pobieranie danych okna widoku

        /// <summary>Warunek aktywnego filtra widocznej warstwy; null = bez filtra.</summary>
        private static string? ActiveWhere(LayerNodeViewModel node) => node.ActiveFilter?.Filter.Where;

        private static Expression<Func<TEntity, bool>>? CompileFilter<TEntity>(GeoBenchContext context, string? where) =>
            where == null ? null : WhereFilter.Compile<TEntity>(where, WhereSchema.For<TEntity>(context));

        private void ScheduleRefresh()
        {
            // FetchRequested z Mapsui przychodzi spoza wątku UI; odświeżenie zmienia kolekcje (legenda)
            // i stan widoku, więc zawsze startuje na wątku UI (zapytania i tak idą w Task.Run)
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.InvokeAsync(ScheduleRefresh);
                return;
            }
            _refresh?.Cancel();
            _refresh = new CancellationTokenSource();
            _ = RefreshAsync(_refresh.Token);
        }

        private async Task RefreshAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(RefreshDelay, cancellationToken);
                if (!_zoomedToStart)
                    await ZoomToStartAsync(cancellationToken);

                var viewport = Map.Navigator.Viewport;
                if (viewport.Width <= 0 || viewport.Height <= 0)
                    return;
                var extent = viewport.ToExtent();
                var window = MapProjection.ToPl1992(new Envelope(extent.MinX, extent.MaxX, extent.MinY, extent.MaxY));
                if (window.IsNull)
                    return;

                var settings = Settings;
                var engine = SelectedEngine.Engine;
                var detailed = window.Width <= settings.MaxDetailWindowKm * 1000 && window.Height <= settings.MaxDetailWindowKm * 1000;
                var (showStrefy, showDzialki, showBudynki) = (_strefyNode.IsVisible, _dzialkiNode.IsVisible && detailed, _budynkiNode.IsVisible && detailed);
                var (strefyWhere, dzialkiWhere, budynkiWhere) = (ActiveWhere(_strefyNode), ActiveWhere(_dzialkiNode), ActiveWhere(_budynkiNode));

                var (strefy, dzialki, budynki, symbols) = await Task.Run(async () =>
                {
                    await using var context = _factory.Create(engine);
                    var strefyFilter = CompileFilter<StrefaPlanistyczna>(context, strefyWhere);
                    var s = showStrefy ? await MapDataQuery.StrefyAsync(context, window, settings.MaxStrefy, cancellationToken, strefyFilter) : MapQueryResult.Empty;
                    var d = showDzialki
                        ? await MapDataQuery.DzialkiAsync(context, window, settings.MaxDzialki, cancellationToken, CompileFilter<Dzialka>(context, dzialkiWhere))
                        : MapQueryResult.Empty;
                    var b = showBudynki
                        ? await MapDataQuery.BudynkiAsync(context, window, settings.MaxBudynki, cancellationToken, CompileFilter<Budynek>(context, budynkiWhere))
                        : MapQueryResult.Empty;
                    var z = showStrefy && strefyFilter != null ? await MapDataQuery.ZoneSymbolsAsync(context, strefyFilter, cancellationToken) : null;
                    return (s, d, b, z);
                }, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                SetFeatures(_strefy, strefy);
                SetFeatures(_dzialki, dzialki);
                SetFeatures(_budynki, budynki);
                _zoneSymbols = symbols == null ? null : new HashSet<string>(symbols);
                UpdateLegend();
                _detailed = detailed;
                _lastStatus = (engine, detailed, strefy, dzialki, budynki);
                RefreshStatusFilters();
                _lastError = null;
            }
            catch (OperationCanceledException)
            {
                // nowsze okno widoku zastąpiło to zapytanie
            }
            catch (WhereSyntaxException ex)
            {
                Status = $"Błąd w aktywnym filtrze: {ex.DisplayMessage} Popraw go w oknie Filtry.";
            }
            catch (Exception ex)
            {
                var message = ex.GetBaseException().Message;
                Status = $"Błąd pobierania danych mapy: {message}";
                if (message != _lastError)
                    _host.AddLog($"Mapa ({SelectedEngine.Name}): {message}", isError: true);
                _lastError = message;
            }
        }

        private (DatabaseEngine Engine, bool Detailed, MapQueryResult Strefy, MapQueryResult Dzialki, MapQueryResult Budynki)? _lastStatus;

        /// <summary>Tekst statusu z ostatniego odświeżenia i nazwami aktywnych filtrów (np. po zmianie nazwy).</summary>
        private void RefreshStatusFilters()
        {
            if (_lastStatus is not { } last)
                return;
            var settings = Settings;
            var parts = new List<string> { $"strefy: {Count(last.Strefy)}{FilterSuffix(_strefyNode)}" };
            if (last.Detailed)
            {
                parts.Add($"działki: {Count(last.Dzialki)}{FilterSuffix(_dzialkiNode)}");
                parts.Add($"budynki: {Count(last.Budynki)}{FilterSuffix(_budynkiNode)}");
            }
            var text = $"{(last.Engine == DatabaseEngine.SqlServer ? "SQL Server" : "PostgreSQL")} – {string.Join(", ", parts)}";
            if (!last.Detailed)
                text += $". Przybliż do okna ≤ {settings.MaxDetailWindowKm:0.#} km, aby zobaczyć działki i budynki.";
            else if (last.Dzialki.Truncated || last.Budynki.Truncated)
                text += ". Osiągnięto limit obiektów: przybliż albo zwiększ limit w stylu mapy.";
            Status = text;
        }

        private static string FilterSuffix(LayerNodeViewModel node) =>
            node.ActiveFilter is { } filter ? $" (filtr: {filter.Name})" : "";

        /// <summary>Pierwsze przybliżenie do zasięgu aktu planowania (albo do domyślnego zasięgu Częstochowy).</summary>
        private async Task ZoomToStartAsync(CancellationToken cancellationToken)
        {
            var viewport = Map.Navigator.Viewport;
            if (viewport.Width <= 0 || viewport.Height <= 0)
                return;

            var engine = SelectedEngine.Engine;
            var extent = await Task.Run(async () =>
            {
                await using var context = _factory.Create(engine);
                return await MapDataQuery.StartExtentAsync(context, cancellationToken);
            }, cancellationToken) ?? MapDataQuery.DefaultExtent;

            var mercator = MapProjection.ToWebMercator(extent);
            _zoomedToStart = true;
            Map.Navigator.ZoomToBox(new MRect(mercator.MinX, mercator.MinY, mercator.MaxX, mercator.MaxY), MBoxFit.Fit);
        }

        // Informacje o obiektach (PPM)

        /// <summary>
        /// PPM na mapie (współrzędne 3857): pełne rekordy widocznych warstw w punkcie, z tolerancją kilku pikseli,
        /// w niemodalnym oknie informacji. Obiekty ukryte przez aktywny filtr nie są wskazywane.
        /// Kolejne wskazanie odświeża otwarte okno.
        /// </summary>
        public void Identify(double x, double y)
        {
            var half = IdentifyTolerancePixels * Map.Navigator.Viewport.Resolution;
            if (half <= 0)
                return;
            var area = MapProjection.ToPl1992(new Envelope(x - half, x + half, y - half, y + half));
            if (area.IsNull)
                return;

            if (_info == null)
            {
                var info = new FeatureInfoViewModel(Highlight);
                _info = info;
                _dialogs.ShowFeatureInfo(info, () =>
                {
                    if (_info != info)
                        return;
                    _identify?.Cancel();
                    _info = null;
                    Highlight([]);
                });
            }

            var centre = area.Centre;
            _info.BeginSearch($"{SelectedEngine.Name}, punkt X = {centre.X:0.00}, Y = {centre.Y:0.00} (EPSG:2180)");
            _identify?.Cancel();
            _identify = new CancellationTokenSource();
            _ = IdentifyAsync(_info, area, _identify.Token);
        }

        private async Task IdentifyAsync(FeatureInfoViewModel info, Envelope area, CancellationToken cancellationToken)
        {
            var engine = SelectedEngine;
            // Tylko warstwy, które są teraz narysowane; kolejność jak na mapie od góry
            var (budynki, dzialki, strefy) = (_budynkiNode.IsVisible && _detailed, _dzialkiNode.IsVisible && _detailed, _strefyNode.IsVisible);
            var (strefyWhere, dzialkiWhere, budynkiWhere) = (ActiveWhere(_strefyNode), ActiveWhere(_dzialkiNode), ActiveWhere(_budynkiNode));
            try
            {
                var results = await Task.Run(async () =>
                {
                    await using var context = _factory.Create(engine.Engine);
                    var list = new List<IdentifyResult>();
                    if (budynki)
                        list.Add(await MapIdentifyQuery.BudynkiAsync(context, area, cancellationToken, CompileFilter<Budynek>(context, budynkiWhere)));
                    if (dzialki)
                        list.Add(await MapIdentifyQuery.DzialkiAsync(context, area, cancellationToken, CompileFilter<Dzialka>(context, dzialkiWhere)));
                    if (strefy)
                        list.Add(await MapIdentifyQuery.StrefyAsync(context, area, cancellationToken, CompileFilter<StrefaPlanistyczna>(context, strefyWhere)));
                    return list;
                }, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (info == _info)
                    info.SetResults(results);
            }
            catch (OperationCanceledException)
            {
                // nowe wskazanie albo zamknięcie okna
            }
            catch (Exception ex)
            {
                var message = ex is WhereSyntaxException syntax ? $"aktywny filtr – {syntax.DisplayMessage}" : ex.GetBaseException().Message;
                if (info == _info)
                    info.SetError(message);
                _host.AddLog($"Informacje o obiektach ({engine.Name}): {message}", isError: true);
            }
        }

        /// <summary>Podświetlenie obiektów z okna informacji (geometria z bazy, przeliczona do 3857).</summary>
        private void Highlight(IReadOnlyList<FeatureRecord> records)
        {
            _podswietlenie.Features = records
                .Where(r => r.Geometry != null)
                .Select(r => (IFeature)new GeometryFeature(MapProjection.ToWebMercator(r.Geometry!)))
                .ToList();
            _podswietlenie.DataHasChanged();
        }

        private static void SetFeatures(MemoryLayer layer, MapQueryResult result)
        {
            layer.Features = result.Features.Select(feature =>
            {
                var mapFeature = new GeometryFeature(feature.Geometry);
                mapFeature[MapsuiStyles.SymbolKey] = feature.Category;
                mapFeature["id"] = feature.Id;
                mapFeature["label"] = feature.Label;
                return (IFeature)mapFeature;
            }).ToList();
            layer.DataHasChanged();
        }

        private static string Count(MapQueryResult result) => result.Truncated ? $"{result.Features.Count}+" : result.Features.Count.ToString();
    }
}
