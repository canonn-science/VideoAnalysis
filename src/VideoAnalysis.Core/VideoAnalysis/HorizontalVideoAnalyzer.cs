using OpenCvSharp;

namespace VideoAnalysis.Core.VideoAnalysis;

public sealed class HorizontalVideoAnalysisResult
{
    public required double ObservedPeriodSeconds { get; init; }
    public required double ConfidencePercent { get; init; }
    public required double MedianRollDegrees { get; init; }
    public required int ChunksUsed { get; init; }
    public required int ChunksAvailable { get; init; }
    public required double VideoFps { get; init; }
    public required Size FrameSize { get; init; }
}

/// <summary>
/// Orchestrates the horizon-facing video -> observed rotation period pipeline off the UI
/// thread: chunked tracking, then a fixed-vertical-axis fit per chunk, combined into a single
/// estimate with a confidence score based on how well the chunks agree with each other.
/// </summary>
public static class HorizontalVideoAnalyzer
{
    private const int MinTracksPerChunk = 20;

    /// <summary>Used both as the solver's warm-start guess and, via
    /// <see cref="HorizontalStarTracker"/>'s duration gate, as the assumed period when no
    /// Kepler/body estimate is available at all - a video needs to be at least this "typical"
    /// long before a rotation reading is attempted blind.</summary>
    internal const double DefaultSeedPeriodSeconds = 600.0;

    public static Task<HorizontalVideoAnalysisResult> AnalyzeAsync(
        string videoPath,
        double? seedPeriodSeconds = null,
        IProgress<VideoAnalysisProgress>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.Opening, 0, "Opening video"));
            var tracking = HorizontalStarTracker.Track(videoPath, progress, ct, estimatedPeriodSeconds: seedPeriodSeconds);

            ct.ThrowIfCancellationRequested();
            if (tracking.Chunks.Count == 0)
            {
                throw new InvalidOperationException(
                    "Could not track any stars in this video. Make sure the recording shows a clear starfield above the horizon, framed as illustrated.");
            }

            const double seedF = HorizontalRotationSolver.DefaultSeedFocalLengthPx;
            double runningSeedPeriod = seedPeriodSeconds is > 0 ? seedPeriodSeconds.Value : DefaultSeedPeriodSeconds;

            int totalChunks = tracking.Chunks.Count;
            var chunkResults = new List<HorizontalChunkResult>();
            for (int i = 0; i < totalChunks; i++)
            {
                ct.ThrowIfCancellationRequested();
                int chunkIndex = i;
                double chunkPercentStart = 90 + (chunkIndex * 8.0 / totalChunks); // 90 -> 98 across the solving phase
                double chunkPercentEnd = 90 + ((chunkIndex + 1) * 8.0 / totalChunks);
                void ReportChunkProgress(double fraction)
                {
                    int percent = (int)Math.Round(chunkPercentStart + fraction * (chunkPercentEnd - chunkPercentStart));
                    progress?.Report(new VideoAnalysisProgress(
                        VideoAnalysisStage.SolvingRotation, percent,
                        $"Solving rotation period (segment {chunkIndex + 1} of {totalChunks}, {fraction * 100:F0}%)"));
                }
                ReportChunkProgress(0);

                var result = HorizontalRotationSolver.Solve(
                    tracking.Chunks[i].Tracks, tracking.Fps, tracking.FrameSize.Width, tracking.FrameSize.Height, seedF, runningSeedPeriod,
                    ct: ct, onProgress: ReportChunkProgress);
                if (result.TracksUsed >= MinTracksPerChunk && !double.IsNaN(result.Period) && !double.IsInfinity(result.Period))
                {
                    chunkResults.Add(result);
                    runningSeedPeriod = result.Period; // warm-start the next chunk with this one's answer
                }
            }

            if (chunkResults.Count == 0)
            {
                throw new InvalidOperationException("None of the tracked star chunks produced a reliable fit. Try a longer or clearer recording.");
            }

            var periods = chunkResults.Select(r => r.Period).OrderBy(p => p).ToList();
            double median = Median(periods);
            double confidence = ComputeConfidence(chunkResults, median);
            double medianRoll = Median(chunkResults.Select(r => r.RollDegrees).OrderBy(r => r).ToList());

            progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.Done, 100, "Done"));

            return new HorizontalVideoAnalysisResult
            {
                ObservedPeriodSeconds = median,
                ConfidencePercent = confidence,
                MedianRollDegrees = medianRoll,
                ChunksUsed = chunkResults.Count,
                ChunksAvailable = tracking.Chunks.Count,
                VideoFps = tracking.Fps,
                FrameSize = tracking.FrameSize,
            };
        }, ct);
    }

    /// <summary>
    /// Confidence is based on how tightly independent measurements agree, not on any absolute
    /// ground truth (there isn't one at runtime) - a heuristic, not a statistical guarantee.
    /// Multiple chunks: relative spread across chunks (each is an independent measurement of
    /// the same physical rotation, since Elite's rings rotate as a rigid disk - one true rate
    /// everywhere). Single chunk: relative spread across that chunk's own tracked stars.
    /// </summary>
    private static double ComputeConfidence(List<HorizontalChunkResult> chunkResults, double median)
    {
        const double zeroConfidenceAtRelativeSpread = 0.05;

        if (chunkResults.Count >= 2)
        {
            var periods = chunkResults.Select(r => r.Period).ToList();
            double mean = periods.Average();
            double variance = periods.Sum(p => (p - mean) * (p - mean)) / (periods.Count - 1);
            double stdDev = Math.Sqrt(variance);
            double cv = stdDev / median;
            return Math.Clamp(100.0 * (1.0 - cv / zeroConfidenceAtRelativeSpread), 0, 100);
        }

        var only = chunkResults[0];
        if (double.IsNaN(only.MedianOwnPeriod) || only.Period <= 0)
        {
            return 0;
        }
        double relDiff = Math.Abs(only.MedianOwnPeriod - only.Period) / only.Period;
        return Math.Clamp(100.0 * (1.0 - relDiff / zeroConfidenceAtRelativeSpread), 0, 100);
    }

    private static double Median(List<double> sorted)
    {
        int n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }
}
