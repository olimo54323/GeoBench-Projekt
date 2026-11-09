using System.Windows;
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
