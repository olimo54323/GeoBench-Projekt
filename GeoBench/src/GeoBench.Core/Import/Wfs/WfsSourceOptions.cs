using System.Text.Json;
using System.Text.Json.Serialization;
using GeoBench.Core.Configuration;

namespace GeoBench.Core.Import.Wfs
{
    /// <summary>Encja, do której trafiają obiekty warstwy.</summary>
    public enum EgibTarget
    {
        Dzialka,
        Budynek
    }

    /// <summary>Warstwa (typ obiektu) usługi WFS.</summary>
    public sealed class WfsLayerOptions
    {
        /// <summary>Krótka nazwa, używana też jako katalog w cache (np. dzialki).</summary>
        public required string Name { get; init; }

        public string? Title { get; init; }

        /// <summary>Nazwa typu z prefiksem przestrzeni nazw (np. gmgml:dzialki).</summary>
        public required string TypeName { get; init; }

        public EgibTarget Target { get; init; }

        /// <summary>Pole sortowania: bez stałej kolejności STARTINDEX może gubić lub powtarzać obiekty.</summary>
        public string? SortBy { get; init; }

        /// <summary>Nazwa lokalna elementu obiektu w odpowiedzi (TypeName bez prefiksu).</summary>
        public string LocalTypeName => TypeName[(TypeName.IndexOf(':') + 1)..];
    }

    /// <summary>Usługa WFS z listy sources.json.</summary>
    public sealed class WfsSourceOptions
    {
        public required string Name { get; init; }

        public string? Title { get; init; }

        /// <summary>Adres punktu GetFeature (bez parametrów).</summary>
        public required string Url { get; init; }

        public string Version { get; init; } = "2.0.0";

        /// <summary>Układ żądany od serwera; geometria i tak przechodzi przez transformację do EPSG:2180.</summary>
        public string SrsName { get; init; } = "urn:ogc:def:crs:EPSG::2180";

        public int PageSize { get; init; } = 1000;

        /// <summary>Liczba równoczesnych żądań, żeby nie przeciążać usługi.</summary>
        public int MaxParallelRequests { get; init; } = 2;

        /// <summary>Parametr NAMESPACES (WFS 2.0), gdy serwer wymaga deklaracji prefiksu z TypeName.</summary>
        public string? Namespaces { get; init; }

        /// <summary>Dodatkowe parametry dopisywane do każdego żądania (np. BBOX=...).</summary>
        public string? ExtraQuery { get; init; }

        public IReadOnlyList<WfsLayerOptions> Layers { get; init; } = [];
    }

    /// <summary>Lista źródeł z pliku sources.json (obok GeoBench.slnx, w repozytorium).</summary>
    public static class WfsSourceCatalog
    {
        public const string FileName = "sources.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private sealed class SourcesFile
        {
            public List<WfsSourceOptions> Sources { get; init; } = [];
        }

        /// <summary>Źródła z sources.json znalezionego w górę drzewa katalogów; pusta lista, gdy pliku brak.</summary>
        public static IReadOnlyList<WfsSourceOptions> Load()
        {
            var path = GeoBenchConfiguration.FindFile(FileName);
            return path == null ? [] : Load(path);
        }

        public static IReadOnlyList<WfsSourceOptions> Load(string path)
        {
            using var stream = File.OpenRead(path);
            var file = JsonSerializer.Deserialize<SourcesFile>(stream, JsonOptions)
                ?? throw new InvalidDataException($"Plik {path} jest pusty.");
            return file.Sources;
        }

        /// <summary>Katalog cache stron WFS: GeoBench/cache obok sources.json (poza gitem).</summary>
        public static string DefaultCacheDirectory()
        {
            var path = GeoBenchConfiguration.FindFile(FileName) ?? GeoBenchConfiguration.FindFile(GeoBenchConfiguration.FileName);
            var baseDirectory = path == null ? AppContext.BaseDirectory : Path.GetDirectoryName(path)!;
            return Path.Combine(baseDirectory, "cache");
        }
    }
}
