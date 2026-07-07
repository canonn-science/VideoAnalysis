using System.IO;
using System.Windows;
using RotationAnalysis.Core.Diagnostics;
using RotationAnalysis.Core.Updates;

namespace RotationAnalysis.App.Views;

public partial class UpdateDownloadWindow : Window
{
    private readonly UpdateChecker _updateChecker;
    private readonly UpdateInfo _updateInfo;
    private readonly CancellationTokenSource _cts = new();

    public string? InstallerPath { get; private set; }
    public string? FailureMessage { get; private set; }

    public UpdateDownloadWindow(UpdateChecker updateChecker, UpdateInfo updateInfo)
    {
        InitializeComponent();
        _updateChecker = updateChecker;
        _updateInfo = updateInfo;
        StatusText.Text = $"Downloading Rotation Analysis Lab {updateInfo.Version}…";
        Loaded += UpdateDownloadWindow_Loaded;
    }

    private async void UpdateDownloadWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var progress = new Progress<double>(p => ProgressBarControl.Value = p * 100);
        var destinationPath = Path.Combine(Path.GetTempPath(), _updateInfo.InstallerFileName);

        try
        {
            await _updateChecker.DownloadInstallerAsync(_updateInfo.InstallerDownloadUrl, destinationPath, progress, _cts.Token);
            InstallerPath = destinationPath;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            DialogResult = false;
        }
        catch (Exception ex)
        {
            AppLog.LogError("DownloadUpdate", ex);
            FailureMessage = ex.Message;
            DialogResult = false;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        StatusText.Text = "Cancelling…";
    }
}
