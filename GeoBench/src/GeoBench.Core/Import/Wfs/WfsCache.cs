using System.Text.Json;
using GeoBench.Core.Import.Gml;

namespace GeoBench.Core.Import.Wfs
{
    /// <summary>Opis pobranej warstwy: z jakimi parametrami i ile stron powinno leżeć w cache.</summary>
    public sealed class WfsManifest
    {
        public required string Source { get; init; }
        public required string Layer { get; init; }
        public required string TypeName { get; init; }
        public required string SrsName { get; init; }
        public string? SortBy { get; init; }
        public int NumberMatched { get; init; }
        public int PageSize { get; init; }
        public DateTime CreatedUtc { get; init; }

        public int PageCount => NumberMatched == 0 ? 0 : (NumberMatched + PageSize - 1) / PageSize;

        /// <summary>Liczba obiektów oczekiwana na stronie (ostatnia bywa niepełna).</summary>
        public int ExpectedCount(int page) => Math.Min(PageSize, NumberMatched - page * PageSize);

        /// <summary>Czy manifest pasuje do bieżącej konfiguracji źródła (inaczej strony trzeba pobrać od nowa).</summary>
        public bool Matches(WfsSourceOptions source, WfsLayerOptions layer) =>
            PageSize == source.PageSize && SrsName == source.SrsName && TypeName == layer.TypeName && SortBy == layer.SortBy;
    }

    /// <summary>
    /// Strony WFS na dysku: &lt;katalog&gt;/&lt;źródło&gt;/&lt;warstwa&gt;/page_00000.gml i manifest.json.
    /// Obie bazy ładują te same pliki, więc dostają identyczne dane, a ponowny import nie wymaga sieci.
    /// </summary>
    public sealed class WfsCache
    {
        private const string ManifestFileName = "manifest.json";
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public WfsCache(string rootDirectory)
        {
            RootDirectory = rootDirectory;
        }

        public string RootDirectory { get; }

        public string LayerDirectory(WfsSourceOptions source, WfsLayerOptions layer) =>
            Path.Combine(RootDirectory, source.Name, layer.Name);

        public string PagePath(WfsSourceOptions source, WfsLayerOptions layer, int page) =>
            Path.Combine(LayerDirectory(source, layer), $"page_{page:D5}.gml");

        public WfsManifest? ReadManifest(WfsSourceOptions source, WfsLayerOptions layer)
        {
            var path = Path.Combine(LayerDirectory(source, layer), ManifestFileName);
            if (!File.Exists(path))
                return null;
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<WfsManifest>(stream, JsonOptions);
        }

        public void WriteManifest(WfsSourceOptions source, WfsLayerOptions layer, WfsManifest manifest)
        {
            var directory = LayerDirectory(source, layer);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, ManifestFileName);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(manifest, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }

        public IReadOnlyList<int> MissingPages(WfsSourceOptions source, WfsLayerOptions layer, WfsManifest manifest) =>
            Enumerable.Range(0, manifest.PageCount)
                .Where(page => !File.Exists(PagePath(source, layer, page)))
                .ToList();

        /// <summary>Kompletna warstwa: manifest zgodny z konfiguracją i wszystkie strony na dysku.</summary>
        public bool IsComplete(WfsSourceOptions source, WfsLayerOptions layer)
        {
            var manifest = ReadManifest(source, layer);
            return manifest != null && manifest.Matches(source, layer) && MissingPages(source, layer, manifest).Count == 0;
        }

        /// <summary>Obiekty warstwy strona po stronie (strumieniowo: w pamięci jest jeden obiekt naraz).</summary>
        /// <exception cref="InvalidOperationException">Warstwa nie jest kompletna w cache.</exception>
        public IEnumerable<FeatureRecord> ReadFeatures(WfsSourceOptions source, WfsLayerOptions layer)
        {
            var manifest = ReadManifest(source, layer)
                ?? throw new InvalidOperationException($"Brak warstwy {source.Name}/{layer.Name} w cache; najpierw ją pobierz.");
            var missing = MissingPages(source, layer, manifest);
            if (missing.Count > 0)
                throw new InvalidOperationException($"W cache brakuje {missing.Count} z {manifest.PageCount} stron warstwy {source.Name}/{layer.Name}.");

            return ReadPages(source, layer, manifest.PageCount);
        }

        private IEnumerable<FeatureRecord> ReadPages(WfsSourceOptions source, WfsLayerOptions layer, int pageCount)
        {
            var reader = new GmlFeatureReader();
            for (var page = 0; page < pageCount; page++)
            {
                using var stream = File.OpenRead(PagePath(source, layer, page));
                foreach (var record in reader.Read(stream))
                    yield return record;
            }
        }
    }
}
