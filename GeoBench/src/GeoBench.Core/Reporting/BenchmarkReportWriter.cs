using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoBench.Core.Benchmark;
using GeoBench.Core.Configuration;

namespace GeoBench.Core.Reporting
{
    /// <summary>
    /// Zapis raportu serii do katalogu results/&lt;data_godzina&gt; obok GeoBench.slnx (poza gitem):
    /// report.json (pełny raport), summary.csv (tabela do arkusza, separator średnik, liczby z kropką)
    /// i plany zapytań wariantów w bazie (plan_W1.xml, plan_W2.json) z komunikatami serwera.
    /// </summary>
    public static class BenchmarkReportWriter
    {
        public const string SolutionFileName = "GeoBench.slnx";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            // Polskie znaki czytelne w pliku zamiast sekwencji \uXXXX
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() },
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        /// <summary>Katalog results obok GeoBench.slnx (albo obok programu, gdy rozwiązania nie ma).</summary>
        public static string ResultsRoot()
        {
            var solution = GeoBenchConfiguration.FindFile(SolutionFileName);
            var root = solution != null ? Path.GetDirectoryName(solution)! : AppContext.BaseDirectory;
            return Path.Combine(root, "results");
        }

        /// <summary>Katalog wyników dla serii rozpoczętej w podanej chwili.</summary>
        public static string DefaultDirectory(DateTime startedAt) =>
            Path.Combine(ResultsRoot(), startedAt.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));

        /// <summary>Zapisuje raport i zwraca katalog z plikami.</summary>
        public static async Task<string> WriteAsync(BenchmarkReport report, string directory, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(directory);

            foreach (var variant in report.Variants)
            {
                if (variant.Diagnostics is not { } diagnostics)
                    continue;
                variant.PlanFile = $"plan_{variant.Code}.{diagnostics.PlanFormat}";
                await File.WriteAllTextAsync(Path.Combine(directory, variant.PlanFile), diagnostics.Plan, Encoding.UTF8, cancellationToken);
                if (!string.IsNullOrWhiteSpace(diagnostics.Messages))
                    await File.WriteAllTextAsync(Path.Combine(directory, $"messages_{variant.Code}.txt"), diagnostics.Messages, Encoding.UTF8, cancellationToken);
            }

            // Niepoprawne geometrie z kontroli wstępnej: pełna lista identyfikatorów na silnik i tabelę
            foreach (var count in report.Counts.Where(c => c.InvalidIds is { Count: > 0 }))
            {
                var engine = count.Engine == Data.DatabaseEngine.SqlServer ? "sqlserver" : "postgres";
                var file = Path.Combine(directory, $"niepoprawne_{engine}_{count.Table ?? "warstwa"}.csv");
                await File.WriteAllLinesAsync(file, count.InvalidIds!.Prepend("id"), new UTF8Encoding(true), cancellationToken);
            }

            await using (var json = File.Create(Path.Combine(directory, "report.json")))
                await JsonSerializer.SerializeAsync(json, report, JsonOptions, cancellationToken);

            await File.WriteAllTextAsync(Path.Combine(directory, "summary.csv"), SummaryCsv(report), new UTF8Encoding(true), cancellationToken);
            return directory;
        }

        /// <summary>Jeden wiersz na wariant: czasy w ms, fazy (mediany), liczba par, skrót i zgodność.</summary>
        public static string SummaryCsv(BenchmarkReport report)
        {
            var phases = report.Variants.SelectMany(v => v.Phases.Keys).Distinct().ToList();
            var csv = new StringBuilder();
            csv.AppendLine(string.Join(';', new[] { "scenariusz", "wariant", "opis", "mediana_ms", "p95_ms", "min_ms", "max_ms", "odch_std_ms", "pomiary" }
                .Concat(phases.Select(p => $"{p}_mediana_ms"))
                .Concat(new[] { "pary", "sha256", "zgodnosc", "porownano_z", "zapisano", "blad" })));

            foreach (var v in report.Variants)
            {
                var fields = new List<string>
                {
                    report.ScenarioId, v.Code, v.Description,
                    Number(v.Total?.MedianMs), Number(v.Total?.P95Ms), Number(v.Total?.MinMs), Number(v.Total?.MaxMs),
                    Number(v.Total?.StdDevMs), v.Total?.Count.ToString(CultureInfo.InvariantCulture) ?? ""
                };
                fields.AddRange(phases.Select(p => Number(v.Phases.GetValueOrDefault(p)?.MedianMs)));
                fields.Add(v.Fingerprint?.Count.ToString(CultureInfo.InvariantCulture) ?? "");
                fields.Add(v.Fingerprint?.Sha256 ?? "");
                fields.Add(v.Comparison?.ToString() ?? (v.Succeeded ? "wzorzec" : ""));
                fields.Add(v.ComparedWith ?? "");
                fields.Add(v.Saved ? "tak" : "nie");
                fields.Add(v.Error ?? "");
                csv.AppendLine(string.Join(';', fields.Select(Escape)));
            }
            return csv.ToString();
        }

        private static string Number(double? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";

        private static string Escape(string field) =>
            field.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
    }
}
