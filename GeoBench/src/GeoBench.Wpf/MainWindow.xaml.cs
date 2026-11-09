using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using GeoBench.Wpf.ViewModels;

namespace GeoBench.Wpf
{
    /// <summary>
    /// Okno główne: menu, zakładki, log i pasek stanu. Kod okna obsługuje tylko sprawy widoku
    /// (zamykanie, przewijanie logu); logika jest w MainViewModel.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
            viewModel.Log.CollectionChanged += OnLogChanged;
        }

        private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // ScrollIntoView wymusza układ; wywołany wewnątrz CollectionChanged trafia na listę, która jeszcze
            // nie obsłużyła zmiany (wyjątek "ItemsControl jest niespójny ze źródłem"), więc przewijamy później
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems?.Count > 0)
            {
                var item = e.NewItems[0]!;
                Dispatcher.InvokeAsync(() => LogList.ScrollIntoView(item), DispatcherPriority.Background);
            }
        }

        private void OnExitClick(object sender, RoutedEventArgs e) => Close();

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (!_viewModel.ConfirmClose())
                e.Cancel = true;
        }
    }
}
