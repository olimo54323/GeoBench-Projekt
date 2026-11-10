using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoBench.Core.Data;
using GeoBench.Wpf.Services;

namespace GeoBench.Wpf.ViewModels
{
    /// <summary>Wiersz zakładki Połączenia: stan jednej bazy.</summary>
    public sealed partial class DatabaseItemViewModel : ObservableObject
    {
        public DatabaseItemViewModel(DatabaseEngine engine, string name)
        {
            Engine = engine;
            Name = name;
        }

        public DatabaseEngine Engine { get; }

        public string Name { get; }

        [ObservableProperty]
        private string _state = "nie sprawdzono";

        [ObservableProperty]
        private string? _serverVersion;

        [ObservableProperty]
        private string? _migrations;

        [ObservableProperty]
        private string? _error;

        [ObservableProperty]
        private bool _isReady;

        /// <summary>Serwer odpowiada, ale bazy nie ma albo brakuje migracji (przycisk w wierszu).</summary>
        [ObservableProperty]
        private bool _canMigrate;

        [ObservableProperty]
        private string _migrateLabel = "Nałóż migracje";

        /// <summary>Ostatnio sprawdzony stan (do potwierdzenia przed migracją).</summary>
        public DatabaseStatus? Status { get; private set; }

        public void Apply(DatabaseStatus status)
        {
            Status = status;
            IsReady = status.IsReady;
            CanMigrate = status.CanMigrate;
            ServerVersion = status.ServerVersion;
            Error = status.Error;
            if (!status.CanConnect)
            {
                State = "brak połączenia z serwerem";
                Migrations = null;
            }
            else if (!status.DatabaseExists)
            {
                State = $"brak bazy {status.DatabaseName}";
                Migrations = $"do nałożenia po utworzeniu bazy: {string.Join(", ", status.PendingMigrations)}";
                MigrateLabel = "Utwórz bazę i nałóż migracje";
            }
            else if (status.PendingMigrations.Count > 0)
            {
                State = "wymaga migracji";
                Migrations = $"do nałożenia: {string.Join(", ", status.PendingMigrations)}";
                MigrateLabel = "Nałóż migracje";
            }
            else
            {
                State = "gotowa";
                Migrations = "aktualne";
            }
        }
    }

    /// <summary>Zakładka Połączenia: stan obu baz, test połączenia i przygotowanie bazy z migracji.</summary>
    public sealed partial class ConnectionsViewModel : ObservableObject
    {
        private readonly GeoBenchContextFactory _factory;
        private readonly IOperationHost _host;
        private readonly IDialogService _dialogs;

        public ConnectionsViewModel(GeoBenchContextFactory factory, IOperationHost host, IDialogService dialogs)
        {
            _factory = factory;
            _host = host;
            _dialogs = dialogs;
            Databases =
            [
                new DatabaseItemViewModel(DatabaseEngine.SqlServer, "SQL Server"),
                new DatabaseItemViewModel(DatabaseEngine.Postgres, "PostgreSQL + PostGIS")
            ];
        }

        public IReadOnlyList<DatabaseItemViewModel> Databases { get; }

        public DatabaseItemViewModel Find(DatabaseEngine engine) => Databases.Single(d => d.Engine == engine);

        private bool CanTest() => !_host.IsBusy;

        [RelayCommand(CanExecute = nameof(CanTest))]
        private Task TestAsync() =>
            _host.RunAsync("Test połączeń", async cancellationToken =>
            {
                foreach (var database in Databases)
                    await CheckAsync(database, cancellationToken);
            });

        private bool CanMigrate(DatabaseItemViewModel? database) => !_host.IsBusy && database is { CanMigrate: true };

        /// <summary>Utworzenie bazy (gdy jej nie ma) i nałożenie migracji, po potwierdzeniu; potem ponowne sprawdzenie stanu.</summary>
        [RelayCommand(CanExecute = nameof(CanMigrate))]
        private async Task MigrateAsync(DatabaseItemViewModel? database)
        {
            if (database?.Status is not { CanMigrate: true } status)
                return;

            var action = status.DatabaseExists
                ? $"Nałożyć migracje w bazie „{status.DatabaseName}”"
                : $"Utworzyć bazę „{status.DatabaseName}” i nałożyć migracje";
            var requirements = database.Engine == DatabaseEngine.Postgres
                ? "Wymagane: prawo CREATEDB (nowa baza) i możliwość CREATE EXTENSION postgis (zwykle superuser)."
                : "Wymagane: rola serwera dbcreator (nowa baza) albo prawa właściciela bazy (db_owner).";
            var message = $"{database.Name}: {action}?\n\nMigracje: {string.Join(", ", status.PendingMigrations)}\n\n{requirements}";
            if (!_dialogs.Confirm(message, "Przygotowanie bazy"))
                return;

            var succeeded = await _host.RunAsync($"Migracje: {database.Name}", async cancellationToken =>
            {
                _host.SetStatus($"{database.Name}: {(status.DatabaseExists ? "nakładanie migracji" : "tworzenie bazy i nakładanie migracji")}…");
                database.State = "nakładanie migracji…";
                var applied = await DatabaseSetup.MigrateAsync(_factory, database.Engine, cancellationToken);
                _host.AddLog(applied.Count == 0
                    ? $"{database.Name}: nie było migracji do nałożenia."
                    : $"{database.Name}: nałożono migracje {string.Join(", ", applied)}.");
                await CheckAsync(database, cancellationToken);
            });

            // Po błędzie migracji (np. baza utworzona, ale bez PostGIS) stan w wierszu też ma być aktualny
            if (!succeeded)
                await _host.RunAsync("Test połączenia", cancellationToken => CheckAsync(database, cancellationToken));
        }

        private async Task CheckAsync(DatabaseItemViewModel database, CancellationToken cancellationToken)
        {
            _host.SetStatus($"Test połączenia: {database.Name}");
            database.State = "sprawdzanie…";
            var status = await DatabaseStatus.CheckAsync(_factory, database.Engine, cancellationToken);
            database.Apply(status);
            MigrateCommand.NotifyCanExecuteChanged();
            _host.AddLog(status.CanConnect
                    ? $"{database.Name}: {database.State}{(status.ServerVersion == null ? "" : $" ({status.ServerVersion})")}"
                    : $"{database.Name}: {status.Error}",
                isError: !status.IsReady);
        }
    }
}
