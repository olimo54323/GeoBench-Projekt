using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoBench.Core.Data;

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

        public void Apply(DatabaseStatus status)
        {
            IsReady = status.IsReady;
            ServerVersion = status.ServerVersion;
            Error = status.Error;
            if (!status.CanConnect)
            {
                State = "brak połączenia";
                Migrations = null;
            }
            else if (status.PendingMigrations.Count > 0)
            {
                State = "wymaga migracji";
                Migrations = $"do nałożenia: {string.Join(", ", status.PendingMigrations)}";
            }
            else
            {
                State = "gotowa";
                Migrations = "aktualne";
            }
        }
    }

    /// <summary>Zakładka Połączenia: stan obu baz i test połączenia.</summary>
    public sealed partial class ConnectionsViewModel : ObservableObject
    {
        private readonly GeoBenchContextFactory _factory;
        private readonly IOperationHost _host;

        public ConnectionsViewModel(GeoBenchContextFactory factory, IOperationHost host)
        {
            _factory = factory;
            _host = host;
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
                {
                    _host.SetStatus($"Test połączenia: {database.Name}");
                    database.State = "sprawdzanie…";
                    var status = await DatabaseStatus.CheckAsync(_factory, database.Engine, cancellationToken);
                    database.Apply(status);
                    _host.AddLog(status.CanConnect
                            ? $"{database.Name}: {database.State} ({status.ServerVersion})"
                            : $"{database.Name}: {status.Error}",
                        isError: !status.IsReady);
                }
            });
    }
}
