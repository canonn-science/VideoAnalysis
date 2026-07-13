using System.IO;
using VideoAnalysis.App.Infrastructure;
using VideoAnalysis.Core.VideoAnalysis;
using VideoAnalysis.Core.VideoAnalysis.SlitScan;

namespace VideoAnalysis.App.ViewModels;

/// <summary>Slit Scan is a general creative video effect, not tied to a specific system/body -
/// unlike Ring/Station Rotation and Long Exposure, there's no Spansh lookup here. The user just
/// uploads a video directly from the tab and adjusts the controls embedded in it.</summary>
public sealed class SlitScanViewModel : ObservableObject
{
    private string? _videoFilePath;
    private string? _errorMessage;

    public string? VideoFilePath
    {
        get => _videoFilePath;
        set
        {
            if (SetField(ref _videoFilePath, value))
            {
                OnPropertyChanged(nameof(VideoFileName));
                OnPropertyChanged(nameof(HasVideo));
            }
        }
    }

    public string? VideoFileName => VideoFilePath is null ? null : Path.GetFileName(VideoFilePath);

    public bool HasVideo => VideoFilePath is not null;

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value);
    }

    public Task<SlitScanResult> GenerateAsync(string videoPath, SlitScanParameters parameters, IProgress<VideoAnalysisProgress> progress, CancellationToken ct)
        => SlitScanProcessor.GenerateAsync(videoPath, parameters, progress, ct);

    /// <summary>A single representative frame (PNG bytes), for the geometry-guide preview - null
    /// if there's no video yet or the frame couldn't be read.</summary>
    public Task<byte[]?> LoadPreviewFrameAsync(CancellationToken ct)
        => VideoFilePath is null
            ? Task.FromResult<byte[]?>(null)
            : VideoFrameReader.ReadRepresentativeFrameAsync(VideoFilePath, ct);
}
