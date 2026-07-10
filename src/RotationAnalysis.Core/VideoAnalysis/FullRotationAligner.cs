using OpenCvSharp;

namespace RotationAnalysis.Core.VideoAnalysis;

/// <summary>One reference frame's alignment measurement, kept even when rejected - this is what
/// gets written verbatim into the calibration sidecar.</summary>
public sealed record ReferenceSampleLog(
    double TRefSeconds,
    double SearchWindowStartSeconds,
    double SearchWindowEndSeconds,
    double? TMatchSeconds,
    double? PeriodSampleSeconds,
    double MeanPeakStrength,
    double FitRSquared,
    bool WindowWidened,
    bool Accepted,
    string? RejectionReason);

/// <summary>A reference frame and its matched frame, as JPEG stills, for one accepted alignment
/// sample - "evidence" the measurement can show the user, not part of the calibration log.</summary>
public sealed record ReferenceMatchPreview(
    int ReferenceIndex,
    double TRefSeconds,
    double TMatchSeconds,
    double PeriodSampleSeconds,
    double MeanPeakStrength,
    double FitRSquared,
    byte[] ReferenceFrameJpeg,
    byte[] MatchedFrameJpeg);

public sealed class FullRotationAlignmentResult
{
    public required bool Success { get; init; }
    public double? MeasuredPeriodSeconds { get; init; }
    public double? MeasuredPeriodUncertaintySeconds { get; init; }
    public int SampleCount { get; init; }
    public required IReadOnlyList<ReferenceSampleLog> Samples { get; init; }
    public string? FailureReason { get; init; }
    public int LetterboxTopRows { get; init; }
    public int LetterboxBottomRows { get; init; }

    /// <summary>One entry per accepted reference sample - deliberately not filtered down to "the
    /// best" here, since that's a presentation choice for whoever displays these.</summary>
    public IReadOnlyList<ReferenceMatchPreview> Previews { get; init; } = Array.Empty<ReferenceMatchPreview>();
}

/// <summary>
/// Measures the rotation period directly by finding when the star field re-aligns with reference
/// frames taken near the start of the video - a timing measurement, independent of the FOV/focal
/// length calibration the rate-based <see cref="HorizontalRotationSolver"/> depends on. Only
/// meaningful for videos long enough to contain a full rotation (see the eligibility check in
/// <see cref="HorizontalVideoAnalyzer"/>); opens its own <see cref="VideoCapture"/> for a
/// dedicated pass rather than sharing the tracker's.
/// </summary>
public static class FullRotationAligner
{
    private const double MinPeakStrength = 0.05;
    private const double MinFitR2 = 0.8;
    private const double LetterboxRowMeanThreshold = 8.0;
    private const double MaxLetterboxFraction = 0.25;
    private const int MinSamplesPerWindow = 5;
    private const int MaxCandidateSamplesPerWindow = 150;
    private const int MinAcceptedSamples = 3;

    /// <summary>How often (in processed samples) <see cref="SampleWindow"/> reports progress -
    /// frequent enough that the UI visibly moves during a phase-correlation sweep, not so
    /// frequent it spams the progress channel.</summary>
    private const int SampleProgressInterval = 10;

    // This stage often dominates wall-clock time for long, eligible recordings (each reference
    // frame can sample up to MaxCandidateSamplesPerWindow candidates, each a full-frame phase
    // correlation) - give it a wide enough percent band that the bar visibly moves, not just the
    // message text.
    private const double AlignmentPercentStart = 98.0;
    private const double AlignmentPercentEnd = 99.8;

