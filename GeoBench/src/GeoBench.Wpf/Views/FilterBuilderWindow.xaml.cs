using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GeoBench.Core.Data.Filters;
using GeoBench.Wpf.ViewModels;

namespace GeoBench.Wpf.Views
{
    /// <summary>
    /// Builder filtrów. Kod widoku obsługuje podpowiedzi przy kursorze w polu WHERE (pozycja popupu, klawisze,
    /// wstawienie i ustawienie kursora), których nie da się wyrazić wiązaniami.
    /// </summary>
    public partial class FilterBuilderWindow : Window
    {
        private bool _applying;

        public FilterBuilderWindow()
        {
            InitializeComponent();
            DataContextChanged += (_, e) =>
            {
                if (e.OldValue is FilterBuilderViewModel old)
                    old.SuggestionsChanged -= OnSuggestionsChanged;
                if (e.NewValue is FilterBuilderViewModel current)
                    current.SuggestionsChanged += OnSuggestionsChanged;
            };
        }

        private FilterBuilderViewModel? ViewModel => DataContext as FilterBuilderViewModel;

        protected override void OnClosing(CancelEventArgs e)
        {
            // Niezapisane zmiany: pytanie o zapis; nieudany zapis zostawia okno otwarte
            if (ViewModel is { } viewModel && !viewModel.ConfirmLeave())
                e.Cancel = true;
            base.OnClosing(e);
        }

        private void OnWhereTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_applying || !WhereBox.IsKeyboardFocusWithin)
                return;
            ShowSuggestions(explicitRequest: false);
        }

        private void OnWhereSelectionChanged(object sender, RoutedEventArgs e)
        {
            // Przesunięcie kursora przy otwartej liście: lista dla nowego miejsca
            if (!_applying && ViewModel is { IsSuggestionsOpen: true })
                ShowSuggestions(explicitRequest: false);
        }

        private void OnWherePreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ViewModel is not { } viewModel)
                return;

            if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ShowSuggestions(explicitRequest: true);
                e.Handled = true;
                return;
            }
            if (!viewModel.IsSuggestionsOpen)
                return;

            switch (e.Key)
            {
                case Key.Down:
                    viewModel.MoveSuggestion(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    viewModel.MoveSuggestion(-1);
                    e.Handled = true;
                    break;
                case Key.PageDown:
                    viewModel.MoveSuggestion(8);
                    e.Handled = true;
                    break;
                case Key.PageUp:
                    viewModel.MoveSuggestion(-8);
                    e.Handled = true;
                    break;
                case Key.Enter or Key.Tab when viewModel.SelectedSuggestion is { } item:
                    Apply(item);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    viewModel.IsSuggestionsOpen = false;
                    e.Handled = true; // Esc nie zamyka okna, gdy jest otwarta lista
                    break;
            }
            if (e.Handled && viewModel.SelectedSuggestion != null)
                SuggestionsList.ScrollIntoView(viewModel.SelectedSuggestion);
        }

        private void OnSuggestionClicked(object sender, MouseButtonEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is WhereCompletionItem item)
                Apply(item);
        }

        private void OnSuggestionsChanged(object? sender, EventArgs e)
        {
            // Wartości z bazy doszły w tle: odśwież listę, jeśli użytkownik nadal pisze w polu
            if (WhereBox.IsKeyboardFocusWithin)
                ShowSuggestions(explicitRequest: false);
        }

        private void ShowSuggestions(bool explicitRequest)
        {
            if (ViewModel is not { } viewModel)
                return;
            var caret = WhereBox.CaretIndex;
            viewModel.UpdateSuggestions(caret, explicitRequest);
            if (viewModel.IsSuggestionsOpen)
            {
                var rect = WhereBox.GetRectFromCharacterIndex(caret);
                if (!rect.IsEmpty)
                {
                    SuggestionsPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                    SuggestionsPopup.PlacementRectangle = rect;
                }
            }
        }

        // Przyciski składni i lista kolumn: wstawienie w miejscu kursora pola WHERE

        private void OnSnippetClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is string template)
                Insert(template);
        }

        private void OnColumnDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is FilterColumnItem column)
                Insert(column.Name);
        }

        private void OnColumnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ColumnsList.SelectedItem is FilterColumnItem column)
            {
                Insert(column.Name);
                e.Handled = true;
            }
        }

        private void Insert(string template)
        {
            if (ViewModel is not { HasSelection: true } viewModel)
                return;
            _applying = true;
            try
            {
                var caret = viewModel.InsertSnippet(template, WhereBox.SelectionStart, WhereBox.SelectionLength);
                WhereBox.Focus();
                WhereBox.CaretIndex = Math.Min(caret, WhereBox.Text.Length);
            }
            finally
            {
                _applying = false;
            }
            // Np. po „=” od razu lista wartości kolumny
            Dispatcher.InvokeAsync(() => ShowSuggestions(explicitRequest: false), DispatcherPriority.Input);
        }

        private void Apply(WhereCompletionItem item)
        {
            if (ViewModel is not { } viewModel)
                return;
            _applying = true;
            try
            {
                var caret = viewModel.ApplySuggestion(item);
                WhereBox.Focus();
                WhereBox.CaretIndex = Math.Min(caret, WhereBox.Text.Length);
            }
            finally
            {
                _applying = false;
            }
            // Kolejna podpowiedź od razu (np. po kolumnie operatory), gdy tekst w polu jest już zaktualizowany
            Dispatcher.InvokeAsync(() => ShowSuggestions(explicitRequest: false), DispatcherPriority.Input);
        }
    }
}
