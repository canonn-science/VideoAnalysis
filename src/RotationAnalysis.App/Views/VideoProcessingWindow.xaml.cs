using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using RotationAnalysis.App.ViewModels;
using RotationAnalysis.Core.Diagnostics;
using RotationAnalysis.Core.Storage;
using RotationAnalysis.Core.VideoAnalysis;

namespace RotationAnalysis.App.Views;

public partial class VideoProcessingWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly string _ringName;
    private readonly double? _seedPeriodSeconds;
    private readonly CancellationTokenSource _cts = new();
    private string _videoPath;

    public HorizontalVideoAnalysisResult? Result { get; private set; }
    public string? FailureMessage { get; private set; }

    /// <summary>The video's path after this window closes - the renamed path if the user accepted
    /// the rename prompt and the rename succeeded, otherwise the original upload path.</summary>
    public string FinalVideoPath => _videoPath;

    private Task<HorizontalVideoAnalysisResult>? _analysisTask;
    private string? _pendingRenamePath;

    public VideoProcessingWindow(MainViewModel viewModel, string videoPath, double? seedPeriodSeconds, string ringName)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _videoPath = videoPath;
        _seedPeriodSeconds = seedPeriodSeconds;
        _ringName = ringName;
        Loaded += VideoProcessingWindow_Loaded;
    }

    private void VideoProcessingWindow_Loaded(object sender, RoutedEventArgs e)
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

        _analysisTask = _viewModel.AnalyzeVideoAsync(_videoPath, _seedPeriodSeconds, progress, _cts.Token);

        if (VideoFileNamer.MatchesRingName(_videoPath, _ringName))
        {
            _ = FinishAnalysisAsync();
        }
        else
        {
            // Wait for this window's own first paint before stealing the message pump with a
            // nested modal - otherwise it can sit unpainted behind the rename dialog for the
            // whole analysis run.
            ContentRendered += VideoProcessingWindow_ContentRendered;
        }
    }

    private async void VideoProcessingWindow_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= VideoProcessingWindow_ContentRendered;

        var suggestedPath = VideoFileNamer.GetNextAvailableFileName(_videoPath, _ringName);
        var renamePrompt = new VideoRenamePromptWindow(Path.GetFileName(suggestedPath)) { Owner = this };
        if (renamePrompt.ShowDialog() == true)
        {
            _pendingRenamePath = suggestedPath;
        }

        await FinishAnalysisAsync();
    }

    private async Task FinishAnalysisAsync()
    {
        try
        {
            Result = await _analysisTask!;

            if (_pendingRenamePath is not null)
            {
                try
                {
                    File.Move(_videoPath, _pendingRenamePath);
                    _videoPath = _pendingRenamePath;
                }
                catch (Exception ex)
                {
                    AppLog.LogError("RenameVideo", ex);
                }
            }

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
