using OpenCvSharp;
using VideoAnalysis.Core.Domain;

namespace VideoAnalysis.Core.VideoAnalysis;

public sealed class HorizontalChunk
{
    public required List<StarTrack> Tracks { get; init; }
}

public sealed class HorizontalTrackingResult
{
    public required List<HorizontalChunk> Chunks { get; init; }
    public required double Fps { get; init; }
    public required Size FrameSize { get; init; }
}

/// <summary>
/// Tracking for horizon-facing recordings (ship parked on the ring surface, facing outward
/// toward the horizon; asteroids fill the bottom of the frame, stars the top).
///
/// Unlike the polar (looking-at-the-axis) shots, a star here only stays in frame for the time
/// it takes to cross the field of view as the camera yaws - it enters one side and exits the
/// other, so no single continuous track survives a whole recording. Rather than track once and
/// watch everything die out, the video is processed as a sequence of independent chunks, each
/// with its own fresh Shi-Tomasi seed restricted to the region above the horizon line.
/// </summary>
public static class HorizontalStarTracker
{
    public static HorizontalTrackingResult Track(
        string videoPath,
        IProgress<VideoAnalysisProgress>? progress = null,
        CancellationToken ct = default,
        double chunkSeconds = 24.0,
        int maxCorners = 500,
        double qualityLevel = 0.03,
        double minDistance = 8,
        int winSize = 15,
        int maxLevel = 3,
        float errorThreshold = 15f,
        double horizonFraction = 0.45,
        double? estimatedPeriodSeconds = null)
    {
        using var capture = new VideoCapture(videoPath);
        if (!capture.IsOpened())
        {
            throw new InvalidOperationException($"Could not open video file: {videoPath}");
        }

        double fps = capture.Fps > 0 ? capture.Fps : 30.0;
        var frameSize = new Size(capture.FrameWidth, capture.FrameHeight);
        bool frameCountKnown = capture.FrameCount > 0;
        int estimatedTotal = frameCountKnown ? capture.FrameCount : 1;
        double durationSeconds = estimatedTotal / fps;
        int chunkFrames = Math.Max(30, (int)Math.Round(chunkSeconds * fps));
        int frameStride = ComputeFrameStride(fps, estimatedPeriodSeconds);

        string strideNote = frameStride > 1 ? $" · sampling every {frameStride} frames (long estimated period)" : "";
        progress?.Report(new VideoAnalysisProgress(
            VideoAnalysisStage.Opening, 5,
            $"{frameSize.Width}×{frameSize.Height} · {fps:0.##} fps · {FormatDuration(durationSeconds)}{strideNote}"));

        // A star's apparent drift only builds up to something a per-chunk fit can pull out of
        // tracking noise once the recording spans a large enough slice of a full rotation - the
        // same period/36 threshold the ring/station table already suggests recording *before* the
        // fact, checked here again against what was actually captured. With no period estimate at
        // all (parent mass/body unresolved), fall back to the same "typical" period the solver
        // itself warm-starts from.
        double assumedPeriodSeconds = estimatedPeriodSeconds is > 0 ? estimatedPeriodSeconds.Value : HorizontalVideoAnalyzer.DefaultSeedPeriodSeconds;
        double minReliableDurationSeconds = RingMath.MinimumReliableVideoDurationSeconds(assumedPeriodSeconds);
        if (frameCountKnown && durationSeconds < minReliableDurationSeconds)
        {
            string periodClause = estimatedPeriodSeconds is > 0
                ? $" of the estimated {FormatDuration(estimatedPeriodSeconds.Value)} rotation period"
                : "";
            throw new InvalidOperationException(
                $"This video is only {FormatDuration(Math.Ceiling(durationSeconds))} long, which is too short for a reliable rotation reading{periodClause}. " +
                $"Record at least {FormatDuration(Math.Ceiling(minReliableDurationSeconds))} and try again.");
        }

        int horizonCutoff = (int)(frameSize.Height * horizonFraction);
        using var mask = new Mat(frameSize, MatType.CV_8UC1, Scalar.All(0));
        using (var roi = new Mat(mask, new Rect(0, 0, frameSize.Width, horizonCutoff)))
        {
            roi.SetTo(Scalar.All(255));
        }

        var chunks = new List<HorizontalChunk>();
        List<StarTrack>? currentTracks = null;
        bool[]? alive = null;
        Point2f[] currentPts = Array.Empty<Point2f>();
        Mat? prevGray = null;
        int posInChunk = 0;
        int realFrameIndex = -1;
        const int previewIntervalFrames = 5;

        using var frame = new Mat();
        while (capture.Grab())
        {
            ct.ThrowIfCancellationRequested();
            realFrameIndex++;

            if (realFrameIndex % frameStride != 0)
            {
                continue;
            }

            capture.Retrieve(frame);

            if (posInChunk == 0)
            {
                if (currentTracks != null)
                {
                    chunks.Add(new HorizontalChunk { Tracks = currentTracks });
                }

                var gray0 = new Mat();
                Cv2.CvtColor(frame, gray0, ColorConversionCodes.BGR2GRAY);
                var pts0 = Cv2.GoodFeaturesToTrack(gray0, maxCorners, qualityLevel, minDistance, mask: mask, blockSize: 3, useHarrisDetector: false, k: 0.04);

                currentTracks = new List<StarTrack>(pts0.Length);
                alive = new bool[pts0.Length];
                for (int i = 0; i < pts0.Length; i++)
                {
                    var track = new StarTrack { Id = i };
                    track.FrameIndices.Add(0);
                    track.Xs.Add(pts0[i].X);
                    track.Ys.Add(pts0[i].Y);
                    currentTracks.Add(track);
                    alive[i] = true;
                }
                currentPts = pts0;
                prevGray?.Dispose();
                prevGray = gray0;
                posInChunk = frameStride;
            }
            else
            {
                var gray = new Mat();
                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);

                if (currentPts.Length > 0)
                {
                    Point2f[] nextPts = Array.Empty<Point2f>();
                    Cv2.CalcOpticalFlowPyrLK(
                        prevGray!, gray, currentPts, ref nextPts,
                        out var status, out var err,
                        winSize: new Size(winSize, winSize),
                        maxLevel: maxLevel);

                    for (int i = 0; i < currentPts.Length; i++)
                    {
                        if (!alive![i])
                        {
                            continue;
                        }
                        if (status[i] == 0 || err[i] > errorThreshold)
                        {
                            alive[i] = false;
                            continue;
                        }
                        currentTracks![i].FrameIndices.Add(posInChunk);
                        currentTracks[i].Xs.Add(nextPts[i].X);
                        currentTracks[i].Ys.Add(nextPts[i].Y);
                    }
                    currentPts = nextPts;
                }

                prevGray?.Dispose();
                prevGray = gray;

                posInChunk += frameStride;
                if (posInChunk >= chunkFrames)
                {
                    posInChunk = 0;
                }
            }

            int percent = Math.Min(85, 5 + (realFrameIndex + 1) * 80 / Math.Max(1, estimatedTotal));
            byte[]? previewBytes = null;
            if (frameStride > 1 || realFrameIndex % previewIntervalFrames == 0)
            {
                Cv2.ImEncode(".jpg", frame, out previewBytes);
            }
            progress?.Report(new VideoAnalysisProgress(
                VideoAnalysisStage.Tracking, percent, $"Tracking frame {realFrameIndex + 1} of {estimatedTotal}",
                FramesProcessed: realFrameIndex + 1, TotalFrames: estimatedTotal, PreviewImageBytes: previewBytes));
        }

