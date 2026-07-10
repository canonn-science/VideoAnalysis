using OpenCvSharp;

namespace RotationAnalysis.Core.VideoAnalysis;

public sealed class HorizontalVideoAnalysisResult
{
    public required double ObservedPeriodSeconds { get; init; }
    public required double ConfidencePercent { get; init; }
    public required double MedianRollDegrees { get; init; }
    public required int ChunksUsed { get; init; }
    public required int ChunksAvailable { get; init; }
    public required double VideoFps { get; init; }
    public required Size FrameSize { get; init; }

    /// <summary>Full-rotation period measured from start/end frame re-alignment - only populated
    /// for videos long enough to contain a full rotation (see <see cref="HorizontalVideoAnalyzer"/>'s
    /// eligibility check) where the measurement also succeeded.</summary>
    public double? MeasuredPeriodSeconds { get; init; }
    public double? MeasuredPeriodErrSeconds { get; init; }
    public int? NReferenceSamples { get; init; }

    /// <summary>(observed - measured) / measured * 100.</summary>
    public double? RateVsMeasuredPctDiff { get; init; }

    /// <summary>True when the rate-based and full-rotation measurements disagree by more than 3x
    /// their combined uncertainty - with steady rotation that indicates a pipeline/calibration
    /// problem (FOV, capture settings), not physics.</summary>
    public bool ConsistencyWarning { get; init; }

    /// <summary>One reference/matched-frame image pair per accepted alignment sample - "evidence"
    /// for the results dialog. Empty unless the alignment measurement ran and produced at least
    /// one accepted sample.</summary>
    public IReadOnlyList<ReferenceMatchPreview> AlignmentPreviews { get; init; } = Array.Empty<ReferenceMatchPreview>();

    /// <summary>True if the video cleared the eligibility bar and the full-rotation measurement
    /// was actually attempted - lets the results dialog distinguish "not attempted" (video too
    /// short) from "attempted but failed" when <see cref="MeasuredPeriodSeconds"/> is null.</summary>
    public bool AlignmentAttempted { get; init; }

    /// <summary>Why the alignment measurement didn't produce a result, when it was attempted but
    /// unsuccessful. Null when it wasn't attempted, or when it succeeded.</summary>
    public string? AlignmentFailureReason { get; init; }
}

/// <summary>
/// Orchestrates the horizon-facing video -> observed rotation period pipeline off the UI
/// thread: chunked tracking, then a fixed-vertical-axis fit per chunk, combined into a single
/// estimate with a confidence score based on how well the chunks agree with each other.
/// </summary>
public static class HorizontalVideoAnalyzer
{
    private const int MinTracksPerChunk = 20;
    private const double DefaultSeedPeriodSeconds = 600.0;

    /// <summary>Videos must be at least this many times the estimated period to attempt the
    /// full-rotation alignment measurement - the 20% margin keeps the re-alignment event inside
    /// the video even if the estimate is off by up to ~20%.</summary>
    private const double EligibilityDurationMultiple = 1.2;

    /// <summary>Below this rate-based confidence, prefer the Kepler estimate over the rate-based
    /// period when sizing the alignment search window.</summary>
    private const double LowRateConfidenceFloorPercent = 20.0;

    /// <summary>Rate-based vs Kepler disagreement beyond this fraction widens the alignment
    /// search window (the rate-based value is still used as T_est, per the issue).</summary>
    private const double RateKeplerDisagreementFraction = 0.5;

    private const double ConsistencyWarningSigma = 3.0;

    /// <summary>
    /// One-sample calibration correction for the rate-based method's systematic bias, derived from
    /// the single full-rotation ground-truth measurement collected so far (Eorl Scrua AA-A h670 2 A
    /// Ring: rate-based fit came out 530.9561807430355s, the full-rotation measurement 505.89471843069344s
    /// - the rate method overestimated by ~4.95%). Applied to every rate-based result from here on,
    /// not just videos where a full-rotation measurement also runs, so the correction benefits every
    /// analysis. This is a rough starting point, not a fitted model - as more calibration data accumulates
    /// (see the calibration folder), future eligible videos' rate-vs-measured comparison shows whether
    /// this correction still holds for a different ring or the two drift apart again, which is exactly
    /// the signal needed to refine it (or replace it with something that depends on angular sweep/star
    /// count/etc., per the calibration log's fields) instead of one flat factor.
    /// </summary>
    private const double RateBasedBiasCorrectionFactor = 505.89471843069344 / 530.9561807430355;

