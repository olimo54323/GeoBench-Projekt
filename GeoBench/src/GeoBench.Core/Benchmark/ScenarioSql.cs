using GeoBench.Core.Data;

namespace GeoBench.Core.Benchmark
{
    /// <summary>
    /// Zapytania scenariuszy z zasobów osadzonych (katalog sql/&lt;silnik&gt;/scenarios obok GeoBench.slnx).
    /// Średnik na końcu jest usuwany, bo zapytanie bywa opakowywane (EXPLAIN, SET STATISTICS).
    /// </summary>
    public static class ScenarioSql
    {
        public static string EngineDirectory(DatabaseEngine engine) => engine switch
        {
            DatabaseEngine.SqlServer => "sqlserver",
            DatabaseEngine.Postgres => "postgres",
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
        };

        public static string Load(DatabaseEngine engine, string fileName)
        {
            var assembly = typeof(ScenarioSql).Assembly;
            // Nazwa zasobu zależy od ścieżki (Link), więc wystarcza końcówka silnik.scenarios.plik
            var suffix = $".{EngineDirectory(engine)}.scenarios.{fileName}";
            var resource = assembly.GetManifestResourceNames()
                .SingleOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Brak zapytania sql/{EngineDirectory(engine)}/scenarios/{fileName} w zasobach biblioteki.");

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().TrimEnd().TrimEnd(';').TrimEnd();
        }
    }
}