        if (currentTracks != null)
        {
            int frameCount = posInChunk == 0 ? chunkFrames : posInChunk;
            if (frameCount >= 30)
            {
                chunks.Add(new HorizontalChunk { Tracks = currentTracks });
            }
        }
        prevGray?.Dispose();

        if (realFrameIndex < 0)
        {
            throw new InvalidOperationException("Video has no frames.");
        }

        return new HorizontalTrackingResult { Chunks = chunks, Fps = fps, FrameSize = frameSize };
    }

    /// <summary>
    /// Picks how many real frames to advance between optical-flow samples. For long estimated
    /// periods the apparent per-frame star motion is tiny, so consecutive frames add tracking cost
    /// without adding information; this keeps the pixel shift between sampled frames near a fixed
    /// target instead of always processing every single frame. Falls back to 1 (no skipping) when
    /// there's no usable estimate. The focal length used here is just a rough stand-in for the
    /// solver's later fitted value - it only needs to be in the right ballpark to size the stride.
    /// </summary>
    private static int ComputeFrameStride(double fps, double? estimatedPeriodSeconds)
    {
        const double approxFocalLengthPx = HorizontalRotationSolver.DefaultSeedFocalLengthPx;
        const double targetPixelShiftPerStep = 4.0;
        const int maxFrameStride = 12;

        if (estimatedPeriodSeconds is not double period || period <= 0 || double.IsNaN(period) || double.IsInfinity(period))
        {
            return 1;
        }

        double omega = 2 * Math.PI / period;
        double idealStride = targetPixelShiftPerStep * fps / (approxFocalLengthPx * omega);
        return (int)Math.Clamp(Math.Round(idealStride), 1, maxFrameStride);
    }

    private static string FormatDuration(double totalSeconds)
    {
        var span = TimeSpan.FromSeconds(totalSeconds);
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m {span.Seconds}s";
        }
        if (span.TotalMinutes >= 1)
        {
            return $"{span.Minutes}m {span.Seconds}s";
        }
        return $"{span.Seconds}s";
    }
}
