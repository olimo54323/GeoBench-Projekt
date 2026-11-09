using System.Windows;
using System.Windows.Data;

namespace GeoBench.Wpf.Views
{
    public partial class MapStyleWindow : Window
    {
        public MapStyleWindow()
        {
            InitializeComponent();
            // Grupowanie listy: Ewidencja, Plan ogólny gminy
            Loaded += (_, _) =>
            {
                var view = CollectionViewSource.GetDefaultView(ItemsList.ItemsSource);
                if (view != null && view.GroupDescriptions.Count == 0)
                    view.GroupDescriptions.Add(new PropertyGroupDescription("Group"));
            };
        }

        private void OnSaveClick(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