    public static FullRotationAlignmentResult Measure(
        string videoPath,
        double estimatedPeriodSeconds,
        double videoDurationSeconds,
        double rollDegrees,
        IProgress<VideoAnalysisProgress>? progress = null,
        CancellationToken ct = default,
        int referenceCount = 5,
        double referenceWindowFraction = 0.10,
        double minWindowFraction = 0.05,
        double minWindowSeconds = 10.0,
        double horizonFraction = 0.45)
    {
        using var capture = new VideoCapture(videoPath);
        if (!capture.IsOpened())
        {
            return Failed("Could not open video file for alignment measurement.");
        }

        double fps = capture.Fps > 0 ? capture.Fps : 30.0;
        int totalFrames = capture.FrameCount > 0 ? capture.FrameCount : int.MaxValue;
        var frameSize = new Size(capture.FrameWidth, capture.FrameHeight);
        double rollRadians = rollDegrees * Math.PI / 180.0;

        using var firstFrame = new Mat();
        if (!capture.Read(firstFrame) || firstFrame.Empty())
        {
            return Failed("Video has no frames.");
        }
        var (letterboxTop, letterboxBottom) = DetectLetterboxBars(firstFrame, frameSize.Height);
        capture.Set(VideoCaptureProperties.PosFrames, 0);

        double[] targetRefTimes = Enumerable.Range(0, referenceCount)
            .Select(i => referenceCount > 1 ? referenceWindowFraction * estimatedPeriodSeconds * i / (referenceCount - 1) : 0.0)
            .ToArray();

        var (refFrames, refTimes, refFrameJpegs) = CaptureReferenceFrames(capture, targetRefTimes, progress, ct);
        if (refFrames.Count < MinAcceptedSamples)
        {
            DisposeAll(refFrames);
            return Failed("Could not capture enough reference frames near the start of the video.");
        }

        var samplesLog = new List<ReferenceSampleLog>();
        var previews = new List<ReferenceMatchPreview>();
        var acceptedPeriods = new List<double>();
        Mat? weightWindow = null;
        try
        {
            weightWindow = BuildWeightWindow(frameSize, horizonFraction, letterboxTop, letterboxBottom);

            for (int i = 0; i < refFrames.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                int referenceIndex = i;
                int percent = (int)Math.Round(AlignmentPercentStart + (double)referenceIndex / refFrames.Count * (AlignmentPercentEnd - AlignmentPercentStart));
                progress?.Report(new VideoAnalysisProgress(
                    VideoAnalysisStage.SolvingRotation, percent,
                    $"Measuring full-rotation alignment (reference {referenceIndex + 1} of {refFrames.Count})"));

                var (log, preview) = MeasureReference(
                    capture, refFrames[i], refFrameJpegs[i], refTimes[i], weightWindow, fps, totalFrames,
                    videoDurationSeconds, estimatedPeriodSeconds, rollRadians,
                    minWindowFraction, minWindowSeconds, acceptedPeriods,
                    referenceIndex, refFrames.Count, progress, ct);
                samplesLog.Add(log);
                if (preview is not null)
                {
                    previews.Add(preview);
                }
            }
        }
        finally
        {
            weightWindow?.Dispose();
            DisposeAll(refFrames);
        }

        if (acceptedPeriods.Count < MinAcceptedSamples)
        {
            return new FullRotationAlignmentResult
            {
                Success = false,
                SampleCount = acceptedPeriods.Count,
                Samples = samplesLog,
                FailureReason = $"Only {acceptedPeriods.Count} of {samplesLog.Count} reference samples passed confidence gating (need >= {MinAcceptedSamples}).",
                LetterboxTopRows = letterboxTop,
                LetterboxBottomRows = letterboxBottom,
                Previews = previews,
            };
        }

        var (median, uncertainty) = FullRotationMath.Aggregate(acceptedPeriods);
        return new FullRotationAlignmentResult
        {
            Success = true,
            MeasuredPeriodSeconds = median,
            MeasuredPeriodUncertaintySeconds = uncertainty,
            SampleCount = acceptedPeriods.Count,
            Samples = samplesLog,
            LetterboxTopRows = letterboxTop,
            LetterboxBottomRows = letterboxBottom,
            Previews = previews,
        };
    }

