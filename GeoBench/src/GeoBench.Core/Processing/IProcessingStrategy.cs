using GeoBench.Core.Benchmark;
using GeoBench.Core.Storage;

namespace GeoBench.Core.Processing
{
    /// <summary>Czas jednej fazy pomiaru (np. zapytanie, odczyt, indeks, obliczenie).</summary>
    public readonly record struct PhaseTime(string Phase, TimeSpan Elapsed);

    /// <summary>Wynik jednego przebiegu strategii: pary i czasy faz (suma faz to czas przebiegu).</summary>
    public sealed record StrategyRun(IReadOnlyList<ResultPair> Pairs, IReadOnlyList<PhaseTime> Phases)
    {
        public TimeSpan Total => TimeSpan.FromTicks(Phases.Sum(p => p.Elapsed.Ticks));
    }

    /// <summary>Gdzie wykonywane jest przetwarzanie przestrzenne: w bazie (SQL) albo w C# (NTS).</summary>
    public interface IProcessingStrategy
    {
        string Name { get; }

        /// <summary>Czy strategia wykonuje SQL w bazie (wtedy jest przebieg diagnostyczny z planem).</summary>
        bool InDatabase { get; }

        Task<StrategyRun> RunAsync(IBenchmarkScenario scenario, ISpatialStore store, CancellationToken cancellationToken = default);
    }
}
