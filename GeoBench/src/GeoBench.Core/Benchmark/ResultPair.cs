namespace GeoBench.Core.Benchmark
{
    /// <summary>
    /// Jedna para wyniku scenariusza: obiekt A powiązany z obiektem B po identyfikatorach biznesowych
    /// (np. id_budynku i oznaczenie strefy). Measure to opcjonalna miara pary, np. pole przecięcia w m².
    /// </summary>
    public readonly record struct ResultPair(string IdA, string IdB, double? Measure = null);
}
