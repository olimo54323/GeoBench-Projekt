using System.Diagnostics;
using GeoBench.Core.Benchmark;
using GeoBench.Core.Storage;

namespace GeoBench.Core.Processing
{
    /// <summary>
    /// Przetwarzanie w bazie (W1, W2): zapytanie scenariusza w dialekcie silnika. Mierzony jest czas
    /// wykonania razem z odczytem wszystkich par do aplikacji, bo strategia NTS też kończy na liście par.
    /// </summary>
    public sealed class InDatabaseStrategy : IProcessingStrategy
    {
        public const string QueryPhase = "zapytanie";

        public string Name => "w bazie";

        public bool InDatabase => true;

        public async Task<StrategyRun> RunAsync(IBenchmarkScenario scenario, ISpatialStore store, CancellationToken cancellationToken = default)
        {
            // Wczytanie pliku SQL nie należy do pomiaru
            var sql = ScenarioSql.Load(store.Engine, scenario.SqlFileName);

            var stopwatch = Stopwatch.StartNew();
            var pairs = await scenario.ExecuteInDatabaseAsync(store, sql, cancellationToken);
            stopwatch.Stop();

            return new StrategyRun(pairs, [new PhaseTime(QueryPhase, stopwatch.Elapsed)]);
        }
    }
}
