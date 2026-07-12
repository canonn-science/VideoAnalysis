using OpenCvSharp;

namespace VideoAnalysis.Core.VideoAnalysis;

/// <summary>A HUD region expressed as fractions of frame width/height (0.0-1.0, not pixels), so it
/// holds up across resolutions sharing the same aspect ratio/HUD scale.</summary>
public readonly record struct HudRegion(double X0, double Y0, double X1, double Y1);

/// <summary>
/// Detects the frame where Elite Dangerous's "WARNING! FSD OPERATING / BEYOND SAFETY LIMITS"
/// overlay fades onto the HUD during a neutron-star/white-dwarf jet-cone approach - the moment
/// the reticle/bottom-left distance readout should be read from. Ported line-for-line from
/// <c>S:\Canonn\NeutronJet\process_jet_footage.py</c>'s <c>warning_signature_score</c>/
/// <c>compute_baseline_score</c>/<c>find_warning_onset</c>: same HSV bounds, same two-phase
/// coarse-then-fine scan, same region fractions. Validated against real sample footage in
/// <c>S:\Canonn\NeutronJet\Output</c> during planning (6/6 onset detections).
/// </summary>
public static class JetWarningOnsetDetector
{
    /// <summary>Reticle (center-right) readout: body name (possibly wrapped over two lines),
    /// distance, and ETA countdown. Generous padding - this box's on-screen position visibly
    /// drifts as the ship closes in and its heading wobbles.</summary>
    public static readonly HudRegion ReticleRegion = new(0.50, 0.37, 0.68, 0.60);

    /// <summary>Bottom-left target readout: same body name + distance, single line each - cleaner
    /// to read than the reticle region, but not always populated (no target lock yet).</summary>
    public static readonly HudRegion BottomLeftRegion = new(0.03, 0.735, 0.235, 0.79);

    /// <summary>Centered "WARNING! FSD OPERATING / BEYOND SAFETY LIMITS" text - keyed off the
    /// orange/yellow text only (the red triangle glyph above it is deliberately excluded), a
    /// stronger and simpler color signal.</summary>
    private static readonly HudRegion WarningTextRegion = new(0.41, 0.265, 0.60, 0.335);

    private static readonly Scalar WarningHsvLower = new(0, 70, 120);
    private static readonly Scalar WarningHsvUpper = new(30, 255, 255);

    private const int BaselineSampleFrames = 15;
    private const int BaselineSampleStep = 3;

    /// <summary>Onset is declared the first frame whose warning-region score exceeds baseline +
    /// this delta. In calibration footage baseline sat at 0.0 and the onset frame jumped straight
    /// to ~21, so a modest delta keeps this robust to per-video noise without waiting for full
    /// opacity.</summary>
    private const double WarningScoreDelta = 8.0;

    /// <summary>Coarse step for the first pass over the whole video (speed); the exact onset
    /// frame is then found by re-scanning one coarse step's worth of frames one at a time.</summary>
    private const int WarningCoarseStep = 5;

    private const int MaxFramesFallback = 200_000;

    public sealed record OnsetResult(int? OnsetFrameIndex, bool Detected, double BaselineScore);

