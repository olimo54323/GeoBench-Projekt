using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GeoBench.Wpf.ViewModels;
using Mapsui.Extensions;

namespace GeoBench.Wpf.Views
{
    /// <summary>
    /// Widok mapy. Mapa Mapsui należy do MapViewModel; kod widoku tylko przypina ją do kontrolki,
    /// zgłasza wczytanie (dopiero wtedy kontrolka zna rozmiar okna widoku) i zamienia klik PPM na współrzędne mapy.
    /// </summary>
    public partial class MapView : UserControl
    {
        public MapView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += (_, _) => (DataContext as MapViewModel)?.OnViewLoaded();
            // Kontrolka mapy może sama oznaczać zdarzenia myszy jako obsłużone
            MapControl.AddHandler(MouseRightButtonUpEvent, new MouseButtonEventHandler(OnMapRightButtonUp), handledEventsToo: true);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is MapViewModel viewModel)
                MapControl.Map = viewModel.Map;
        }

        // Zmiana nazwy filtra w miejscu: F2 na wierszu filtra, Enter zatwierdza, Esc anuluje, utrata fokusu zatwierdza

        private void OnFilterKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2 && (sender as FrameworkElement)?.DataContext is FilterNodeViewModel { IsRenaming: false } filter)
            {
                filter.StartRenameCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void OnRenameKeyDown(object sender, KeyEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not FilterNodeViewModel filter)
                return;
            if (e.Key == Key.Enter)
            {
                filter.CommitRename();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                filter.CancelRename();
                e.Handled = true;
            }
        }

        private void OnRenameLostFocus(object sender, KeyboardFocusChangedEventArgs e) =>
            ((sender as FrameworkElement)?.DataContext as FilterNodeViewModel)?.CommitRename();

        private void OnRenameVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox { IsVisible: true } box)
            {
                // Fokus dopiero po wyrenderowaniu pola
                box.Dispatcher.InvokeAsync(() =>
                {
                    box.Focus();
                    box.SelectAll();
                }, System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void OnMapRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not MapViewModel viewModel)
                return;
            var position = e.GetPosition(MapControl);
            var world = MapControl.Map.Navigator.Viewport.ScreenToWorld(position.X, position.Y);
            viewModel.Identify(world.X, world.Y);
            e.Handled = true;
        }
    }
}
