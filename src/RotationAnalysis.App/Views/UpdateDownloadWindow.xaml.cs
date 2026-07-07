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
        Closing += (_, _) =>
        {
            // Guard in case Closing fires after _cts is disposed (e.g. rapid shutdown).
            try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        };
    }

    private async void UpdateDownloadWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var progress = new Progress<double>(p => ProgressBarControl.Value = p * 100);
        // Strip any directory components from the asset name to prevent path traversal.
        var safeFileName = Path.GetFileName(_updateInfo.InstallerFileName);
        var destinationPath = Path.Combine(Path.GetTempPath(), safeFileName);

        try
        {
            await _updateChecker.DownloadInstallerAsync(_updateInfo.InstallerDownloadUrl, destinationPath, progress, _cts.Token);
            InstallerPath = destinationPath;
            if (IsVisible) DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(destinationPath);
            if (IsVisible) DialogResult = false;
        }
        catch (Exception ex)
        {
            TryDeleteFile(destinationPath);
            AppLog.LogError("DownloadUpdate", ex);
            FailureMessage = ex.Message;
            if (IsVisible) DialogResult = false;
        }
        finally
        {
            // Dispose after all use of the token is complete.  Closing fires before this
            // finally runs (Closing → Close() returns → DialogResult assignment returns →
            // finally), so _cts is never disposed before Cancel() is called.
            _cts.Dispose();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        StatusText.Text = "Cancelling…";
    }

    private static void TryDeleteFile(string path)
    {
        // File.Delete does not throw when the file does not exist; IOException/
        // UnauthorizedAccessException only occur for genuine I/O problems.
        try { File.Delete(path); }
        catch (IOException ex) { AppLog.LogError("DeleteTempInstaller", ex); }
        catch (UnauthorizedAccessException ex) { AppLog.LogError("DeleteTempInstaller", ex); }
    }
}
