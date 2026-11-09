using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoBench.Core.Styles;
using GeoBench.Wpf.Styles;

namespace GeoBench.Wpf.ViewModels
{
    /// <summary>Pozycja listy w oknie stylu: warstwa (działki, budynki) albo rodzaj strefy POG.</summary>
    public sealed partial class StyleItemViewModel : ObservableObject
    {
        private readonly LayerStyle _style;
        private readonly Func<LayerStyle> _defaults;
        private readonly Action _changed;

        public StyleItemViewModel(string group, string title, LayerStyle style, Func<LayerStyle> defaults, Action changed)
        {
            Group = group;
            Title = title;
            _style = style;
            _defaults = defaults;
            _changed = changed;
        }

        public string Group { get; }

        public string Title { get; }

        public Color? FillColor
        {
            get => MapsuiStyles.ParseColor(_style.FillColor);
            set => Update(() => _style.FillColor = MapsuiStyles.ToHex(value ?? Colors.Gray));
        }

        public Color? OutlineColor
        {
            get => MapsuiStyles.ParseColor(_style.OutlineColor);
            set => Update(() => _style.OutlineColor = MapsuiStyles.ToHex(value ?? Colors.Black));
        }

        /// <summary>Krycie wypełnienia w procentach (0 = sam obrys).</summary>
        public double FillOpacityPercent
        {
            get => Math.Round(_style.FillOpacity * 100);
            set => Update(() => _style.FillOpacity = Math.Clamp(value, 0, 100) / 100);
        }

        public double OutlineWidth
        {
            get => _style.OutlineWidth;
            set => Update(() => _style.OutlineWidth = Math.Clamp(value, 0, 10));
        }

        /// <summary>Podgląd w liście: kolor wypełnienia z kryciem.</summary>
        public SolidColorBrush Preview
        {
            get
            {
                var color = MapsuiStyles.ParseColor(_style.FillColor);
                color.A = (byte)Math.Round(_style.FillOpacity * 255);
                return new SolidColorBrush(color);
            }
        }

        public SolidColorBrush OutlinePreview => new(MapsuiStyles.ParseColor(_style.OutlineColor));

        public void ResetToDefault()
        {
            var defaults = _defaults();
            _style.FillColor = defaults.FillColor;
            _style.FillOpacity = defaults.FillOpacity;
            _style.OutlineColor = defaults.OutlineColor;
            _style.OutlineWidth = defaults.OutlineWidth;
            OnPropertyChanged(string.Empty);
            _changed();
        }

        private void Update(Action change)
        {
            change();
            OnPropertyChanged(string.Empty);
            _changed();
        }
    }

    /// <summary>
    /// Okno stylu mapy: kolory działek, budynków i stref POG oraz zasady wyświetlania (próg przybliżenia, limity).
    /// Zmiany są od razu pokazywane na mapie; Anuluj przywraca stan sprzed otwarcia (robi to wywołujący).
    /// </summary>
    public sealed partial class MapStyleViewModel : ObservableObject
    {
        private readonly Action<MapStyleSettings> _preview;

        public MapStyleViewModel(MapStyleSettings settings, Action<MapStyleSettings> preview)
        {
            Settings = settings;
            _preview = preview;
            BuildItems();
            _selectedItem = Items.FirstOrDefault();
        }

        /// <summary>Edytowana kopia ustawień.</summary>
        public MapStyleSettings Settings { get; }

        public List<StyleItemViewModel> Items { get; } = [];

        [ObservableProperty]
        private StyleItemViewModel? _selectedItem;

        public double PlanOpacityPercent
        {
            get => Math.Round(Settings.PlanOpacity * 100);
            set
            {
                Settings.PlanOpacity = Math.Clamp(value, 0, 100) / 100;
                OnPropertyChanged();
                Changed();
            }
        }

        public double MaxDetailWindowKm
        {
            get => Settings.MaxDetailWindowKm;
            set
            {
                Settings.MaxDetailWindowKm = Math.Clamp(value, 0.1, 100);
                OnPropertyChanged();
            }
        }

        public int MaxDzialki
        {
            get => Settings.MaxDzialki;
            set
            {
                Settings.MaxDzialki = Math.Clamp(value, 100, 500000);
                OnPropertyChanged();
            }
        }

        public int MaxBudynki
        {
            get => Settings.MaxBudynki;
            set
            {
                Settings.MaxBudynki = Math.Clamp(value, 100, 500000);
                OnPropertyChanged();
            }
        }

        public int MaxStrefy
        {
            get => Settings.MaxStrefy;
            set
            {
                Settings.MaxStrefy = Math.Clamp(value, 100, 100000);
                OnPropertyChanged();
            }
        }

        private void BuildItems()
        {
            Items.Add(new StyleItemViewModel("Ewidencja", "Działki", Settings.Dzialki, MapStyleSettings.DefaultDzialki, Changed));
            Items.Add(new StyleItemViewModel("Ewidencja", "Budynki", Settings.Budynki, MapStyleSettings.DefaultBudynki, Changed));
            foreach (var kind in MapStyleSettings.ZoneKinds)
            {
                Items.Add(new StyleItemViewModel("Plan ogólny gminy", $"{kind.Symbol} – strefa {kind.Name}",
                    Settings.Strefa(kind.Symbol), () => MapStyleSettings.DefaultStrefa(kind.Symbol), Changed));
            }
            Items.Add(new StyleItemViewModel("Zaznaczenie", "Podświetlenie wskazanych obiektów", Settings.Podswietlenie,
                MapStyleSettings.DefaultPodswietlenie, Changed));
        }

        private void Changed() => _preview(Settings);

        [RelayCommand]
        private void ResetSelected() => SelectedItem?.ResetToDefault();

        [RelayCommand]
        private void ResetAll()
        {
            foreach (var item in Items)
                item.ResetToDefault();
            var defaults = new MapStyleSettings();
            PlanOpacityPercent = defaults.PlanOpacity * 100;
            MaxDetailWindowKm = defaults.MaxDetailWindowKm;
            MaxDzialki = defaults.MaxDzialki;
            MaxBudynki = defaults.MaxBudynki;
            MaxStrefy = defaults.MaxStrefy;
        }
    }
}
