using OpenCvSharp;

namespace RotationAnalysis.Core.VideoAnalysis;

public sealed record VideoTimestampProfile(
    double MeanFrameIntervalSeconds, double StdFrameIntervalSeconds, int DroppedFrameCount, int FrameCount);

/// <summary>
/// Characterizes a video's actual frame timing (container presentation timestamps, not nominal
/// fps) for the calibration log: mean/std of frame intervals, and a count of intervals more than
/// 1.5x the nominal one (a proxy for dropped/duplicated frames). Grab-only for most frames (no
/// decode) so the full-video pass stays cheap; only the handful of frames a progress tick lands on
/// get decoded, so a preview image can accompany that tick.
/// </summary>
public static class VideoTimestampProfiler
{
    private const double DroppedFrameIntervalMultiplier = 1.5;

    /// <summary>How many progress ticks to report across the whole video, regardless of its
    /// length - enough that the UI visibly moves without flooding the progress channel.</summary>
    private const int ProgressTickCount = 50;

    /// <param name="onProgress">Invoked periodically with (framesGrabbed, totalFrames, previewJpegBytes) -
    /// this is a full sequential pass over the whole video, so on a long recording it can take real
    /// wall-clock time with nothing else to show for it otherwise.</param>
    public static VideoTimestampProfile Profile(
        string videoPath, double nominalFps, Action<int, int, byte[]?>? onProgress = null, CancellationToken ct = default)
    {
        using var capture = new VideoCapture(videoPath);
        if (!capture.IsOpened())
        {
            return new VideoTimestampProfile(0, 0, 0, 0);
        }

        int totalFrames = capture.FrameCount > 0 ? capture.FrameCount : 1;
        int reportInterval = Math.Max(1, totalFrames / ProgressTickCount);

        double nominalInterval = nominalFps > 0 ? 1.0 / nominalFps : 0;
        double? lastT = null;
        var intervals = new List<double>();
        int frameCount = 0;

        using var frame = new Mat();
        while (capture.Grab())
        {
            ct.ThrowIfCancellationRequested();
            double t = capture.Get(VideoCaptureProperties.PosMsec) / 1000.0;
            frameCount++;
            if (lastT is double prev)
            {
                intervals.Add(t - prev);
            }
            lastT = t;

            if (frameCount % reportInterval == 0)
            {
                byte[]? previewBytes = null;
                if (onProgress is not null && capture.Retrieve(frame) && !frame.Empty())
                {
                    Cv2.ImEncode(".jpg", frame, out previewBytes);
                }
                onProgress?.Invoke(frameCount, totalFrames, previewBytes);
            }
        }
        onProgress?.Invoke(frameCount, totalFrames, null);

        if (intervals.Count == 0)
        {
            return new VideoTimestampProfile(0, 0, 0, frameCount);
        }

        double mean = intervals.Average();
        double variance = intervals.Sum(x => (x - mean) * (x - mean)) / Math.Max(1, intervals.Count - 1);
        double std = Math.Sqrt(variance);
        int dropped = nominalInterval > 0
            ? intervals.Count(x => x > nominalInterval * DroppedFrameIntervalMultiplier)
            : 0;

        return new VideoTimestampProfile(mean, std, dropped, frameCount);
    }
}
