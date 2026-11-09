using System.Reflection;
using System.Windows;

namespace GeoBench.Wpf.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = $"Wersja {version?.ToString(3) ?? "?"}";
        }
    }
}