    public static Task<HorizontalVideoAnalysisResult> AnalyzeAsync(
        string videoPath,
        double? seedPeriodSeconds = null,
        IProgress<VideoAnalysisProgress>? progress = null,
        CancellationToken ct = default,
        string? systemName = null,
        string? ringName = null)
    {
        return Task.Run(() =>
        {
            progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.Opening, 0, "Opening video"));
            var tracking = HorizontalStarTracker.Track(videoPath, progress, ct);

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
            var chunkDurationsSeconds = new List<double>();
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
                    chunkDurationsSeconds.Add(ChunkDurationSeconds(tracking.Chunks[i], tracking.Fps));
                    runningSeedPeriod = result.Period; // warm-start the next chunk with this one's answer
                }
            }

            if (chunkResults.Count == 0)
            {
                throw new InvalidOperationException("None of the tracked star chunks produced a reliable fit. Try a longer or clearer recording.");
            }

            var periods = chunkResults.Select(r => r.Period).OrderBy(p => p).ToList();
            double median = Median(periods); // raw rate-based fit - kept as-is for the calibration log
            double confidence = ComputeConfidence(chunkResults, median);
            double rateUncertaintySeconds = ComputeRateUncertaintySeconds(chunkResults, median); // raw scale, matches median
            double medianRoll = Median(chunkResults.Select(r => r.RollDegrees).OrderBy(r => r).ToList());

            // The reported/compared-against value: the rate-based fit corrected by the one-sample
            // bias factor above. Everything downstream (eligibility sizing, the reported "observed
            // rotation", and the rate-vs-measured comparison) uses this corrected value - the raw
            // median only survives for the calibration log, which needs the uncorrected fit to stay
            // useful as ground-truth-vs-raw-method data.
            double correctedMedian = median * RateBasedBiasCorrectionFactor;
            double correctedRateUncertaintySeconds = rateUncertaintySeconds * RateBasedBiasCorrectionFactor;

            double? kepler = seedPeriodSeconds is > 0 ? seedPeriodSeconds : null;
            bool rateReliable = confidence >= LowRateConfidenceFloorPercent;
            double tEst = rateReliable || kepler is not double k0 ? correctedMedian : k0;
            bool rateKeplerDisagreement = kepler is double k1 && Math.Abs(correctedMedian - k1) / k1 > RateKeplerDisagreementFraction;

            bool eligible = tracking.DurationSeconds >= EligibilityDurationMultiple * tEst;

            FullRotationAlignmentResult? alignment = null;
            double? measuredPeriod = null, measuredPeriodErr = null, rateVsMeasuredPctDiff = null;
            int? nReferenceSamples = null;
            bool consistencyWarning = false;

            if (eligible)
            {
                progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.SolvingRotation, 98, "Measuring full-rotation alignment - this can take a while on long recordings"));
                double windowWiden = rateKeplerDisagreement ? 2.0 : 1.0;
                alignment = FullRotationAligner.Measure(
                    videoPath, tEst, tracking.DurationSeconds, medianRoll, progress, ct,
                    minWindowFraction: 0.05 * windowWiden, minWindowSeconds: 10.0 * windowWiden);

                if (alignment.Success)
                {
                    measuredPeriod = alignment.MeasuredPeriodSeconds;
                    measuredPeriodErr = alignment.MeasuredPeriodUncertaintySeconds;
                    nReferenceSamples = alignment.SampleCount;
                    rateVsMeasuredPctDiff = (correctedMedian - measuredPeriod!.Value) / measuredPeriod.Value * 100.0;

                    double combinedUncertainty = Math.Sqrt(
                        correctedRateUncertaintySeconds * correctedRateUncertaintySeconds + measuredPeriodErr!.Value * measuredPeriodErr.Value);
                    consistencyWarning = !double.IsNaN(correctedRateUncertaintySeconds)
                        && Math.Abs(correctedMedian - measuredPeriod.Value) > ConsistencyWarningSigma * combinedUncertainty;
                }

                WriteCalibrationLog(
                    videoPath, tracking, chunkResults, chunkDurationsSeconds, median, confidence, rateUncertaintySeconds,
                    kepler, systemName, ringName, rateKeplerDisagreement, alignment, progress, ct);
            }

            progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.Done, 100, "Done"));

            return new HorizontalVideoAnalysisResult
            {
                ObservedPeriodSeconds = correctedMedian,
                ConfidencePercent = confidence,
                MedianRollDegrees = medianRoll,
                ChunksUsed = chunkResults.Count,
                ChunksAvailable = tracking.Chunks.Count,
                VideoFps = tracking.Fps,
                FrameSize = tracking.FrameSize,
                MeasuredPeriodSeconds = measuredPeriod,
                MeasuredPeriodErrSeconds = measuredPeriodErr,
                NReferenceSamples = nReferenceSamples,
                RateVsMeasuredPctDiff = rateVsMeasuredPctDiff,
                ConsistencyWarning = consistencyWarning,
                AlignmentPreviews = alignment?.Previews ?? Array.Empty<ReferenceMatchPreview>(),
                AlignmentAttempted = alignment is not null,
                AlignmentFailureReason = alignment is { Success: false } ? alignment.FailureReason : null,
            };
        }, ct);
    }

    private const double TimestampProfilePercentStart = 99.8;
    private const double TimestampProfilePercentEnd = 99.95;

    private static void WriteCalibrationLog(
        string videoPath, HorizontalTrackingResult tracking, List<HorizontalChunkResult> chunkResults,
        List<double> chunkDurationsSeconds, double median, double confidence, double rateUncertaintySeconds,
        double? kepler, string? systemName, string? ringName, bool rateKeplerDisagreement,
        FullRotationAlignmentResult? alignment, IProgress<VideoAnalysisProgress>? progress, CancellationToken ct)
    {
        try
        {
            void ReportProfileProgress(int framesDone, int totalFrames, byte[]? previewBytes)
            {
                double fraction = Math.Min(1.0, (double)framesDone / totalFrames);
                int percent = (int)Math.Round(TimestampProfilePercentStart + fraction * (TimestampProfilePercentEnd - TimestampProfilePercentStart));
                progress?.Report(new VideoAnalysisProgress(
                    VideoAnalysisStage.SolvingRotation, percent,
                    $"Profiling frame timing for the calibration log ({framesDone} of {totalFrames})",
                    FramesProcessed: framesDone, TotalFrames: totalFrames, PreviewImageBytes: previewBytes));
            }

            var timestampProfile = VideoTimestampProfiler.Profile(videoPath, tracking.Fps, ReportProfileProgress, ct);
            var angularSweeps = chunkResults.Zip(chunkDurationsSeconds,
                (result, duration) => 2 * Math.PI / result.Period * duration * 180.0 / Math.PI).ToList();

            var log = new RotationCalibrationLog
            {
                Video = new VideoMetadataLog(
                    RotationCalibrationLogWriter.HashFilename(videoPath),
                    tracking.FrameSize.Width, tracking.FrameSize.Height, tracking.Fps, tracking.DurationSeconds,
                    timestampProfile.MeanFrameIntervalSeconds, timestampProfile.StdFrameIntervalSeconds,
                    timestampProfile.DroppedFrameCount),
                Geometry = new CaptureGeometryLog(
                    HorizontalRotationSolver.DefaultSeedFocalLengthPx, chunkResults[^1].F,
                    "fixed vertical (roll-corrected)", alignment?.LetterboxTopRows ?? 0, alignment?.LetterboxBottomRows ?? 0),
                Ring = new RingMetadataLog(systemName ?? string.Empty, ringName ?? string.Empty, kepler),
                RateBased = new RateBasedInternalsLog(
                    tracking.Chunks.Count, chunkResults.Count,
                    chunkResults.Select(r => r.Period).ToList(),
                    chunkResults.Select(r => r.TracksUsed).ToList(),
                    angularSweeps,
                    median, confidence, rateUncertaintySeconds, RateBasedBiasCorrectionFactor),
                Alignment = new AlignmentInternalsLog(
                    rateKeplerDisagreement, alignment?.Samples ?? Array.Empty<ReferenceSampleLog>()),
                Aggregate = new AlignmentAggregateLog(
                    alignment?.Success ?? false, alignment?.MeasuredPeriodSeconds, alignment?.MeasuredPeriodUncertaintySeconds,
                    alignment?.SampleCount ?? 0, alignment?.FailureReason),
                Derived = new DerivedCalibrationLog(
                    alignment is { Success: true, MeasuredPeriodSeconds: double measured }
                        ? (median - measured) / measured * 100.0
                        : null),
            };

            RotationCalibrationLogWriter.Write(videoPath, log);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Calibration logging is best-effort - never let it fail the actual measurement.
        }
    }

    /// <summary>Duration a chunk's tracking actually spanned, from the furthest frame index any of
    /// its tracks reached - used only to log an approximate angular sweep for calibration.</summary>
    private static double ChunkDurationSeconds(HorizontalChunk chunk, double fps)
    {
        if (chunk.Tracks.Count == 0)
        {
            return 0;
        }
        int maxFrameIndex = chunk.Tracks.Max(t => t.FrameIndices.Count > 0 ? t.FrameIndices[^1] : 0);
        return maxFrameIndex / fps;
    }

    /// <summary>
    /// Uncertainty (in seconds) behind <see cref="ComputeConfidence"/>'s heuristic, so the
    /// consistency check against the full-rotation measurement has something concrete to compare:
    /// std dev across chunk periods (2+ chunks), or the single chunk's own-track spread scaled to
    /// seconds (1 chunk).
    /// </summary>
    private static double ComputeRateUncertaintySeconds(List<HorizontalChunkResult> chunkResults, double median)
    {
        if (chunkResults.Count >= 2)
        {
            var periods = chunkResults.Select(r => r.Period).ToList();
            double mean = periods.Average();
            double variance = periods.Sum(p => (p - mean) * (p - mean)) / (periods.Count - 1);
            return Math.Sqrt(variance);
        }

        var only = chunkResults[0];
        if (double.IsNaN(only.MedianOwnPeriod) || only.Period <= 0)
        {
            return double.NaN;
        }
        return Math.Abs(only.MedianOwnPeriod - only.Period);
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
