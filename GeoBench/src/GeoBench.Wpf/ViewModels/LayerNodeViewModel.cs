using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoBench.Core.Data.Filters;
using GeoBench.Core.Styles;
using GeoBench.Wpf.Styles;

namespace GeoBench.Wpf.ViewModels
{
    /// <summary>Warstwa danych na mapie.</summary>
    public enum MapLayerKind
    {
        Strefy,
        Dzialki,
        Budynki
    }

    /// <summary>
    /// Wiersz panelu warstw z kwadracikiem stylu: warstwa bazowa albo jej filtr.
    /// Klik w kwadracik otwiera popup stylu; po zamknięciu popupu styl jest zapisywany.
    /// </summary>
    public abstract partial class MapLayerItemViewModel : ObservableObject
    {
        public abstract string Title { get; }

        public abstract Brush SwatchFill { get; }

        public abstract Brush SwatchOutline { get; }

        /// <summary>Edytor kolorów w popupie; null, gdy warstwa nie ma jednego stylu (strefy: kolory rodzajów).</summary>
        public abstract StyleItemViewModel? StyleEditor { get; }

        public bool HasStyleEditor => StyleEditor != null;

        public bool HasNoStyleEditor => StyleEditor == null;

        /// <summary>Czy kolory w popupie można zmieniać (filtr ze stylem warstwy bazowej: nie).</summary>
        public virtual bool IsStyleEditable => true;

        /// <summary>Czy popup pokazuje przełącznik „Styl warstwy bazowej” (tylko filtry).</summary>
        public virtual bool CanUseLayerStyle => false;

        public virtual bool UseLayerStyle { get; set; }

        [ObservableProperty]
        private bool _isStylePopupOpen;

        partial void OnIsStylePopupOpenChanged(bool value)
        {
            if (!value)
                CommitStyle();
        }

        [RelayCommand]
        private void OpenStyle() => IsStylePopupOpen = true;

        /// <summary>Zapis stylu po zamknięciu popupu.</summary>
        protected abstract void CommitStyle();

        protected void RaiseSwatch()
        {
            OnPropertyChanged(nameof(SwatchFill));
            OnPropertyChanged(nameof(SwatchOutline));
        }

        protected static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>Podgląd wypełnienia; całkiem przezroczyste wypełnienie (działki) zostaje puste, widać sam obrys.</summary>
        protected static Brush Fill(LayerStyle style)
        {
            var color = MapsuiStyles.ParseColor(style.FillColor);
            color.A = (byte)Math.Round(Math.Clamp(style.FillOpacity, 0, 1) * 255);
            return Frozen(color);
        }

        protected static Brush Outline(LayerStyle style) => Frozen(MapsuiStyles.ParseColor(style.OutlineColor));

