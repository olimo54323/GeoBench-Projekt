using System.Diagnostics;
using GeoBench.Core.Benchmark;
using GeoBench.Core.Storage;

namespace GeoBench.Core.Processing
{
    /// <summary>
    /// Przetwarzanie w C# (W3, W4): baza jest tylko magazynem. Fazy mierzone osobno: odczyt geometrii z bazy,
    /// budowa indeksu (STRtree + PreparedGeometry) i obliczenie, jednowątkowo albo przez Parallel.For (W3‖, W4‖).
    /// </summary>
    public sealed class NtsStrategy : IProcessingStrategy
    {
        public const string ReadPhase = "odczyt";
        public const string IndexPhase = "indeks";
        public const string ComputePhase = "obliczenie";

        public NtsStrategy(bool parallel)
        {
            Parallel = parallel;
        }

        public bool Parallel { get; }

        public string Name => Parallel ? "NTS wielowątkowo" : "NTS jednowątkowo";

        public bool InDatabase => false;

        public async Task<StrategyRun> RunAsync(IBenchmarkScenario scenario, ISpatialStore store, CancellationToken cancellationToken = default)
        {
            var run = scenario.CreateNtsRun();

            var stopwatch = Stopwatch.StartNew();
            await run.LoadAsync(store, cancellationToken);
            var read = stopwatch.Elapsed;

            stopwatch.Restart();
            run.BuildIndex();
            var index = stopwatch.Elapsed;

            stopwatch.Restart();
            var pairs = run.Compute(Parallel, cancellationToken);
            var compute = stopwatch.Elapsed;

            return new StrategyRun(pairs,
            [
                new PhaseTime(ReadPhase, read),
                new PhaseTime(IndexPhase, index),
                new PhaseTime(ComputePhase, compute)
            ]);
        }
    }
}
