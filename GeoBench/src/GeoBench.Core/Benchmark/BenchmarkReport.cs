using System.Text.Json.Serialization;
using GeoBench.Core.Benchmark.Measurement;
using GeoBench.Core.Data;
using GeoBench.Core.Storage;
using GeoBench.Core.Validation;

namespace GeoBench.Core.Benchmark
{
    /// <summary>Ustawienia serii pomiarów.</summary>
    public sealed record BenchmarkOptions
    {
        /// <summary>Przebiegi rozgrzewkowe (pamięć podręczna bazy, JIT), niezapisywane w statystykach.</summary>
        public int Warmups { get; init; } = 1;

        /// <summary>Pomiary do statystyk (co najmniej 5 dla sensownej mediany).</summary>
        public int Repetitions { get; init; } = 5;

        /// <summary>Przebieg diagnostyczny z planem zapytania (poza pomiarem) dla wariantów w bazie.</summary>
        public bool Diagnostics { get; init; } = true;

        /// <summary>Zapis par do tabeli wyników scenariusza (poza pomiarem, po walidacji).</summary>
        public bool SaveResults { get; init; } = true;

        /// <summary>Dopuszcza różne liczności tabel w obu bazach (domyślnie pomiar jest przerywany).</summary>
        public bool AllowCountMismatch { get; init; }

        /// <summary>CSV z wynikiem wzorcowym z QGIS; bez niego wzorcem jest pierwszy udany wariant.</summary>
        public string? ReferencePath { get; init; }

        /// <summary>
        /// Niepoprawne geometrie naprawiane w ramach mierzonej pracy (MakeValid, ST_MakeValid, GeometryFixer);
        /// bez naprawy są odrzucane i wymieniane w raporcie. Dotyczy scenariuszy nakładania (05a–05c).
        /// </summary>
        public bool RepairInvalidGeometries { get; init; }
    }

    /// <summary>Postęp serii: opis kroku i jego numer.</summary>
    public readonly record struct BenchmarkProgress(string Message, int Step, int TotalSteps);

    /// <summary>
    /// Liczność warstwy w bazie z kontroli wstępnej; dla scenariuszy nakładania także niepoprawne geometrie
    /// według walidatora silnika (STIsValid, ST_IsValid).
    /// </summary>
    public sealed record LayerCount(DatabaseEngine Engine, string Layer, long Count, string? Table = null,
        int InvalidCount = 0, IReadOnlyList<string>? InvalidIds = null);

    /// <summary>Wynik jednego wariantu: czasy, odcisk, zgodność i diagnostyka.</summary>
    public sealed class VariantReport
    {
        public VariantReport(BenchmarkVariant variant)
        {
            Code = variant.Code;
            Label = variant.Label;
            Description = variant.Description;
            Engine = variant.Engine;
            Strategy = variant.Strategy.Name;
        }

        public string Code { get; }
        public string Label { get; }
        public string Description { get; }
        public DatabaseEngine Engine { get; }
        public string Strategy { get; }

        /// <summary>Ustawienia serwera w chwili pomiaru (wersja, pamięć, równoległość, indeksy).</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Server { get; set; } = [];

        /// <summary>Czasy kolejnych pomiarów w ms (bez rozgrzewki).</summary>
        public List<double> RunsMs { get; } = [];

        public MeasurementStatistics? Total { get; set; }

        /// <summary>Statystyki faz (zapytanie albo odczyt / indeks / obliczenie).</summary>
        public Dictionary<string, MeasurementStatistics> Phases { get; } = [];

        public ResultFingerprint? Fingerprint { get; set; }

        /// <summary>Czy każdy pomiar dał ten sam odcisk wyniku.</summary>
        public bool ConsistentAcrossRuns { get; set; } = true;

        /// <summary>Porównanie ze wzorcem (CSV z QGIS albo pierwszy udany wariant).</summary>
        public ResultComparison? Comparison { get; set; }

        public string? ComparedWith { get; set; }

        /// <summary>Opis wyniku scenariusza i uwagi (np. nieudany zapis tabeli wyników).</summary>
        public List<string> Notes { get; } = [];

        public bool Saved { get; set; }

        public string? Error { get; set; }

        /// <summary>Nazwa pliku z planem zapytania w katalogu raportu.</summary>
        public string? PlanFile { get; set; }

        [JsonIgnore]
        public QueryDiagnostics? Diagnostics { get; set; }

        [JsonIgnore]
        public IReadOnlyList<ResultPair> Pairs { get; set; } = [];

        [JsonIgnore]
        public bool Succeeded => Error == null && Total != null;
    }

    /// <summary>Raport serii pomiarów jednego scenariusza (zapisywany jako JSON i CSV).</summary>
    public sealed class BenchmarkReport
    {
        public required string ScenarioId { get; init; }
        public required string ScenarioName { get; init; }
        public required BenchmarkOptions Options { get; init; }
        public DateTime StartedAt { get; init; } = DateTime.Now;
        public DateTime? FinishedAt { get; set; }

        /// <summary>Środowisko aplikacji: system, .NET, liczba procesorów logicznych.</summary>
        public Dictionary<string, string> Environment { get; } = [];

        public List<LayerCount> Counts { get; } = [];

        public int? ReferencePairs { get; set; }

        public List<VariantReport> Variants { get; } = [];
    }
}
