using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using RotationAnalysis.App.ViewModels;
using RotationAnalysis.Core.Diagnostics;
using RotationAnalysis.Core.VideoAnalysis;

namespace RotationAnalysis.App.Views;

public partial class VideoProcessingWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly string _videoPath;
    private readonly double? _seedPeriodSeconds;
    private readonly CancellationTokenSource _cts = new();

    public HorizontalVideoAnalysisResult? Result { get; private set; }
    public string? FailureMessage { get; private set; }

    public VideoProcessingWindow(MainViewModel viewModel, string videoPath, double? seedPeriodSeconds)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _videoPath = videoPath;
        _seedPeriodSeconds = seedPeriodSeconds;
        Loaded += VideoProcessingWindow_Loaded;
    }

    private async void VideoProcessingWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var progress = new Progress<VideoAnalysisProgress>(p =>
        {
            ProgressBarControl.Value = p.PercentComplete;
            StatusText.Text = p.Message;
            FrameCounterText.Text = p.TotalFrames > 0 ? $"Frame {p.FramesProcessed} of {p.TotalFrames}" : string.Empty;

            if (p.Stage == VideoAnalysisStage.Opening && !string.IsNullOrEmpty(p.Message))
            {
                VideoMetaText.Text = p.Message;
            }

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
            }
        });

        try
        {
            Result = await _viewModel.AnalyzeVideoAsync(_videoPath, _seedPeriodSeconds, progress, _cts.Token);
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            DialogResult = false;
        }
        catch (Exception ex)
        {
            AppLog.LogError("AnalyzeVideo", ex);
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
