using GeoBench.Core.Import;
using GeoBench.Core.Import.Gml;

namespace GeoBench.Tests
{
    /// <summary>
    /// Dostęp do plików z katalogu Fixtures (kopiowanych do katalogu wyjściowego testów).
    /// </summary>
    internal static class FixtureFiles
    {
        public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

        public static List<FeatureRecord> Read(string name)
        {
            using var stream = File.OpenRead(PathOf(name));
            return new GmlFeatureReader().Read(stream).ToList();
        }
    }
}
