using System.Windows;
using GeoBench.Wpf.ViewModels;
using GeoBench.Wpf.Views;
using Microsoft.Win32;

namespace GeoBench.Wpf.Services
{
    /// <summary>Okna dialogowe dla ViewModeli, żeby nie odwoływały się bezpośrednio do klas okien.</summary>
    public interface IDialogService
    {
        /// <summary>Ścieżka wybranego pliku albo null, gdy użytkownik zrezygnował.</summary>
        string? OpenFile(string title, string filter);

        bool Confirm(string message, string title);

        void ShowError(string message, string title);

        void ShowAbout();

        /// <summary>Okno stylu mapy; true, gdy użytkownik zapisał zmiany.</summary>
        bool ShowMapStyle(MapStyleViewModel viewModel);

        /// <summary>Niemodalne okno informacji o obiektach; closed jest wywoływane po jego zamknięciu.</summary>
        void ShowFeatureInfo(FeatureInfoViewModel viewModel, Action closed);

        /// <summary>Builder filtrów warstw (modalny).</summary>
        void ShowFilterBuilder(FilterBuilderViewModel viewModel);
    }

    public sealed class DialogService : IDialogService
    {
        public string? OpenFile(string title, string filter)
        {
            var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
            var owner = Owner;
            var result = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            return result == true ? dialog.FileName : null;
        }

        public bool Confirm(string message, string title) =>
            Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        public void ShowError(string message, string title) =>
            Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

        public void ShowAbout() => new AboutWindow { Owner = Owner }.ShowDialog();

        public bool ShowMapStyle(MapStyleViewModel viewModel) =>
            new MapStyleWindow { Owner = Owner, DataContext = viewModel }.ShowDialog() == true;

        public void ShowFeatureInfo(FeatureInfoViewModel viewModel, Action closed)
        {
            var owner = Owner;
            var window = new FeatureInfoWindow { Owner = owner, DataContext = viewModel };
            if (owner is { WindowState: WindowState.Normal })
            {
                // Przy prawej krawędzi okna głównego, żeby nie zasłaniać środka mapy
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = owner.Left + Math.Max(0, owner.ActualWidth - window.Width - 300);
                window.Top = owner.Top + 80;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            window.Closed += (_, _) => closed();
            window.Show();
        }

        public void ShowFilterBuilder(FilterBuilderViewModel viewModel) =>
            new FilterBuilderWindow { Owner = Owner, DataContext = viewModel }.ShowDialog();

        private static Window? Owner => Application.Current?.MainWindow is { IsLoaded: true } window ? window : null;

        private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
        {
            var owner = Owner;
            return owner == null
                ? MessageBox.Show(message, title, buttons, image)
                : MessageBox.Show(owner, message, title, buttons, image);
        }
    }
}
