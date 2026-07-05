using System.Windows;
using System.Windows.Media;
using ModernWpf;

namespace RotationAnalysis.App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Match the Canonn Signals site (signals.canonn.tech): dark theme, orange accent.
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
            ThemeManager.Current.AccentColor = Color.FromRgb(0xFF, 0x99, 0x00);
        }
    }
}
