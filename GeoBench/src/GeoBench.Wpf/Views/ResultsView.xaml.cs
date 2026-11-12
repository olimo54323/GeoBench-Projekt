using System.Windows;
using System.Windows.Controls;
using GeoBench.Wpf.ViewModels;

namespace GeoBench.Wpf.Views
{
    /// <summary>
    /// Zakładka Wyniki. Kontrolka ScottPlot nie ma wiązania danych, więc widok rysuje wykres od nowa
    /// po zdarzeniu ChartInvalidated ViewModelu.
    /// </summary>
    public partial class ResultsView : UserControl
    {
        private ResultsViewModel? _viewModel;

        public ResultsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel != null)
                _viewModel.ChartInvalidated -= OnChartInvalidated;
            _viewModel = e.NewValue as ResultsViewModel;
            if (_viewModel != null)
                _viewModel.ChartInvalidated += OnChartInvalidated;
            Redraw();
        }

        private void OnChartInvalidated(object? sender, EventArgs e)
        {
            // Zdarzenie może przyjść po pomiarze spoza wątku interfejsu
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(Redraw);
                return;
            }
            Redraw();
        }

        private void Redraw()
        {
            if (_viewModel == null)
                return;
            // Nowy Plot: bez podziałek i legendy z poprzedniego wykresu
            Chart.Reset();
            _viewModel.Draw(Chart.Plot);
            Chart.Refresh();
        }
    }
}