    public static Rect RegionToPixels(Size frameSize, HudRegion region)
    {
        // Convert to pixel coords and clamp so the Rect always stays within the frame.
        int x0 = (int)Math.Round(region.X0 * frameSize.Width);
        int y0 = (int)Math.Round(region.Y0 * frameSize.Height);
        int x1 = (int)Math.Round(region.X1 * frameSize.Width);
        int y1 = (int)Math.Round(region.Y1 * frameSize.Height);

        x0 = Math.Clamp(x0, 0, Math.Max(frameSize.Width - 1, 0));
        y0 = Math.Clamp(y0, 0, Math.Max(frameSize.Height - 1, 0));
        x1 = Math.Clamp(x1, x0 + 1, frameSize.Width);
        y1 = Math.Clamp(y1, y0 + 1, frameSize.Height);

        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>Always returns an independent copy - safe to keep around after the source frame
    /// is disposed (unlike a raw sub-Mat view, which would alias freed memory).</summary>
    public static Mat CropRegion(Mat frame, HudRegion region)
    {
        var rect = RegionToPixels(frame.Size(), region);
        using var view = new Mat(frame, rect);
        return view.Clone();
    }

    private static double WarningSignatureScore(Mat frame)
    {
        using var roi = new Mat(frame, RegionToPixels(frame.Size(), WarningTextRegion));
        using var hsv = new Mat();
        Cv2.CvtColor(roi, hsv, ColorConversionCodes.BGR2HSV);
        using var mask = new Mat();
        Cv2.InRange(hsv, WarningHsvLower, WarningHsvUpper, mask);
        return Cv2.Mean(mask).Val0;
    }

    private static bool TryReadFrameAt(VideoCapture cap, int index, out Mat frame)
    {
        cap.Set(VideoCaptureProperties.PosFrames, index);
        frame = new Mat();
        if (cap.Read(frame) && !frame.Empty())
        {
            return true;
        }
        frame.Dispose();
        return false;
    }

    private static double ComputeBaselineScore(VideoCapture cap)
    {
        var scores = new List<double>();
        for (int i = 0; i < BaselineSampleFrames; i++)
        {
            int idx = i * BaselineSampleStep;
            if (!TryReadFrameAt(cap, idx, out var frame))
            {
                break;
            }
            using (frame)
            {
                scores.Add(WarningSignatureScore(frame));
            }
        }
        return scores.Count > 0 ? scores.Average() : 0.0;
    }

    /// <summary>How often (in coarse-scan iterations) to JPEG-encode the current frame for the
    /// live preview - every iteration would add a meaningful encode cost across a long scan for
    /// marginal visible benefit, since consecutive coarse-step frames already differ by
    /// <see cref="WarningCoarseStep"/> real frames.</summary>
    private const int PreviewEveryNIterations = 2;

    /// <summary>Runs the full two-phase scan against an already-open capture (caller owns/disposes
    /// it) so the caller can reuse the same <see cref="VideoCapture"/> afterward to read the crop
    /// at the resulting onset frame without reopening the file. Reports a preview frame and frame
    /// count as it scans - the coarse pass over a multi-thousand-frame video is the slow part of
    /// this detector and otherwise looks like the app has hung.</summary>
    public static OnsetResult FindOnset(VideoCapture cap, IProgress<VideoAnalysisProgress>? progress = null, CancellationToken ct = default)
    {
        int frameCount = cap.FrameCount > 0 ? cap.FrameCount : MaxFramesFallback;
        progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.DetectingOnset, 0, "Establishing baseline HUD signature…", TotalFrames: frameCount));
        double baseline = ComputeBaselineScore(cap);
        double threshold = baseline + WarningScoreDelta;

        int prevIdx = 0;
        int? coarseHit = null;
        int idx = 0;
        int iteration = 0;
        while (idx < frameCount)
        {
            ct.ThrowIfCancellationRequested();
            if (!TryReadFrameAt(cap, idx, out var frame))
            {
                break;
            }
            double score;
            byte[]? previewBytes = null;
            using (frame)
            {
                score = WarningSignatureScore(frame);
                if (iteration % PreviewEveryNIterations == 0)
                {
                    Cv2.ImEncode(".jpg", frame, out previewBytes);
                }
            }
            iteration++;

            int percent = frameCount > 0 ? (int)Math.Clamp(idx * 90.0 / frameCount, 0, 90) : 0;
            progress?.Report(new VideoAnalysisProgress(
                VideoAnalysisStage.DetectingOnset, percent, $"Scanning for the FSD warning overlay (frame {idx} of {frameCount})…",
                FramesProcessed: idx, TotalFrames: frameCount, PreviewImageBytes: previewBytes));

            if (score > threshold)
            {
                coarseHit = idx;
                break;
            }
            prevIdx = idx;
            idx += WarningCoarseStep;
        }

        if (coarseHit is null)
        {
            return new OnsetResult(null, false, baseline);
        }

        for (int fine = prevIdx; fine <= coarseHit.Value; fine++)
        {
            ct.ThrowIfCancellationRequested();
            if (!TryReadFrameAt(cap, fine, out var frame))
            {
                continue;
            }
            double score;
            byte[]? previewBytes;
            using (frame)
            {
                score = WarningSignatureScore(frame);
                Cv2.ImEncode(".jpg", frame, out previewBytes);
            }

            progress?.Report(new VideoAnalysisProgress(
                VideoAnalysisStage.DetectingOnset, 95, $"Pinpointing the exact onset frame ({fine} of {coarseHit.Value})…",
                FramesProcessed: fine, TotalFrames: frameCount, PreviewImageBytes: previewBytes));

            if (score > threshold)
            {
                return new OnsetResult(fine, true, baseline);
            }
        }

        return new OnsetResult(coarseHit, true, baseline);
    }
}
