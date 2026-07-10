using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using RotationAnalysis.App.Infrastructure;
using RotationAnalysis.App.ViewModels;
using RotationAnalysis.Core.Diagnostics;
using RotationAnalysis.Core.Storage;
using RotationAnalysis.Core.VideoAnalysis;

namespace RotationAnalysis.App.Views;

public partial class VideoProcessingWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly RingRowViewModel _row;
    private readonly Task<QuickVideoMetadata> _quickMetadataTask;
    private readonly CancellationTokenSource _cts = new();
    private bool _realProgressReceived;
    private string _videoPath;

    public HorizontalVideoAnalysisResult? Result { get; private set; }
    public string? FailureMessage { get; private set; }

    /// <summary>The video's path after this window closes - the renamed path if the user accepted
    /// the rename prompt and the rename succeeded, otherwise the original upload path.</summary>
    public string FinalVideoPath => _videoPath;

    private Task<HorizontalVideoAnalysisResult>? _analysisTask;
    private string? _pendingRenamePath;

    public VideoProcessingWindow(MainViewModel viewModel, string videoPath, RingRowViewModel row, Task<QuickVideoMetadata> quickMetadataTask)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _videoPath = videoPath;
        _row = row;
        _quickMetadataTask = quickMetadataTask;
        Loaded += VideoProcessingWindow_Loaded;
    }

    private void VideoProcessingWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // OpenCV's own file open can be slow (large/poorly-indexed capture files), and reports no
        // progress at all until it finishes. While that's in flight, read the same metadata and
        // thumbnail Windows Explorer would show (via the Shell Property System / thumbnail cache)
        // so the window shows something immediately instead of sitting blank.
        _ = ShowQuickPreviewAsync();

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
                HideLoadingRing();
            }
        });

        _analysisTask = _viewModel.AnalyzeVideoAsync(_row, _videoPath, progress, _cts.Token);

        if (VideoFileNamer.MatchesRingName(_videoPath, _row.Ring.RingName))
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

        var suggestedPath = VideoFileNamer.GetNextAvailableFileName(_videoPath, _row.Ring.RingName);
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

                    var calibrationLogPath = RotationCalibrationLogWriter.PathFor(_videoPath);
                    if (File.Exists(calibrationLogPath))
                    {
                        File.Move(calibrationLogPath, RotationCalibrationLogWriter.PathFor(_pendingRenamePath));
                    }

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

    private async Task ShowQuickPreviewAsync()
    {
        var meta = await _quickMetadataTask;

        // The real analysis may already have reported actual (OpenCV-derived) numbers by the
        // time this finishes - never regress the display back to the approximate shell values.
        if (_realProgressReceived)
        {
            return;
        }

        if (meta.Thumbnail is not null)
        {
            CurrentFrameImage.Source = meta.Thumbnail;
            HideLoadingRing();
        }

        if (meta.Width is int width && meta.Height is int height)
        {
            var parts = new List<string> { $"{width}×{height}" };
            if (meta.FrameRate is double fps)
            {
                parts.Add($"{fps:0.##} fps");
            }
            if (meta.Duration is TimeSpan duration)
            {
                parts.Add(DurationFormat.Seconds(duration.TotalSeconds));
            }
            VideoMetaText.Text = string.Join(" · ", parts) + " (from file properties)";
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
