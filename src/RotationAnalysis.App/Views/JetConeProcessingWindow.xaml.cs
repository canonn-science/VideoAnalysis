using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using RotationAnalysis.Core.Diagnostics;
using RotationAnalysis.Core.VideoAnalysis;

namespace RotationAnalysis.App.Views;

public partial class JetConeProcessingWindow : Window
{
    private readonly Func<string, IProgress<VideoAnalysisProgress>, CancellationToken, Task<JetConeAnalysisResult>> _analyzeVideo;
    private readonly string _videoPath;
    private readonly CancellationTokenSource _cts = new();
    private bool _realProgressReceived;

    public JetConeAnalysisResult? Result { get; private set; }
    public string? FailureMessage { get; private set; }

    public JetConeProcessingWindow(
        Func<string, IProgress<VideoAnalysisProgress>, CancellationToken, Task<JetConeAnalysisResult>> analyzeVideo,
        string videoPath)
    {
        InitializeComponent();
        _analyzeVideo = analyzeVideo;
        _videoPath = videoPath;
        Loaded += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        var progress = new Progress<VideoAnalysisProgress>(p =>
        {
            if (!_realProgressReceived)
            {
                _realProgressReceived = true;
                ProgressBarControl.IsIndeterminate = false;
            }
            ProgressBarControl.Value = p.PercentComplete;
            StatusText.Text = p.Message;
            FrameCounterText.Text = p.TotalFrames > 0 ? $"Frame {p.FramesProcessed} of {p.TotalFrames}" : string.Empty;

            if (p.PreviewImageBytes is { Length: > 0 } bytes)
            {
                var bitmap = new BitmapImage();
                using var stream = new MemoryStream(bytes);
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                CurrentFrameImage.Source = bitmap;
                HideLoadingRing();
            }
        });

        try
        {
            Result = await _analyzeVideo(_videoPath, progress, _cts.Token);
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            DialogResult = false;
        }
        catch (Exception ex)
        {
            AppLog.LogError("AnalyzeJetConeVideo", ex);
            FailureMessage = ex.Message;
            DialogResult = false;
        }
    }

    private void HideLoadingRing()
    {
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        StatusText.Text = "Cancelling…";
    }
}
