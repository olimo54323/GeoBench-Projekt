using GeoBench.Core.Data;
using GeoBench.Core.Processing;

namespace GeoBench.Core.Benchmark
{
    /// <summary>
    /// Wariant macierzy pomiarów: silnik bazy (magazyn) i miejsce przetwarzania.
    /// Code jest zapisywany w kolumnie wariant tabel wyników (ASCII), Label służy do wyświetlania.
    /// </summary>
    public sealed record BenchmarkVariant(string Code, string Label, string Description, DatabaseEngine Engine, IProcessingStrategy Strategy);

    public static class VariantFactory
    {
        public static IReadOnlyList<BenchmarkVariant> All { get; } =
        [
            new("W1", "W1", "SQL Server, przetwarzanie w bazie", DatabaseEngine.SqlServer, new InDatabaseStrategy()),
            new("W2", "W2", "PostgreSQL + PostGIS, przetwarzanie w bazie", DatabaseEngine.Postgres, new InDatabaseStrategy()),
            new("W3", "W3", "SQL Server jako magazyn, NTS jednowątkowo", DatabaseEngine.SqlServer, new NtsStrategy(parallel: false)),
            new("W3P", "W3P", "SQL Server jako magazyn, NTS wielowątkowo", DatabaseEngine.SqlServer, new NtsStrategy(parallel: true)),
            new("W4", "W4", "PostgreSQL jako magazyn, NTS jednowątkowo", DatabaseEngine.Postgres, new NtsStrategy(parallel: false)),
            new("W4P", "W4P", "PostgreSQL jako magazyn, NTS wielowątkowo", DatabaseEngine.Postgres, new NtsStrategy(parallel: true))
        ];

        public static BenchmarkVariant Get(string code) =>
            All.FirstOrDefault(v => string.Equals(v.Code, code, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Nieznany wariant {code}.", nameof(code));
    }
}
