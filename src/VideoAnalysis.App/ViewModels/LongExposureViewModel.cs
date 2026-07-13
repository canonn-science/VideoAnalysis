using System.IO;
using VideoAnalysis.App.Infrastructure;
using VideoAnalysis.Core.VideoAnalysis;
using VideoAnalysis.Core.VideoAnalysis.LongExposure;

namespace VideoAnalysis.App.ViewModels;

/// <summary>Long Exposure is driven entirely by the shared video library selection (see
/// <see cref="VideoLibraryViewModel.EntrySelected"/>) rather than its own system search - the
/// selected library entry already carries whatever system/body/station identity it was tagged
/// with, which is all this mode ever needed one for (naming the output file), same rationale as
/// <see cref="SlitScanViewModel"/> not doing a Spansh lookup of its own.</summary>
public sealed class LongExposureViewModel : ObservableObject
{
    private string? _videoFilePath;
    private string? _systemName;
    private string? _bodyOrStationName;
    private double _motionBlurAlpha = LongExposureProcessor.DefaultMotionBlurAlpha;
    private string? _errorMessage;
    private LongExposureResult? _result;

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

    /// <summary>Captured from the selected library entry - used only to name the saved output
    /// files, falling back to the video's own filename when the entry has no system tagged.</summary>
    public string? SystemName
    {
        get => _systemName;
        set => SetField(ref _systemName, value);
    }

    public string? BodyOrStationName
    {
        get => _bodyOrStationName;
        set => SetField(ref _bodyOrStationName, value);
    }

    /// <summary>0.01-1.0 blend weight for the Motion Blur variant's exponential moving average -
    /// see <see cref="LongExposureProcessor.GenerateAsync"/>.</summary>
    public double MotionBlurAlpha
    {
        get => _motionBlurAlpha;
        set => SetField(ref _motionBlurAlpha, Math.Clamp(value, 0.01, 1.0));
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value);
    }

    /// <summary>The most recently generated set of six variants, shown inline in the tab rather
    /// than a separate results window - null before the first generation (or after selecting a
    /// different video, since a stale result no longer corresponds to what's loaded).</summary>
    public LongExposureResult? Result
    {
        get => _result;
        set
        {
            if (SetField(ref _result, value))
            {
                OnPropertyChanged(nameof(HasResult));
            }
        }
    }

    public bool HasResult => Result is not null;

    public Task<LongExposureResult> GenerateAsync(string videoPath, IProgress<VideoAnalysisProgress> progress, CancellationToken ct)
        => LongExposureProcessor.GenerateAsync(videoPath, MotionBlurAlpha, progress, ct);
}
