using OpenCvSharp;

namespace RotationAnalysis.Core.VideoAnalysis;

public sealed class VideoAnalysisResult
{
    public required double ObservedPeriodSeconds { get; init; }
    public required double CenterX { get; init; }
    public required double CenterY { get; init; }
    public required int TrackCount { get; init; }
    public required double ConfidenceStdDevDegreesPerSecond { get; init; }
    public required string TimelapseImagePath { get; init; }
    public required double VideoFps { get; init; }
    public required Size FrameSize { get; init; }
}

/// <summary>Orchestrates the full video -> observed rotation period pipeline off the UI thread.</summary>
public static class VideoAnalyzer
{
    /// <summary>Minimum number of surviving star tracks required to trust the result.</summary>
    public const int MinUsableTracks = 20;

    public static Task<VideoAnalysisResult> AnalyzeAsync(
        string videoPath,
        string timelapseOutputDirectory,
        IProgress<VideoAnalysisProgress>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.Opening, 0, "Opening video"));
            var tracking = StarTracker.Track(videoPath, progress, ct);
            using var timelapse = tracking.Timelapse;

            ct.ThrowIfCancellationRequested();
            var usableTracks = tracking.Tracks.Where(t => t.Xs.Count >= 5).ToList();
            if (usableTracks.Count < MinUsableTracks)
            {
                throw new InvalidOperationException(
                    $"Only {usableTracks.Count} stars could be tracked reliably (need at least {MinUsableTracks}). Try a longer or clearer recording.");
            }

            progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.FittingCenter, 88, "Fitting rotation center"));
            var (cx, cy) = CircleFit.FitSharedCenter(usableTracks);

            ct.ThrowIfCancellationRequested();
            progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.SolvingRotation, 94, "Solving rotation period"));
            var rotation = RotationSolver.Solve(usableTracks, cx, cy, tracking.Fps);

            Directory.CreateDirectory(timelapseOutputDirectory);
            string timelapsePath = Path.Combine(timelapseOutputDirectory, $"timelapse_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png");
            TimelapseRenderer.Save(timelapse, timelapsePath);

            progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.Done, 100, "Done"));

            return new VideoAnalysisResult
            {
                ObservedPeriodSeconds = rotation.PeriodSeconds,
                CenterX = cx,
                CenterY = cy,
                TrackCount = rotation.UsedTrackCount,
                ConfidenceStdDevDegreesPerSecond = rotation.StdDevDegreesPerSecond,
                TimelapseImagePath = timelapsePath,
                VideoFps = tracking.Fps,
                FrameSize = tracking.FrameSize,
            };
        }, ct);
    }
}
