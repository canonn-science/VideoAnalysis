using OpenCvSharp;

namespace RotationAnalysis.Core.VideoAnalysis;

public sealed class HorizontalChunk
{
    public required List<StarTrack> Tracks { get; init; }
}

public sealed class HorizontalTrackingResult
{
    public required List<HorizontalChunk> Chunks { get; init; }
    public required double Fps { get; init; }
    public required Size FrameSize { get; init; }
    public required double DurationSeconds { get; init; }
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
    /// <summary>Below this per-step pixel displacement, motion is too close to tracking noise to
    /// trust - the fix is the same whether it's genuinely slow rotation or just noise: take a
    /// bigger step so the next comparison has a clearer signal.</summary>
    private const double MinPixelShiftPerStep = 1.0;

    /// <summary>Ceiling on how many real frames to skip between optical-flow samples. Rotation
    /// speed is constant for a given ring (Elite's rings turn as a rigid disk), so once a stride
    /// comfortably clears the noise floor there's no reason to keep growing it - this just bounds
    /// the worst case (e.g. a near-stationary scene) rather than being a target to reach.</summary>
    private const int MaxFrameStride = 8;

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
        double horizonFraction = 0.45)
    {
        using var capture = new VideoCapture(videoPath);
        if (!capture.IsOpened())
        {
            throw new InvalidOperationException($"Could not open video file: {videoPath}");
        }

        double fps = capture.Fps > 0 ? capture.Fps : 30.0;
        var frameSize = new Size(capture.FrameWidth, capture.FrameHeight);
        int estimatedTotal = capture.FrameCount > 0 ? capture.FrameCount : 1;
        int chunkFrames = Math.Max(30, (int)Math.Round(chunkSeconds * fps));

        progress?.Report(new VideoAnalysisProgress(
            VideoAnalysisStage.Opening, 5,
            $"{frameSize.Width}×{frameSize.Height} · {fps:0.##} fps · {FormatDuration(estimatedTotal / fps)}"));

        int horizonCutoff = (int)(frameSize.Height * horizonFraction);
        using var mask = new Mat(frameSize, MatType.CV_8UC1, Scalar.All(0));
        using (var roi = new Mat(mask, new Rect(0, 0, frameSize.Width, horizonCutoff)))
        {
            roi.SetTo(Scalar.All(255));
        }

        // Adapts from observed motion rather than trusting an upfront period estimate (which can
        // itself be off by 5-15%, per the calibration data) - doubled whenever a step's tracked
        // displacement is too small to trust, carried forward across chunk boundaries once it's
        // found a good cadence for this ring's actual speed. Pre-warmed here with a short throwaway
        // pass (rather than starting chunk 1 cold at stride=1) so chunk 1's own tracks don't pay the
        // cost of the ramp-up: at a small stride, a track has to survive many more individual
        // optical-flow hops to last the same 24-second chunk, and each hop is one more chance for it
        // to die - denser sampling doesn't add angular sweep, only more opportunities to lose points.
        int frameStride = ProbeInitialStride(capture, mask, maxCorners, qualityLevel, minDistance, winSize, maxLevel, errorThreshold, ct);
        capture.Set(VideoCaptureProperties.PosFrames, 0);
        int framesToSkip = 0;

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

            if (framesToSkip > 0)
            {
                framesToSkip--;
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
                framesToSkip = frameStride - 1;
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

                    var stepShifts = new List<double>();
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
                        double dx = nextPts[i].X - currentPts[i].X;
                        double dy = nextPts[i].Y - currentPts[i].Y;
                        stepShifts.Add(Math.Sqrt(dx * dx + dy * dy));

                        currentTracks![i].FrameIndices.Add(posInChunk);
                        currentTracks[i].Xs.Add(nextPts[i].X);
                        currentTracks[i].Ys.Add(nextPts[i].Y);
                    }
                    currentPts = nextPts;

                    if (stepShifts.Count > 0 && frameStride < MaxFrameStride)
                    {
                        stepShifts.Sort();
                        double medianShift = stepShifts[stepShifts.Count / 2];
                        if (medianShift < MinPixelShiftPerStep)
                        {
                            frameStride = Math.Min(frameStride * 2, MaxFrameStride);
                        }
                    }
                }

                prevGray?.Dispose();
                prevGray = gray;

                posInChunk += frameStride;
                framesToSkip = frameStride - 1;
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
            string strideNote = frameStride > 1 ? $" · sampling every {frameStride} frames" : "";
            progress?.Report(new VideoAnalysisProgress(
                VideoAnalysisStage.Tracking, percent, $"Tracking frame {realFrameIndex + 1} of {estimatedTotal}{strideNote}",
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

        return new HorizontalTrackingResult
        {
            Chunks = chunks,
            Fps = fps,
            FrameSize = frameSize,
            DurationSeconds = estimatedTotal / fps,
        };
    }

    /// <summary>
    /// Throwaway pass over the first handful of frames to find a working starting stride before
    /// chunk 1's real tracking begins, so chunk 1 doesn't have to warm up on its own time (see the
    /// comment above its call site). Ramps the exact same way the main loop does - double whenever
    /// a step's median displacement is under the noise floor - and stops as soon as one step clears
    /// it (or the cap), discarding everything it tracked. Bounded to a small number of real frames
    /// so a pathological near-stationary scene can't stall startup.
    /// </summary>
    private static int ProbeInitialStride(
        VideoCapture capture, Mat mask, int maxCorners, double qualityLevel, double minDistance,
        int winSize, int maxLevel, float errorThreshold, CancellationToken ct)
    {
        const int maxProbeFrames = 64;

        using var gray0 = new Mat();
        using (var frame0 = new Mat())
        {
            if (!capture.Read(frame0) || frame0.Empty())
            {
                return 1;
            }
            Cv2.CvtColor(frame0, gray0, ColorConversionCodes.BGR2GRAY);
        }

        var pts = Cv2.GoodFeaturesToTrack(gray0, maxCorners, qualityLevel, minDistance, mask: mask, blockSize: 3, useHarrisDetector: false, k: 0.04);
        if (pts.Length == 0)
        {
            return 1;
        }

        int frameStride = 1;
        Mat prevGray = gray0.Clone();
        int framesConsumed = 0;

        try
        {
            while (framesConsumed < maxProbeFrames)
            {
                for (int skip = 0; skip < frameStride - 1 && capture.Grab(); skip++)
                {
                    ct.ThrowIfCancellationRequested();
                    framesConsumed++;
                }

                using var frame = new Mat();
                if (!capture.Read(frame) || frame.Empty())
                {
                    break;
                }
                framesConsumed++;
                ct.ThrowIfCancellationRequested();

                using var gray = new Mat();
                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);

                Point2f[] nextPts = Array.Empty<Point2f>();
                Cv2.CalcOpticalFlowPyrLK(
                    prevGray, gray, pts, ref nextPts, out var status, out var err,
                    winSize: new Size(winSize, winSize), maxLevel: maxLevel);

                var shifts = new List<double>();
                for (int i = 0; i < pts.Length; i++)
                {
                    if (status[i] == 0 || err[i] > errorThreshold)
                    {
                        continue;
                    }
                    double dx = nextPts[i].X - pts[i].X;
                    double dy = nextPts[i].Y - pts[i].Y;
                    shifts.Add(Math.Sqrt(dx * dx + dy * dy));
                }

                prevGray.Dispose();
                prevGray = gray.Clone();
                pts = nextPts;

                if (shifts.Count == 0)
                {
                    break; // nothing left to measure - go with whatever stride we'd reached
                }

                shifts.Sort();
                double medianShift = shifts[shifts.Count / 2];
                if (medianShift >= MinPixelShiftPerStep || frameStride >= MaxFrameStride)
                {
                    break;
                }
                frameStride = Math.Min(frameStride * 2, MaxFrameStride);
            }
        }
        finally
        {
            prevGray.Dispose();
        }

        return frameStride;
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
