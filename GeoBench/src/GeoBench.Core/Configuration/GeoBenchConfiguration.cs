using Microsoft.Extensions.Configuration;

namespace GeoBench.Core.Configuration
{
    /// <summary>
    /// Konfiguracja aplikacji: appsettings.json (poza gitem, obok GeoBench.slnx) wyszukiwany w górę
    /// drzewa katalogów, z możliwością nadpisania zmiennymi środowiskowymi (np. ConnectionStrings__SqlServer).
    /// Wspólna dla dotnet ef, testów i aplikacji WPF.
    /// </summary>
    public static class GeoBenchConfiguration
    {
        public const string FileName = "appsettings.json";
        public const string SqlServerConnectionName = "SqlServer";
        public const string PostgresConnectionName = "Postgres";

        public static IConfiguration Build()
        {
            var builder = new ConfigurationBuilder();
            var file = FindFile(AppContext.BaseDirectory) ?? FindFile(Directory.GetCurrentDirectory());
            if (file != null)
                builder.AddJsonFile(file, optional: false);
            return builder.AddEnvironmentVariables().Build();
        }

        public static string RequireConnectionString(IConfiguration configuration, string name) =>
            configuration.GetConnectionString(name)
            ?? throw new InvalidOperationException($"Brak connection stringa '{name}' w sekcji ConnectionStrings pliku {FileName}.");

        private static string? FindFile(string startDirectory)
        {
            for (var dir = new DirectoryInfo(startDirectory); dir != null; dir = dir.Parent)
            {
                var path = Path.Combine(dir.FullName, FileName);
                if (File.Exists(path))
                    return path;
            }
            return null;
        }
    }
}
