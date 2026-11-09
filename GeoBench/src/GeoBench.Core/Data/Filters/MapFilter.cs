using System.Text.Json;
using GeoBench.Core.Styles;

namespace GeoBench.Core.Data.Filters
{
    /// <summary>Zapisany filtr warstwy: nazwa (wyświetlana pod warstwą), tabela, warunek WHERE i styl.</summary>
    public sealed class MapFilter
    {
        public string Name { get; set; } = "";

        /// <summary>Tabela w bazie (dzialka, budynek, strefa_planistyczna).</summary>
        public string Table { get; set; } = "";

        /// <summary>Warunek w składni bazowej klauzuli WHERE; pusty = wszystkie obiekty.</summary>
        public string Where { get; set; } = "";

        /// <summary>Rysowanie stylem warstwy bazowej (dla stref: kolorami rodzajów) zamiast własnego.</summary>
        public bool UseLayerStyle { get; set; }

        public LayerStyle Style { get; set; } = new();

        public DateTime Modified { get; set; }

        public MapFilter Clone() => new()
        {
            Name = Name,
            Table = Table,
            Where = Where,
            UseLayerStyle = UseLayerStyle,
            Style = Style.Clone(),
            Modified = Modified
        };
    }

    /// <summary>
    /// Filtry zapisane jako pliki JSON: <c>&lt;katalog&gt;\&lt;tabela&gt;\&lt;nazwa&gt;.json</c>, domyślnie
    /// w %APPDATA%\GeoBench\filters. Nazwa w pliku jest pełna; w nazwie pliku znaki niedozwolone zastępuje _.
    /// Nazwy są unikalne w obrębie tabeli bez względu na wielkość liter (jak nazwy plików w Windows).
    /// </summary>
    public sealed class FilterStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public FilterStore(string root)
        {
            Root = root;
        }

        public static string DefaultRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GeoBench", "filters");

        public string Root { get; }

        public string FilePath(string table, string name) => Path.Combine(Root, table, FileName(name) + ".json");

        /// <summary>Nazwa pliku (bez rozszerzenia) dla nazwy filtra.</summary>
        public static string FileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            var result = new string(chars).TrimEnd('.', ' ');
            if (result.Length > 100)
                result = result[..100];
            if (result.Length == 0 || ReservedNames.Contains(result))
                result = "_" + result;
            return result;
        }

        /// <summary>Filtry tabeli posortowane po nazwie; uszkodzone pliki są pomijane, a opis błędu trafia do errors.</summary>
        public IReadOnlyList<MapFilter> Load(string table, ICollection<string>? errors = null)
        {
            var directory = Path.Combine(Root, table);
            if (!Directory.Exists(directory))
                return [];

            var filters = new List<MapFilter>();
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                try
                {
                    var filter = JsonSerializer.Deserialize<MapFilter>(File.ReadAllText(path), JsonOptions);
                    if (filter == null)
                        continue;
                    filter.Table = table;
                    if (string.IsNullOrWhiteSpace(filter.Name))
                        filter.Name = Path.GetFileNameWithoutExtension(path);
                    filter.Style ??= new LayerStyle();
                    filter.Where ??= "";
                    filters.Add(filter);
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    errors?.Add($"{path}: {ex.Message}");
                }
            }
            return filters.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Czy nazwa jest zajęta przez inny filtr tabeli (exceptName = bieżąca nazwa edytowanego filtra).</summary>
        public bool IsNameTaken(string table, string name, string? exceptName = null)
        {
            if (exceptName != null && string.Equals(FileName(name), FileName(exceptName), StringComparison.OrdinalIgnoreCase))
                return false;
            return File.Exists(FilePath(table, name));
        }

        /// <summary>
        /// Zapisuje filtr; previousName to nazwa przed zmianą (plik o starej nazwie jest usuwany po zapisie nowego).
        /// </summary>
        public void Save(MapFilter filter, string? previousName = null)
        {
            filter.Name = filter.Name.Trim();
            if (filter.Name.Length == 0)
                throw new ArgumentException("Nazwa filtra nie może być pusta.", nameof(filter));
            if (IsNameTaken(filter.Table, filter.Name, previousName))
                throw new InvalidOperationException($"Filtr o nazwie „{filter.Name}” już istnieje w warstwie {filter.Table}.");

            var path = FilePath(filter.Table, filter.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            filter.Modified = DateTime.Now;
            File.WriteAllText(path, JsonSerializer.Serialize(filter, JsonOptions));

            if (previousName != null)
            {
                var previousPath = FilePath(filter.Table, previousName);
                if (!string.Equals(previousPath, path, StringComparison.OrdinalIgnoreCase) && File.Exists(previousPath))
                    File.Delete(previousPath);
            }
        }

        public void Delete(string table, string name)
        {
            var path = FilePath(table, name);
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
