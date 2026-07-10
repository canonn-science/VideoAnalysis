using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RotationAnalysis.Core.VideoAnalysis;

public sealed record VideoMetadataLog(
    string FilenameHash, int Width, int Height, double ContainerFps, double DurationSeconds,
    double MeanFrameIntervalSeconds, double StdFrameIntervalSeconds, int DroppedFrameCount);

public sealed record CaptureGeometryLog(
    double AssumedSeedFocalLengthPx, double FittedFocalLengthPx, string AxisAssumption,
    int LetterboxTopRows, int LetterboxBottomRows);

public sealed record RingMetadataLog(string SystemName, string RingName, double? KeplerPeriodSeconds);

public sealed record RateBasedInternalsLog(
    int ChunksAvailable, int ChunksUsed, IReadOnlyList<double> ChunkPeriodsSeconds,
    IReadOnlyList<int> ChunkTracksUsed, IReadOnlyList<double> ChunkAngularSweepDegrees,
    double ResultingPeriodSeconds, double ConfidencePercent, double UncertaintySeconds,
    double CorrectionFactorApplied);

public sealed record AlignmentInternalsLog(
    bool RateKeplerDisagreement, IReadOnlyList<ReferenceSampleLog> Samples);

public sealed record AlignmentAggregateLog(
    bool Success, double? MeasuredPeriodSeconds, double? MeasuredPeriodUncertaintySeconds,
    int SampleCount, string? FailureReason);

public sealed record DerivedCalibrationLog(double? RateErrorPct);

/// <summary>
/// Per-video calibration dataset row: everything needed to later fit the rate-based method's
/// error model against the full-rotation measurement as ground truth. Written for every eligible
/// video (duration >= 1.2x estimated period), including videos where the alignment measurement
/// itself failed - failed cases are calibration data too.
///
/// <see cref="RateBasedInternalsLog.ResultingPeriodSeconds"/> is the raw, uncorrected rate-based
/// fit (not what the app reports as "observed rotation" - see
/// <see cref="RateBasedInternalsLog.CorrectionFactorApplied"/> and
/// <c>HorizontalVideoAnalyzer.RateBasedBiasCorrectionFactor</c>), so this dataset stays usable for
/// refitting the correction itself as more samples accumulate, rather than calibrating against an
/// already-corrected number.
/// </summary>
public sealed class RotationCalibrationLog
{
    public required VideoMetadataLog Video { get; init; }
    public required CaptureGeometryLog Geometry { get; init; }
    public required RingMetadataLog Ring { get; init; }
    public required RateBasedInternalsLog RateBased { get; init; }
    public required AlignmentInternalsLog Alignment { get; init; }
    public required AlignmentAggregateLog Aggregate { get; init; }
    public required DerivedCalibrationLog Derived { get; init; }
}

/// <summary>
/// Writes the calibration log to <c>%LocalAppData%\RotationAnalysisLab\calibration\</c>, alongside
/// the app's other local data (<c>measurements.csv</c>, <c>settings.json</c>) rather than next to
/// the video itself - the video can live anywhere (a large removable drive, a temp folder that
/// gets cleaned up, etc.), but the calibration dataset should stay put and stay findable.
/// </summary>
public static class RotationCalibrationLogWriter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string CalibrationDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RotationAnalysisLab", "calibration");

    /// <summary>Keyed by the video's filename rather than its full path, so a rename (see
    /// <c>VideoProcessingWindow</c>) can move just this one file within the calibration folder
    /// instead of needing to track wherever the video's directory happens to be.</summary>
    public static string PathFor(string videoPath) =>
        Path.Combine(CalibrationDirectory, Path.GetFileName(videoPath) + ".calibration.json");

    public static void Write(string videoPath, RotationCalibrationLog log)
    {
        Directory.CreateDirectory(CalibrationDirectory);
        File.WriteAllText(PathFor(videoPath), JsonSerializer.Serialize(log, Options));
    }

    /// <summary>Short, non-reversible stand-in for the raw filename in the log (avoids writing a
    /// commander's file-naming habits verbatim into calibration data shared for analysis).</summary>
    public static string HashFilename(string videoPath)
    {
        var bytes = Encoding.UTF8.GetBytes(Path.GetFileName(videoPath));
        return Convert.ToHexString(SHA256.HashData(bytes))[..16];
    }
}