        /// <summary>Mozaika czterech kolorów stref jako znak „kolory według rodzaju”.</summary>
        protected static Brush ZoneMosaic(MapStyleSettings settings)
        {
            var group = new DrawingGroup();
            var symbols = new[] { "SW", "SU", "SN", "SK" };
            for (var i = 0; i < symbols.Length; i++)
            {
                var style = settings.Strefa(symbols[i]);
                group.Children.Add(new GeometryDrawing(Frozen(MapsuiStyles.ParseColor(style.FillColor)), null,
                    new RectangleGeometry(new Rect(i % 2, i / 2, 1, 1))));
            }
            var brush = new DrawingBrush(group) { Stretch = Stretch.Fill };
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>Warstwa bazowa w panelu: widoczność, styl i lista filtrów (najwyżej jeden aktywny).</summary>
    public sealed partial class LayerNodeViewModel : MapLayerItemViewModel
    {
        private readonly MapViewModel _map;
        private readonly Func<LayerStyle>? _defaults;
        private StyleItemViewModel? _styleEditor;

        public LayerNodeViewModel(MapViewModel map, MapLayerKind kind, string table, string title, Func<LayerStyle>? defaults)
        {
            _map = map;
            Kind = kind;
            Table = table;
            Title = title;
            _defaults = defaults;
        }

        public MapLayerKind Kind { get; }

        /// <summary>Tabela w bazie, której dotyczą filtry (dzialka, budynek, strefa_planistyczna).</summary>
        public string Table { get; }

        public override string Title { get; }

        public ObservableCollection<FilterNodeViewModel> Filters { get; } = [];

        public FilterNodeViewModel? ActiveFilter => Filters.FirstOrDefault(f => f.IsActive);

        [ObservableProperty]
        private bool _isVisible = true;

        partial void OnIsVisibleChanged(bool value) => _map.OnLayerChanged(this);

        public override StyleItemViewModel? StyleEditor => _styleEditor;

        public override Brush SwatchFill => Kind == MapLayerKind.Strefy || _styleEditor == null
            ? ZoneMosaic(_map.AppliedSettings)
            : Fill(_map.StyleOf(this)!);

        public override Brush SwatchOutline => Kind == MapLayerKind.Strefy || _styleEditor == null
            ? Frozen(Colors.DimGray)
            : Outline(_map.StyleOf(this)!);

        /// <summary>Podpina edytor do stylu z bieżących ustawień (po wczytaniu lub zmianie ustawień mapy).</summary>
        internal void RefreshStyle()
        {
            var style = _map.StyleOf(this);
            _styleEditor = style == null || _defaults == null
                ? null
                : new StyleItemViewModel("", Title, style, _defaults, OnStyleEdited);
            OnPropertyChanged(nameof(StyleEditor));
            OnPropertyChanged(nameof(HasStyleEditor));
            OnPropertyChanged(nameof(HasNoStyleEditor));
            RaiseSwatch();
            foreach (var filter in Filters)
                filter.RefreshSwatch();
        }

        private void OnStyleEdited()
        {
            RaiseSwatch();
            foreach (var filter in Filters.Where(f => f.UseLayerStyle))
                filter.RefreshSwatch();
            _map.OnLayerStyleChanged(this);
        }

        protected override void CommitStyle() => _map.SaveSettings();

        [RelayCommand]
        private void Toggle() => IsVisible = !IsVisible;

        [RelayCommand]
        private void EditMapStyle()
        {
            IsStylePopupOpen = false;
            _map.EditStyleCommand.Execute(null);
        }

        /// <summary>Zaznaczenie filtra: pozostałe filtry warstwy są odznaczane, warstwa włączana.</summary>
        internal void Activate(FilterNodeViewModel active)
        {
            foreach (var filter in Filters.Where(f => f != active))
                filter.SetActiveSilently(false);
            if (!IsVisible)
                IsVisible = true; // samo odświeża mapę
            else
                _map.OnLayerChanged(this);
        }

        internal void Deactivated() => _map.OnLayerChanged(this);
    }

    /// <summary>Zapisany filtr pod warstwą: checkbox, nazwa (edytowalna w miejscu) i styl.</summary>
    public sealed partial class FilterNodeViewModel : MapLayerItemViewModel
    {
        private readonly MapViewModel _map;
        private StyleItemViewModel _styleEditor;
        private bool _silent;

        public FilterNodeViewModel(MapViewModel map, LayerNodeViewModel layer, MapFilter filter)
        {
            _map = map;
            Layer = layer;
            Filter = filter;
            _styleEditor = CreateEditor();
        }

        public LayerNodeViewModel Layer { get; }

        public MapFilter Filter { get; }

        public override string Title => Filter.Name;

        public string Name => Filter.Name;

        [ObservableProperty]
        private bool _isActive;

        partial void OnIsActiveChanged(bool value)
        {
            if (_silent)
                return;
            if (value)
                Layer.Activate(this);
            else
                Layer.Deactivated();
        }

        internal void SetActiveSilently(bool value)
        {
            _silent = true;
            IsActive = value;
            _silent = false;
        }

        [RelayCommand]
        private void Toggle() => IsActive = !IsActive;

        public override bool CanUseLayerStyle => true;

        public override bool UseLayerStyle
        {
            get => Filter.UseLayerStyle;
            set
            {
                if (Filter.UseLayerStyle == value)
                    return;
                Filter.UseLayerStyle = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsStyleEditable));
                RefreshSwatch();
                _map.OnLayerStyleChanged(Layer);
            }
        }

        public override bool IsStyleEditable => !Filter.UseLayerStyle;

        public override StyleItemViewModel? StyleEditor => _styleEditor;

        public override Brush SwatchFill => UseLayerStyle ? Layer.SwatchFill : Fill(Filter.Style);

        public override Brush SwatchOutline => UseLayerStyle ? Layer.SwatchOutline : Outline(Filter.Style);

        internal void RefreshSwatch() => RaiseSwatch();

        protected override void CommitStyle() => _map.SaveFilter(this);

        private StyleItemViewModel CreateEditor()
        {
            var original = Filter.Style.Clone();
            return new StyleItemViewModel("", Filter.Name, Filter.Style, () => original.Clone(), () =>
            {
                RaiseSwatch();
                _map.OnLayerStyleChanged(Layer);
            });
        }

        // Zmiana nazwy w miejscu (F2 albo menu kontekstowe)

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotRenaming))]
        private bool _isRenaming;

        public bool IsNotRenaming => !IsRenaming;

        [ObservableProperty]
        private string _editName = "";

        [RelayCommand]
        private void StartRename()
        {
            EditName = Filter.Name;
            IsRenaming = true;
        }

        public void CommitRename()
        {
            if (!IsRenaming)
                return;
            IsRenaming = false;
            _map.RenameFilter(this, EditName);
        }

        public void CancelRename() => IsRenaming = false;

        internal void NameChanged()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Title));
        }

        [RelayCommand]
        private void OpenInBuilder() => _map.OpenFilterBuilder(this);
    }
}