    private static (ReferenceSampleLog Log, ReferenceMatchPreview? Preview) MeasureReference(
        VideoCapture capture, Mat referenceFloat, byte[] referenceFrameJpeg, double tRef, Mat weightWindow, double fps, int totalFrames,
        double videoDurationSeconds, double estimatedPeriodSeconds, double rollRadians,
        double minWindowFraction, double minWindowSeconds, List<double> acceptedPeriods,
        int referenceIndex, int totalReferences, IProgress<VideoAnalysisProgress>? progress, CancellationToken ct)
    {
        double baseWindow = Math.Max(minWindowFraction * estimatedPeriodSeconds, minWindowSeconds);
        double refPercentStart = AlignmentPercentStart + (double)referenceIndex / totalReferences * (AlignmentPercentEnd - AlignmentPercentStart);
        double refPercentEnd = AlignmentPercentStart + (double)(referenceIndex + 1) / totalReferences * (AlignmentPercentEnd - AlignmentPercentStart);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            double w = attempt == 0 ? baseWindow : baseWindow * 2;
            double windowStart = Math.Max(0, tRef + estimatedPeriodSeconds - w);
            double windowEnd = Math.Min(videoDurationSeconds, tRef + estimatedPeriodSeconds + w);
            bool widened = attempt > 0;

            if (widened)
            {
                progress?.Report(new VideoAnalysisProgress(
                    VideoAnalysisStage.SolvingRotation, (int)Math.Round(refPercentStart),
                    $"Measuring full-rotation alignment (reference {referenceIndex + 1} of {totalReferences}, " +
                    "zero crossing was at the window edge - widening search window and retrying)"));
            }

            void ReportSampleProgress(int samplesDone, int maxSamples, byte[]? previewBytes)
            {
                // The stride that decides which frames get sampled is only sized to *target*
                // maxSamples across the window (from a nominal-fps estimate of its frame span) -
                // real container timing can still land a few more candidates than that, so clamp
                // what's shown rather than let the counter read e.g. "200 of 150".
                int clampedSamplesDone = Math.Min(samplesDone, maxSamples);
                double withinRefFraction = Math.Min(1.0, (double)samplesDone / maxSamples);
                int percent = (int)Math.Round(refPercentStart + withinRefFraction * (refPercentEnd - refPercentStart));
                string widenNote = widened ? ", widened window" : "";
                progress?.Report(new VideoAnalysisProgress(
                    VideoAnalysisStage.SolvingRotation, percent,
                    $"Measuring full-rotation alignment (reference {referenceIndex + 1} of {totalReferences}{widenNote}, " +
                    $"sample {clampedSamplesDone} of {maxSamples})",
                    FramesProcessed: clampedSamplesDone, TotalFrames: maxSamples, PreviewImageBytes: previewBytes));
            }

            var samples = SampleWindow(capture, referenceFloat, weightWindow, fps, totalFrames, windowStart, windowEnd, rollRadians, MaxCandidateSamplesPerWindow, ReportSampleProgress, ct);
            double meanPeak = samples.Count > 0 ? samples.Average(s => s.Peak) : 0;

            if (samples.Count < MinSamplesPerWindow)
            {
                return (new ReferenceSampleLog(tRef, windowStart, windowEnd, null, null, meanPeak, 0, widened, false,
                    "Not enough candidate frames sampled in the search window."), null);
            }

            var times = samples.Select(s => s.T).ToList();
            var offsets = samples.Select(s => s.Offset).ToList();
            var (tMatch, r2, atEdge) = FullRotationMath.FitZeroCrossing(times, offsets);

            if (atEdge && attempt == 0)
            {
                continue; // widen once and retry this reference
            }

            if (double.IsNaN(tMatch))
            {
                return (new ReferenceSampleLog(tRef, windowStart, windowEnd, null, null, meanPeak, r2, widened, false,
                    atEdge
                        ? "Zero crossing at window edge - video appears shorter than one rotation."
                        : "No zero crossing found in the search window."), null);
            }

            if (meanPeak < MinPeakStrength)
            {
                return (new ReferenceSampleLog(tRef, windowStart, windowEnd, tMatch, tMatch - tRef, meanPeak, r2, widened, false,
                    $"Correlation peak strength too low ({meanPeak:F3})."), null);
            }

            if (r2 < MinFitR2)
            {
                return (new ReferenceSampleLog(tRef, windowStart, windowEnd, tMatch, tMatch - tRef, meanPeak, r2, widened, false,
                    $"Zero-crossing fit quality too low (R{'²'}={r2:F2})."), null);
            }

            double periodSample = tMatch - tRef;
            acceptedPeriods.Add(periodSample);

            ReferenceMatchPreview? preview = null;
            byte[]? matchedFrameJpeg = CaptureFrameNear(capture, tMatch, fps, totalFrames);
            if (matchedFrameJpeg is not null)
            {
                preview = new ReferenceMatchPreview(
                    referenceIndex, tRef, tMatch, periodSample, meanPeak, r2, referenceFrameJpeg, matchedFrameJpeg);
            }

            return (new ReferenceSampleLog(tRef, windowStart, windowEnd, tMatch, periodSample, meanPeak, r2, widened, true, null), preview);
        }

