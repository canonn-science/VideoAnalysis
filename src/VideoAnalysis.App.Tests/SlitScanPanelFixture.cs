namespace VideoAnalysis.App.Tests;

/// <summary>Shared by every test in <c>SlitScanPresetTests</c> (via <c>IClassFixture</c>, so xUnit
/// creates it once and runs those tests sequentially on its one STA thread): boots a minimal
/// <see cref="VideoAnalysis.App.App"/> so the Canonn brush/style resources that
/// <c>SlitScanControlPanel.xaml</c> pulls in via StaticResource are available, without calling
/// <c>Run()</c> - so no MainWindow, no update checks, no theme/startup side effects.</summary>
public sealed class SlitScanPanelFixture : IDisposable
{
    public StaThread Sta { get; } = new();

    public SlitScanPanelFixture()
    {
        Sta.Invoke(() =>
        {
            if (System.Windows.Application.Current is null)
            {
                var app = new App();
                app.InitializeComponent();
            }
        });
    }

    public void Dispose() => Sta.Dispose();
}
