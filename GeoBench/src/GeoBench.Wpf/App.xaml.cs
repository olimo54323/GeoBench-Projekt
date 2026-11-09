using System.Windows;
using System.Windows.Threading;
using GeoBench.Core.Configuration;
using GeoBench.Core.Data;
using GeoBench.Wpf.Services;
using GeoBench.Wpf.ViewModels;

namespace GeoBench.Wpf
{
    /// <summary>
    /// Start aplikacji: konfiguracja (appsettings.json wyszukiwany w górę drzewa katalogów),
    /// ręczne złożenie ViewModeli (bez kontenera DI) i obsługa nieprzechwyconych wyjątków.
    /// </summary>
    public partial class App : Application
    {
        private MainViewModel? _mainViewModel;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            var dialogs = new DialogService();
            var factory = new GeoBenchContextFactory(GeoBenchConfiguration.Build());
            _mainViewModel = new MainViewModel(factory, dialogs);

            var window = new MainWindow(_mainViewModel);
            MainWindow = window;
            window.Show();

            _mainViewModel.Connections.TestCommand.Execute(null);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            _mainViewModel?.AddLog($"Nieoczekiwany błąd: {e.Exception.GetBaseException().Message}", isError: true);
            MessageBox.Show(e.Exception.GetBaseException().Message, "GeoBench – błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
