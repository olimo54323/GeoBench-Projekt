using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GeoBench.Core.Data
{
    /// <summary>
    /// Przygotowanie bazy na nowym komputerze: utworzenie bazy (gdy jej nie ma) i nałożenie migracji EF.
    /// Indeksów przestrzennych migracje nie tworzą; powstają przy imporcie danych.
    /// </summary>
    public static class DatabaseSetup
    {
        private static readonly TimeSpan MigrationTimeout = TimeSpan.FromMinutes(10);

        /// <summary>Tworzy bazę, jeśli jej nie ma, i nakłada brakujące migracje. Zwraca nazwy nałożonych migracji.</summary>
        public static async Task<IReadOnlyList<string>> MigrateAsync(GeoBenchContextFactory factory, DatabaseEngine engine,
            CancellationToken cancellationToken = default)
        {
            await using var context = factory.Create(engine);
            context.Database.SetCommandTimeout(MigrationTimeout);
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            try
            {
                await context.Database.MigrateAsync(cancellationToken);
            }
            catch (Exception ex) when (Hint(ex.GetBaseException()) is { } hint)
            {
                // Bez wyjątku wewnętrznego: okno błędu pokazuje komunikat najgłębszego wyjątku, a ma pokazać podpowiedź
                throw new InvalidOperationException($"{hint} Komunikat serwera: {ex.GetBaseException().Message}");
            }
            return pending;
        }

        /// <summary>Wyjaśnienie typowych błędów uprawnień i braku PostGIS; null dla pozostałych błędów.</summary>
        private static string? Hint(Exception error) => error switch
        {
            // 42501 insufficient_privilege: CREATE DATABASE bez CREATEDB albo CREATE EXTENSION bez superusera
            PostgresException { SqlState: "42501" } =>
                "Brak uprawnień w PostgreSQL. Utworzenie bazy wymaga prawa CREATEDB, a CREATE EXTENSION postgis zwykle roli superuser " +
                "(albo właściciela bazy z wcześniej zainstalowanym rozszerzeniem).",
            // 58P01 undefined_file / 0A000: brak plików rozszerzenia na serwerze
            PostgresException { SqlState: "58P01" or "0A000" } =>
                "Na serwerze PostgreSQL nie ma zainstalowanego PostGIS (rozszerzenie postgis). Zainstaluj PostGIS dla tej wersji serwera.",
            // 262: brak uprawnienia CREATE DATABASE; 1802: CREATE DATABASE nie powiodło się
            SqlException { Number: 262 or 1802 } =>
                "Brak uprawnień do utworzenia bazy w SQL Server. Login potrzebuje roli serwera dbcreator (albo bazę musi założyć administrator).",
            _ => null
        };
    }
}
