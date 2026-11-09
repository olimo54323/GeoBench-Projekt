using Microsoft.EntityFrameworkCore;

namespace GeoBench.Core.Data
{
    /// <summary>
    /// Stan bazy widoczny w zakładce Połączenia: czy jest połączenie, wersja serwera
    /// i migracje, których jeszcze nie nałożono. Przy błędzie połączenia pole Error zawiera komunikat.
    /// </summary>
    public sealed record DatabaseStatus(
        DatabaseEngine Engine,
        bool CanConnect,
        string? ServerVersion,
        IReadOnlyList<string> PendingMigrations,
        string? Error)
    {
        public bool IsReady => CanConnect && PendingMigrations.Count == 0;

        public static async Task<DatabaseStatus> CheckAsync(GeoBenchContextFactory factory, DatabaseEngine engine,
            CancellationToken cancellationToken = default)
        {
            if (!factory.IsConfigured(engine))
                return Failed(engine, $"Brak connection stringa '{GeoBenchContextFactory.ConnectionName(engine)}' w konfiguracji.");

            try
            {
                await using var context = factory.Create(engine);
                // CanConnect nie zgłasza wyjątku, więc przyczynę błędu daje dopiero pierwsze zapytanie
                var version = await context.GetServerVersionAsync(cancellationToken);
                var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                return new DatabaseStatus(engine, true, version, pending, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Failed(engine, ex.GetBaseException().Message);
            }
        }

        private static DatabaseStatus Failed(DatabaseEngine engine, string error) =>
            new(engine, false, null, [], error);
    }
}
