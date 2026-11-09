using System.IO;
using System.Text.Json;
using GeoBench.Core.Styles;

namespace GeoBench.Wpf.Styles
{
    /// <summary>Rodzaj strefy planistycznej: symbol i nazwa (lista kodowa RodzajStrefyPlanistycznejKod).</summary>
    public sealed record ZoneKind(string Symbol, string Name, string DefaultColor);

    /// <summary>
    /// Ustawienia mapy: kolory działek, budynków i każdego rodzaju strefy POG oraz zasady pobierania danych
    /// (próg przybliżenia i limity obiektów). Zapisywane w %APPDATA%\GeoBench\styl-mapy.json.
    /// </summary>
    public sealed class MapStyleSettings
    {
        public const string FileName = "styl-mapy.json";

        /// <summary>13 rodzajów stref z domyślną paletą (własna, zbliżona do konwencji planistycznych, nie urzędowa).</summary>
        public static readonly IReadOnlyList<ZoneKind> ZoneKinds =
        [
            new("SW", "wielofunkcyjna z zabudową mieszkaniową wielorodzinną", "#E8A33D"),
            new("SJ", "wielofunkcyjna z zabudową mieszkaniową jednorodzinną", "#F5D76E"),
            new("SZ", "wielofunkcyjna z zabudową zagrodową", "#D9C27A"),
            new("SU", "usługowa", "#E0565B"),
            new("SH", "handlu wielkopowierzchniowego", "#B23A6F"),
            new("SP", "gospodarcza", "#8E7CC3"),
            new("SR", "produkcji rolniczej", "#C9DE8C"),
            new("SI", "infrastrukturalna", "#9AA5B1"),
            new("SN", "zieleni i rekreacji", "#6DBE6A"),
            new("SC", "cmentarzy", "#4E8F5A"),
            new("SG", "górnictwa", "#8C6D4F"),
            new("SO", "otwarta", "#B7E1A1"),
            new("SK", "komunikacyjna", "#BDBDBD")
        ];

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public LayerStyle Dzialki { get; set; } = DefaultDzialki();

        public LayerStyle Budynki { get; set; } = DefaultBudynki();

        /// <summary>Podświetlenie obiektów wskazanych prawym przyciskiem (okno informacji).</summary>
        public LayerStyle Podswietlenie { get; set; } = DefaultPodswietlenie();

        /// <summary>Styl każdego rodzaju strefy według symbolu (SW, SJ, …).</summary>
        public Dictionary<string, LayerStyle> Strefy { get; set; } = DefaultStrefy();

        /// <summary>Krycie całej warstwy planu ogólnego nad podkładem (0–1).</summary>
        public double PlanOpacity { get; set; } = 0.8;

        /// <summary>Działki i budynki są pobierane, gdy okno widoku ma najwyżej tyle kilometrów w każdym kierunku.</summary>
        public double MaxDetailWindowKm { get; set; } = 3;

        public int MaxDzialki { get; set; } = 20000;

        public int MaxBudynki { get; set; } = 20000;

        public int MaxStrefy { get; set; } = 5000;

        public static LayerStyle DefaultDzialki() => new() { FillColor = "#FFFFFF", FillOpacity = 0, OutlineColor = "#D62728", OutlineWidth = 1 };

        public static LayerStyle DefaultBudynki() => new() { FillColor = "#7F7F7F", FillOpacity = 0.7, OutlineColor = "#404040", OutlineWidth = 0.5 };

        /// <summary>Czerwone półprzezroczyste wypełnienie i grubszy obrys, jak zaznaczenie w programach GIS.</summary>
        public static LayerStyle DefaultPodswietlenie() => new() { FillColor = "#FF0000", FillOpacity = 0.3, OutlineColor = "#FF0000", OutlineWidth = 3 };

        public static LayerStyle DefaultStrefa(string symbol)
        {
            var kind = ZoneKinds.FirstOrDefault(k => k.Symbol == symbol);
            return new LayerStyle { FillColor = kind?.DefaultColor ?? "#CCCCCC", FillOpacity = 0.6, OutlineColor = "#555555", OutlineWidth = 0.8 };
        }

        private static Dictionary<string, LayerStyle> DefaultStrefy() =>
            ZoneKinds.ToDictionary(k => k.Symbol, k => DefaultStrefa(k.Symbol));

        /// <summary>Styl strefy o danym symbolu; brakujący (np. po dopisaniu rodzaju) dostaje wartość domyślną.</summary>
        public LayerStyle Strefa(string? symbol)
        {
            symbol ??= "";
            if (!Strefy.TryGetValue(symbol, out var style))
                Strefy[symbol] = style = DefaultStrefa(symbol);
            return style;
        }

        public MapStyleSettings Clone() => new()
        {
            Dzialki = Dzialki.Clone(),
            Budynki = Budynki.Clone(),
            Podswietlenie = Podswietlenie.Clone(),
            Strefy = Strefy.ToDictionary(p => p.Key, p => p.Value.Clone()),
            PlanOpacity = PlanOpacity,
            MaxDetailWindowKm = MaxDetailWindowKm,
            MaxDzialki = MaxDzialki,
            MaxBudynki = MaxBudynki,
            MaxStrefy = MaxStrefy
        };

        public static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GeoBench", FileName);

        /// <summary>Ustawienia z pliku; brak lub uszkodzony plik daje wartości domyślne.</summary>
        public static MapStyleSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new MapStyleSettings();
                var settings = JsonSerializer.Deserialize<MapStyleSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new MapStyleSettings();
                foreach (var kind in ZoneKinds)
                    settings.Strefa(kind.Symbol);
                return settings;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return new MapStyleSettings();
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
    }
}
