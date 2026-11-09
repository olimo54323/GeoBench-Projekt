using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GeoBench.Wpf.ViewModels;

namespace GeoBench.Wpf.Views
{
    /// <summary>
    /// Okno informacji o obiektach. Kod widoku przekazuje do ViewModelu zaznaczenie drzewa (TreeView.SelectedItem
    /// nie da się wiązać), zaznacza węzeł pod prawym przyciskiem i obsługuje Ctrl+C dla zaznaczonego węzła.
    /// </summary>
    public partial class FeatureInfoWindow : Window
    {
        public FeatureInfoWindow()
        {
            InitializeComponent();
        }

        private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
            (DataContext as FeatureInfoViewModel)?.OnSelectedNodeChanged(e.NewValue as FeatureNodeViewModel);

        private void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Bez tego menu kontekstowe dotyczy węzła pod kursorem, ale zaznaczenie (i podświetlenie) zostaje na poprzednim
            for (var element = e.OriginalSource as DependencyObject; element != null; element = VisualTreeHelper.GetParent(element))
            {
                if (element is TreeViewItem item)
                {
                    item.IsSelected = true;
                    item.Focus();
                    return;
                }
            }
        }

        private void OnCopyExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            switch (Tree.SelectedItem)
            {
                case FeatureFieldNodeViewModel field:
                    field.CopyFieldCommand.Execute(null);
                    break;
                case FeatureObjectNodeViewModel feature:
                    feature.CopyObjectCommand.Execute(null);
                    break;
                case FeatureTableNodeViewModel table:
                    table.CopyTableCommand.Execute(null);
                    break;
            }
        }
    }
}
