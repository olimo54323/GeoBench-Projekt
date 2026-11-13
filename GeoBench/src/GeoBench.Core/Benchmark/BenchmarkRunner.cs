using System.Runtime.InteropServices;
using GeoBench.Core.Benchmark.Measurement;
using GeoBench.Core.Data;
using GeoBench.Core.Storage;
using GeoBench.Core.Validation;

namespace GeoBench.Core.Benchmark
{
    /// <summary>
    /// Seria pomiarów scenariusza dla wybranych wariantów:
    /// kontrola wstępna (równe liczności tabel w bazach) → dla każdego wariantu rozgrzewka, N pomiarów
    /// i przebieg diagnostyczny poza pomiarem → walidacja (odcisk, porównanie ze wzorcem) → zapis tabel wyników.
    /// Błąd jednego wariantu jest zapisywany w raporcie i nie przerywa pozostałych.
    /// </summary>
    public sealed class BenchmarkRunner
    {
        private readonly GeoBenchContextFactory _factory;

        public BenchmarkRunner(GeoBenchContextFactory factory)
        {
            _factory = factory;
        }

        public async Task<BenchmarkReport> RunAsync(IBenchmarkScenario scenario, IReadOnlyList<BenchmarkVariant> variants,
            BenchmarkOptions options, IProgress<BenchmarkProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (variants.Count == 0)
                throw new ArgumentException("Nie wybrano żadnego wariantu.", nameof(variants));
            if (options.Repetitions < 1 || options.Warmups < 0)
                throw new ArgumentException("Liczba pomiarów musi być dodatnia, a rozgrzewek nieujemna.", nameof(options));

            var report = new BenchmarkReport { ScenarioId = scenario.Id, ScenarioName = scenario.Name, Options = options };
            report.Environment["system"] = RuntimeInformation.OSDescription;
            report.Environment["dotnet"] = RuntimeInformation.FrameworkDescription;
            report.Environment["procesory_logiczne"] = System.Environment.ProcessorCount.ToString();
            report.Environment["gc_serwerowy"] = System.Runtime.GCSettings.IsServerGC ? "tak" : "nie";

            var step = 0;
            var totalSteps = variants.Sum(v => options.Warmups + options.Repetitions + (options.Diagnostics && v.Strategy.InDatabase ? 1 : 0))
                + (options.SaveResults && scenario.HasResultTable ? variants.Count : 0) + 1;
            void Report(string message) => progress?.Report(new BenchmarkProgress(message, ++step, totalSteps));

            scenario.Configure(options);

            Report("Kontrola wstępna danych");
            await CheckCountsAsync(scenario, variants, options, report, cancellationToken);

            // Przygotowanie wspólne dla wszystkich wariantów (np. okna), poza pomiarem
            await using (var context = _factory.Create(variants[0].Engine))
                await scenario.PrepareAsync(SpatialStore.Create(context), cancellationToken);

            IReadOnlyList<ResultPair>? reference = null;
            if (options.ReferencePath != null)
            {
                reference = ReferenceResult.Load(options.ReferencePath);
                report.ReferencePairs = reference.Count;
            }

            foreach (var variant in variants)
            {
                var result = new VariantReport(variant);
                report.Variants.Add(result);
                try
                {
                    await RunVariantAsync(scenario, variant, options, report, result, Report, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    result.Error = ex.GetBaseException().Message;
                }
            }

            Validate(report, reference);

            if (options.SaveResults && scenario.HasResultTable)
            {
                foreach (var (variant, result) in variants.Zip(report.Variants))
                {
                    Report($"{variant.Label}: zapis tabeli wyników");
                    if (!result.Succeeded || !result.ConsistentAcrossRuns)
                        continue;
                    try
                    {
                        await using var context = _factory.Create(variant.Engine);
                        await scenario.SaveResultsAsync(context, variant.Code, result.Pairs, cancellationToken);
                        result.Saved = true;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Np. brak migracji z tabelą wyników: czasy i walidacja pozostają ważne
                        result.Notes.Add($"zapis tabeli wyników nie powiódł się: {ex.GetBaseException().Message}");
                    }
                }
            }

            report.FinishedAt = DateTime.Now;
            return report;
        }

        private async Task CheckCountsAsync(IBenchmarkScenario scenario, IReadOnlyList<BenchmarkVariant> variants,
            BenchmarkOptions options, BenchmarkReport report, CancellationToken cancellationToken)
        {
            var engines = variants.Select(v => v.Engine).Distinct().ToList();
            foreach (var engine in engines)
            {
                await using var context = _factory.Create(engine);
                var store = SpatialStore.Create(context);
                foreach (var layer in new[] { scenario.LayerA, scenario.LayerB }.Distinct())
                {
                    var count = await store.CountAsync(layer, cancellationToken);
                    if (count == 0)
                        throw new InvalidOperationException($"Brak danych w tabeli {layer.Table} ({EngineName(engine)}): najpierw zaimportuj {layer.Name}.");
                    // Niepoprawne geometrie według walidatora silnika: liczba i identyfikatory do raportu
                    var invalid = scenario.HandlesInvalidGeometries
                        ? await store.InvalidIdsAsync(layer, cancellationToken)
                        : [];
                    report.Counts.Add(new LayerCount(engine, layer.Name, count, layer.Table, invalid.Count, invalid));
                }
            }

            if (engines.Count < 2 || options.AllowCountMismatch)
                return;
            var mismatches = report.Counts.GroupBy(c => c.Layer)
                .Where(g => g.Select(c => c.Count).Distinct().Count() > 1)
                .Select(g => $"{g.Key}: " + string.Join(", ", g.Select(c => $"{EngineName(c.Engine)} {c.Count}")))
                .ToList();
            if (mismatches.Count > 0)
                throw new InvalidOperationException(
                    "Bazy mają różne dane, więc wyniki nie byłyby porównywalne. Zaimportuj te same dane do obu baz. " +
                    string.Join("; ", mismatches));
        }

        private async Task RunVariantAsync(IBenchmarkScenario scenario, BenchmarkVariant variant, BenchmarkOptions options,
            BenchmarkReport report, VariantReport result, Action<string> progress, CancellationToken cancellationToken)
        {
            await using var context = _factory.Create(variant.Engine);
            var store = SpatialStore.Create(context);
            result.Server = await store.GetServerInfoAsync(cancellationToken);

            for (var i = 1; i <= options.Warmups; i++)
            {
                progress($"{variant.Label}: rozgrzewka {i}/{options.Warmups}");
                await variant.Strategy.RunAsync(scenario, store, cancellationToken);
            }

            var phases = new Dictionary<string, List<TimeSpan>>();
            var totals = new List<TimeSpan>();
            for (var i = 1; i <= options.Repetitions; i++)
            {
                progress($"{variant.Label}: pomiar {i}/{options.Repetitions}");
                // Sprzątanie pamięci poprzedniego przebiegu poza pomiarem, żeby GC nie wpadał w kolejny
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                var run = await variant.Strategy.RunAsync(scenario, store, cancellationToken);
                totals.Add(run.Total);
                result.RunsMs.Add(run.Total.TotalMilliseconds);
                foreach (var phase in run.Phases)
                {
                    if (!phases.TryGetValue(phase.Phase, out var list))
                        phases[phase.Phase] = list = [];
                    list.Add(phase.Elapsed);
                }

                var fingerprint = ResultFingerprint.Compute(run.Pairs);
                if (result.Fingerprint == null)
                {
                    result.Fingerprint = fingerprint;
                    result.Pairs = run.Pairs;
                }
                else if (fingerprint != result.Fingerprint)
                    result.ConsistentAcrossRuns = false;
            }

            result.Total = MeasurementStatistics.From(totals);
            foreach (var (phase, samples) in phases)
                result.Phases[phase] = MeasurementStatistics.From(samples);
            if (!result.ConsistentAcrossRuns)
                result.Notes.Add("kolejne pomiary dały różne wyniki – wynik nie jest zapisywany");

            if (options.Diagnostics && variant.Strategy.InDatabase)
            {
                progress($"{variant.Label}: przebieg diagnostyczny (plan zapytania)");
                result.Diagnostics = await store.DiagnoseAsync(scenario.DiagnosticSql(variant.Engine), cancellationToken);
            }

            var countA = report.Counts.FirstOrDefault(c => c.Engine == variant.Engine && c.Layer == scenario.LayerA.Name)?.Count ?? 0;
            result.Notes.InsertRange(0, scenario.Describe(result.Pairs, countA));

            if (scenario.HandlesInvalidGeometries)
            {
                var invalid = report.Counts.Where(c => c.Engine == variant.Engine && c.InvalidCount > 0)
                    .Select(c => $"{c.Layer} {c.InvalidCount}").ToList();
                var action = options.RepairInvalidGeometries ? "naprawiane" : "odrzucane";
                result.Notes.Add(invalid.Count == 0
                    ? $"niepoprawne geometrie w bazie {EngineName(variant.Engine)}: brak"
                    : $"niepoprawne geometrie w bazie {EngineName(variant.Engine)} ({action}): {string.Join(", ", invalid)}; identyfikatory w plikach niepoprawne_*.csv" +
                      (variant.Strategy.InDatabase ? "" : " (NTS sprawdza poprawność własnym algorytmem)"));
            }
        }

        /// <summary>Porównanie każdego udanego wariantu ze wzorcem: CSV z QGIS albo pierwszym udanym wariantem.</summary>
        private static void Validate(BenchmarkReport report, IReadOnlyList<ResultPair>? reference)
        {
            var succeeded = report.Variants.Where(v => v.Succeeded).ToList();
            if (succeeded.Count == 0)
                return;

            var baseline = reference;
            var baselineName = "wzorzec CSV";
            if (baseline == null)
            {
                baseline = succeeded[0].Pairs;
                baselineName = succeeded[0].Label;
            }

            foreach (var variant in succeeded)
            {
                if (reference == null && ReferenceEquals(variant, succeeded[0]))
                    continue;
                variant.Comparison = ResultComparison.Compare(baseline, variant.Pairs);
                variant.ComparedWith = baselineName;
            }
        }

        private static string EngineName(DatabaseEngine engine) => engine == DatabaseEngine.SqlServer ? "SQL Server" : "PostgreSQL";
    }
}
