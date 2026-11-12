using System.Text.Json;
using System.Text.Json.Serialization;
using GeoBench.Core.Benchmark;
using GeoBench.Core.Benchmark.Measurement;
using GeoBench.Core.Data;
using GeoBench.Core.Validation;

namespace GeoBench.Core.Reporting
{
    /// <summary>Porównanie ze wzorcem odczytane z raportu (bez przykładów różnic).</summary>
    public sealed record StoredComparison(bool Equal, int OnlyInExpected, int OnlyInActual, double? MeasureDifference);

    /// <summary>Wariant odczytany z report.json (tylko do odczytu, np. do wykresów).</summary>
    public sealed class StoredVariant
    {
        public string Code { get; init; } = "";
        public string Label { get; init; } = "";
        public string Description { get; init; } = "";
        public DatabaseEngine Engine { get; init; }
        public string Strategy { get; init; } = "";
        public List<KeyValuePair<string, string>> Server { get; init; } = [];
        public List<double> RunsMs { get; init; } = [];
        public MeasurementStatistics? Total { get; init; }
        public Dictionary<string, MeasurementStatistics> Phases { get; init; } = [];
        public ResultFingerprint? Fingerprint { get; init; }
        public bool ConsistentAcrossRuns { get; init; } = true;
        public StoredComparison? Comparison { get; init; }
        public string? ComparedWith { get; init; }
        public List<string> Notes { get; init; } = [];
        public bool Saved { get; init; }
        public string? Error { get; init; }

        [JsonIgnore]
        public bool Succeeded => Error == null && Total != null;

        public string? ServerSetting(string key) => Server.FirstOrDefault(s => s.Key == key).Value;
    }

    /// <summary>Raport serii odczytany z report.json.</summary>
    public sealed class StoredReport
    {
        public string ScenarioId { get; init; } = "";
        public string ScenarioName { get; init; } = "";
        public BenchmarkOptions Options { get; init; } = new();
        public DateTime StartedAt { get; init; }
        public DateTime? FinishedAt { get; init; }
        public Dictionary<string, string> Environment { get; init; } = [];
        public List<LayerCount> Counts { get; init; } = [];
        public int? ReferencePairs { get; init; }
        public List<StoredVariant> Variants { get; init; } = [];

        /// <summary>Katalog, z którego wczytano raport (wykresy eksportowane są do jego podkatalogu).</summary>
        [JsonIgnore]
        public string Directory { get; set; } = "";

        /// <summary>Edycja SQL Server z ustawień serwera (np. Express, Developer), jeśli seria ją mierzyła.</summary>
        [JsonIgnore]
        public string? SqlServerEdition => Variants.Where(v => v.Engine == DatabaseEngine.SqlServer)
            .Select(v => v.ServerSetting("edycja")).FirstOrDefault(e => !string.IsNullOrEmpty(e));
    }

    /// <summary>Odczyt raportów zapisanych przez <see cref="BenchmarkReportWriter"/>.</summary>
    public static class BenchmarkReportReader
    {
        public const string FileName = "report.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        public static StoredReport Load(string path)
        {
            using var stream = File.OpenRead(path);
            var report = JsonSerializer.Deserialize<StoredReport>(stream, JsonOptions)
                ?? throw new InvalidDataException($"Plik {path} nie zawiera raportu.");
            report.Directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            return report;
        }

        /// <summary>
        /// Wszystkie raporty z podkatalogów (od najnowszego). Uszkodzony plik nie przerywa odczytu:
        /// trafia do onError i jest pomijany.
        /// </summary>
        public static IReadOnlyList<StoredReport> LoadAll(string root, Action<string, Exception>? onError = null)
        {
            if (!System.IO.Directory.Exists(root))
                return [];

            var reports = new List<StoredReport>();
            foreach (var directory in System.IO.Directory.EnumerateDirectories(root))
            {
                var path = Path.Combine(directory, FileName);
                if (!File.Exists(path))
                    continue;
                try
                {
                    reports.Add(Load(path));
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
                {
                    onError?.Invoke(path, ex);
                }
            }
            return reports.OrderByDescending(r => r.StartedAt).ToList();
        }
    }
}
