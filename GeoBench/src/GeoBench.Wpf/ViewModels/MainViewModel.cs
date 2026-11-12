using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoBench.Core.Data;
using GeoBench.Wpf.Services;

namespace GeoBench.Wpf.ViewModels
{
    /// <summary>Wpis logu w dolnej części okna.</summary>
    public sealed record LogEntry(DateTime Time, string Message, bool IsError);

    /// <summary>
    /// Wspólny stan okna dla zakładek: jedna operacja naraz (zajętość, tekst stanu, anulowanie) i log.
    /// </summary>
    public interface IOperationHost
    {
        bool IsBusy { get; }

        /// <summary>
        /// Wykonuje operację jako jedyną aktywną: ustawia zajętość i tekst stanu, przekazuje token anulowania,
        /// a wyjątki zapisuje w logu i pokazuje użytkownikowi. Zwraca false, gdy operacja nie zakończyła się sukcesem.
        /// </summary>
        Task<bool> RunAsync(string description, Func<CancellationToken, Task> operation);

        void SetStatus(string text);

        /// <summary>Postęp z wartością (np. strona 7/17); bez liczby wszystkich pasek jest nieokreślony.</summary>
        void SetProgress(int processed, int? total);

        void AddLog(string message, bool isError = false);
    }

    public sealed partial class MainViewModel : ObservableObject, IOperationHost
    {
        private const int MaxLogEntries = 2000;

        private readonly IDialogService _dialogs;
        private readonly Dispatcher _dispatcher;
        private CancellationTokenSource? _cancellation;

        public MainViewModel(GeoBenchContextFactory factory, IDialogService dialogs)
        {
            // ViewModel powstaje w wątku interfejsu (App.OnStartup)
            _dispatcher = Dispatcher.CurrentDispatcher;
            _dialogs = dialogs;
            Connections = new ConnectionsViewModel(factory, this, dialogs);
            Import = new ImportViewModel(factory, this, dialogs);
            MapPanel = new MapViewModel(factory, this, dialogs);
            Benchmark = new BenchmarkViewModel(factory, this, dialogs);
        }

        public ConnectionsViewModel Connections { get; }

        public ImportViewModel Import { get; }

        /// <summary>Zakładka Mapa.</summary>
        public MapViewModel MapPanel { get; }

        /// <summary>Zakładka Benchmark.</summary>
        public BenchmarkViewModel Benchmark { get; }

        public ObservableCollection<LogEntry> Log { get; } = [];

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusText = "Gotowy";

        [ObservableProperty]
        private double _progressValue;

        [ObservableProperty]
        private double _progressMaximum = 1;

        [ObservableProperty]
        private bool _isProgressIndeterminate = true;

        public void SetProgress(int processed, int? total)
        {
            IsProgressIndeterminate = total is null or <= 0;
            ProgressMaximum = total is > 0 ? total.Value : 1;
            ProgressValue = total is > 0 ? Math.Min(processed, total.Value) : 0;
        }

        public async Task<bool> RunAsync(string description, Func<CancellationToken, Task> operation)
        {
            if (IsBusy)
                return false;

            using var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            IsProgressIndeterminate = true;
            IsBusy = true;
            StatusText = description;
            AddLog($"{description}…");
            try
            {
                await operation(cancellation.Token);
                StatusText = "Gotowy";
                return true;
            }
            catch (OperationCanceledException)
            {
                AddLog($"{description}: przerwano.", isError: true);
                StatusText = "Przerwano";
                return false;
            }
            catch (Exception ex)
            {
                var message = ex.GetBaseException().Message;
                AddLog($"{description}: {message}", isError: true);
                StatusText = "Błąd";
                _dialogs.ShowError(message, description);
                return false;
            }
            finally
            {
                _cancellation = null;
                IsBusy = false;
            }
        }

        public void SetStatus(string text) => StatusText = text;

        public void AddLog(string message, bool isError = false)
        {
            // Kolekcję związaną z listą wolno zmieniać tylko w wątku interfejsu
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.InvokeAsync(() => AddLog(message, isError));
                return;
            }

            Log.Add(new LogEntry(DateTime.Now, message, isError));
            if (Log.Count > MaxLogEntries)
                Log.RemoveAt(0);
        }

        /// <summary>Pyta o zamknięcie; przy trwającej operacji przerywa ją po potwierdzeniu.</summary>
        public bool ConfirmClose()
        {
            var message = IsBusy
                ? "Trwa operacja. Przerwać ją i zamknąć aplikację?"
                : "Zamknąć aplikację GeoBench?";
            if (!_dialogs.Confirm(message, "Zamykanie"))
                return false;
            _cancellation?.Cancel();
            return true;
        }

        partial void OnIsBusyChanged(bool value)
        {
            Connections.TestCommand.NotifyCanExecuteChanged();
            Connections.MigrateCommand.NotifyCanExecuteChanged();
            Import.ImportCommand.NotifyCanExecuteChanged();
            Import.WfsImportCommand.NotifyCanExecuteChanged();
            Benchmark?.RunCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(IsBusy))]
        private void Cancel() => _cancellation?.Cancel();

        [RelayCommand]
        private void ShowAbout() => _dialogs.ShowAbout();
    }
}