        // Widened once already and still landed on an edge.
        double finalWindow = baseWindow * 2;
        double finalStart = Math.Max(0, tRef + estimatedPeriodSeconds - finalWindow);
        double finalEnd = Math.Min(videoDurationSeconds, tRef + estimatedPeriodSeconds + finalWindow);
        return (new ReferenceSampleLog(tRef, finalStart, finalEnd, null, null, 0, 0, true, false,
            "Zero crossing at window edge after widening - video appears shorter than one rotation."), null);
    }

    private static (List<Mat> RefFrames, List<double> RefTimes, List<byte[]> RefFrameJpegs) CaptureReferenceFrames(
        VideoCapture capture, IReadOnlyList<double> targetTimes, IProgress<VideoAnalysisProgress>? progress, CancellationToken ct)
    {
        var refFrames = new List<Mat>();
        var refTimes = new List<double>();
        var refFrameJpegs = new List<byte[]>();
        int nextTargetIndex = 0;

        using var frame = new Mat();
        while (nextTargetIndex < targetTimes.Count && capture.Grab())
        {
            ct.ThrowIfCancellationRequested();
            double t = capture.Get(VideoCaptureProperties.PosMsec) / 1000.0;
            if (t + 1e-9 < targetTimes[nextTargetIndex])
            {
                continue;
            }

            capture.Retrieve(frame);
            refFrames.Add(PreprocessFrame(frame));
            refTimes.Add(t);

            Cv2.ImEncode(".jpg", frame, out var jpegBytes);
            refFrameJpegs.Add(jpegBytes);
            nextTargetIndex++;

            progress?.Report(new VideoAnalysisProgress(
                VideoAnalysisStage.SolvingRotation, (int)AlignmentPercentStart,
                $"Capturing full-rotation reference frame {nextTargetIndex} of {targetTimes.Count}",
                FramesProcessed: nextTargetIndex, TotalFrames: targetTimes.Count, PreviewImageBytes: jpegBytes));
        }

        return (refFrames, refTimes, refFrameJpegs);
    }

    /// <summary>Grabs a single frame near <paramref name="targetSeconds"/> purely for display -
    /// the seek doesn't need to be frame-exact, since this is just a thumbnail of "roughly what the
    /// sky looked like at the interpolated crossing," not part of the measurement itself.</summary>
    private static byte[]? CaptureFrameNear(VideoCapture capture, double targetSeconds, double fps, int totalFrames)
    {
        int seekFrame = (int)Math.Clamp(Math.Round(targetSeconds * fps), 0, Math.Max(0, totalFrames - 1));
        capture.Set(VideoCaptureProperties.PosFrames, seekFrame);

        using var frame = new Mat();
        if (!capture.Read(frame) || frame.Empty())
        {
            return null;
        }

        Cv2.ImEncode(".jpg", frame, out var bytes);
        return bytes;
    }

    private static List<(double T, double Offset, double Peak)> SampleWindow(
        VideoCapture capture, Mat referenceFloat, Mat weightWindow, double fps, int totalFrames,
        double windowStart, double windowEnd, double rollRadians, int maxSamples,
        Action<int, int, byte[]?>? onSample, CancellationToken ct)
    {
        var samples = new List<(double, double, double)>();

        int seekFrame = (int)Math.Clamp(Math.Round(windowStart * fps), 0, Math.Max(0, totalFrames - 1));
        capture.Set(VideoCaptureProperties.PosFrames, seekFrame);

        double windowSpanSeconds = Math.Max(1e-6, windowEnd - windowStart);
        int approxFramesInWindow = Math.Max(1, (int)Math.Round(windowSpanSeconds * fps));
        int stride = Math.Max(1, approxFramesInWindow / maxSamples);

        using var frame = new Mat();
        int framesSinceStart = 0;
        while (capture.Grab())
        {
            ct.ThrowIfCancellationRequested();
            double t = capture.Get(VideoCaptureProperties.PosMsec) / 1000.0;
            if (t < windowStart)
            {
                continue;
            }
            if (t > windowEnd)
            {
                break;
            }

            if (framesSinceStart % stride == 0)
            {
                capture.Retrieve(frame);
                using var candidate = PreprocessFrame(frame);
                var shift = Cv2.PhaseCorrelate(referenceFloat, candidate, weightWindow, out double response);
                double offset = shift.X * Math.Cos(rollRadians) + shift.Y * Math.Sin(rollRadians);
                samples.Add((t, offset, response));

                if (samples.Count % SampleProgressInterval == 0)
                {
                    byte[]? previewBytes = null;
                    if (onSample is not null)
                    {
                        Cv2.ImEncode(".jpg", frame, out previewBytes);
                    }
                    onSample?.Invoke(samples.Count, maxSamples, previewBytes);
                }
            }
            framesSinceStart++;
        }

        onSample?.Invoke(samples.Count, maxSamples, null);
        return samples;
    }

    /// <summary>Grayscale, normalized to [0,1] float32 - the format <c>Cv2.PhaseCorrelate</c> expects.</summary>
    private static Mat PreprocessFrame(Mat bgrFrame)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgrFrame, gray, ColorConversionCodes.BGR2GRAY);
        var floatFrame = new Mat();
        gray.ConvertTo(floatFrame, MatType.CV_32FC1, 1.0 / 255.0);
        return floatFrame;
    }

    /// <summary>
    /// Per-pixel weighting passed as <c>Cv2.PhaseCorrelate</c>'s window argument: zero below the
    /// horizon and over any detected letterbox bars (so only the star field participates), a
    /// Hanning taper everywhere else to suppress edge artifacts in the FFT-based correlation.
    /// </summary>
    private static Mat BuildWeightWindow(Size frameSize, double horizonFraction, int letterboxTop, int letterboxBottom)
    {
        using var mask = new Mat(frameSize, MatType.CV_32FC1, Scalar.All(0));
        int top = letterboxTop;
        int horizonCutoff = (int)(frameSize.Height * horizonFraction);
        int bottom = Math.Min(frameSize.Height - letterboxBottom, horizonCutoff);

        if (bottom > top)
        {
            using var roi = new Mat(mask, new Rect(0, top, frameSize.Width, bottom - top));
            roi.SetTo(Scalar.All(1.0));
        }

        using var hann = new Mat();
        Cv2.CreateHanningWindow(hann, frameSize, MatType.CV_32FC1);

        var window = new Mat();
        Cv2.Multiply(mask, hann, window);
        return window;
    }

    /// <summary>Detects near-black rows from the top/bottom edges inward (a letterbar overlay
    /// baked into the raw capture), so they can be excluded from star-field matching.</summary>
    private static (int Top, int Bottom) DetectLetterboxBars(Mat bgrFrame, int frameHeight)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgrFrame, gray, ColorConversionCodes.BGR2GRAY);
        int maxBars = (int)(frameHeight * MaxLetterboxFraction);

        int top = 0;
        for (int y = 0; y < maxBars; y++)
        {
            using var row = gray.Row(y);
            if (Cv2.Mean(row).Val0 > LetterboxRowMeanThreshold)
            {
                break;
            }
            top++;
        }

        int bottom = 0;
        for (int y = frameHeight - 1; y >= frameHeight - maxBars; y--)
        {
            using var row = gray.Row(y);
            if (Cv2.Mean(row).Val0 > LetterboxRowMeanThreshold)
            {
                break;
            }
            bottom++;
        }

        if (top + bottom >= frameHeight * 0.9)
        {
            // Degenerate (e.g. an all-dark frame) - don't let it swallow the whole mask.
            return (0, 0);
        }

        return (top, bottom);
    }

    private static void DisposeAll(List<Mat> mats)
    {
        foreach (var mat in mats)
        {
            mat.Dispose();
        }
    }

    private static FullRotationAlignmentResult Failed(string reason) => new()
    {
        Success = false,
        SampleCount = 0,
        Samples = Array.Empty<ReferenceSampleLog>(),
        FailureReason = reason,
    };
}
