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
    private string? _ringName;
    private double _motionBlurAlpha = LongExposureProcessor.DefaultMotionBlurAlpha;
    private string? _errorMessage;
    private LongExposureResult? _result;
    private bool _includeAverage = true;
    private bool _includeMaximum = true;
    private bool _includeMinimum = true;
    private bool _includeMaxMinusMin = true;
    private bool _includeMotionVariance = true;
    private bool _includeMotionBlur = true;

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

    public string? RingName
    {
        get => _ringName;
        set => SetField(ref _ringName, value);
    }

    /// <summary>The name a saved output image is based on: the most specific of ring/body/system
    /// that's tagged on the selected library entry - the same Ring &gt; Body &gt; System priority
    /// <see cref="VideoUploadMetadataViewModel.SuggestedFileBaseName"/> uses to rename the source
    /// video itself, so a saved image and its source video end up named consistently.</summary>
    public string? SuggestedFileBaseName =>
        !string.IsNullOrWhiteSpace(RingName) ? RingName :
        !string.IsNullOrWhiteSpace(BodyOrStationName) ? BodyOrStationName :
        SystemName;

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

    /// <summary>Unchecking a mode skips generating it entirely (see
    /// <see cref="LongExposureProcessor.GenerateAsync"/>), not just hiding it afterward - so
    /// deselecting variants you don't need speeds up generation.</summary>
    public bool IncludeAverage
    {
        get => _includeAverage;
        set => SetField(ref _includeAverage, value);
    }

    public bool IncludeMaximum
    {
        get => _includeMaximum;
        set => SetField(ref _includeMaximum, value);
    }

    public bool IncludeMinimum
    {
        get => _includeMinimum;
        set => SetField(ref _includeMinimum, value);
    }

    public bool IncludeMaxMinusMin
    {
        get => _includeMaxMinusMin;
        set => SetField(ref _includeMaxMinusMin, value);
    }

    public bool IncludeMotionVariance
    {
        get => _includeMotionVariance;
        set => SetField(ref _includeMotionVariance, value);
    }

    public bool IncludeMotionBlur
    {
        get => _includeMotionBlur;
        set => SetField(ref _includeMotionBlur, value);
    }

    public LongExposureVariants SelectedVariants
    {
        get
        {
            var variants = LongExposureVariants.None;
            if (IncludeAverage) variants |= LongExposureVariants.Average;
            if (IncludeMaximum) variants |= LongExposureVariants.Maximum;
            if (IncludeMinimum) variants |= LongExposureVariants.Minimum;
            if (IncludeMaxMinusMin) variants |= LongExposureVariants.MaxMinusMin;
            if (IncludeMotionVariance) variants |= LongExposureVariants.MotionVariance;
            if (IncludeMotionBlur) variants |= LongExposureVariants.MotionBlur;
            return variants;
        }
    }

    public Task<LongExposureResult> GenerateAsync(string videoPath, IProgress<VideoAnalysisProgress> progress, CancellationToken ct)
        => LongExposureProcessor.GenerateAsync(videoPath, MotionBlurAlpha, SelectedVariants, progress, ct);
}
