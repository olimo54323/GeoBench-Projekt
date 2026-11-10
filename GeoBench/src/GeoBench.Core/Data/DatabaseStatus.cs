using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace GeoBench.Core.Data
{
    /// <summary>
    /// Stan bazy widoczny w zakładce Połączenia: czy serwer odpowiada, czy baza istnieje, wersja serwera
    /// i migracje, których jeszcze nie nałożono. Przy błędzie połączenia z serwerem pole Error zawiera komunikat.
    /// </summary>
    /// <param name="CanConnect">Serwer odpowiada i login działa (sama baza może jeszcze nie istnieć).</param>
    /// <param name="DatabaseName">Nazwa bazy z connection stringa (bez reszty connection stringa).</param>
    public sealed record DatabaseStatus(
        DatabaseEngine Engine,
        bool CanConnect,
        bool DatabaseExists,
        string? DatabaseName,
        string? ServerVersion,
        IReadOnlyList<string> PendingMigrations,
        string? Error)
    {
        public bool IsReady => CanConnect && DatabaseExists && PendingMigrations.Count == 0;

        /// <summary>Serwer odpowiada, ale bazy nie ma albo brakuje migracji: można ją utworzyć lub zaktualizować.</summary>
        public bool CanMigrate => CanConnect && !IsReady;

        public static async Task<DatabaseStatus> CheckAsync(GeoBenchContextFactory factory, DatabaseEngine engine,
            CancellationToken cancellationToken = default)
        {
            if (!factory.IsConfigured(engine))
                return Failed(engine, null, $"Brak connection stringa '{GeoBenchContextFactory.ConnectionName(engine)}' w konfiguracji.");

            await using var context = factory.Create(engine);
            string? databaseName = null;
            try
            {
                databaseName = context.Database.GetDbConnection().Database;

                // Istnienie bazy sprawdza połączenie z bazą systemową (master / postgres), więc rozróżnia
                // „serwer nie odpowiada” od „serwer jest, bazy jeszcze nie ma” (nowy komputer)
                var creator = context.GetService<IRelationalDatabaseCreator>();
                if (!await creator.ExistsAsync(cancellationToken))
                    return new DatabaseStatus(engine, true, false, databaseName, null, context.Database.GetMigrations().ToList(), null);

                var version = await context.GetServerVersionAsync(cancellationToken);
                var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                return new DatabaseStatus(engine, true, true, databaseName, version, pending, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Failed(engine, databaseName, ex.GetBaseException().Message);
            }
        }

        private static DatabaseStatus Failed(DatabaseEngine engine, string? databaseName, string error) =>
            new(engine, false, false, databaseName, null, [], error);
    }
}
