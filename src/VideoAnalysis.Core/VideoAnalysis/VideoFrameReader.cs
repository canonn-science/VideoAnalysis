using OpenCvSharp;

namespace VideoAnalysis.Core.VideoAnalysis;

/// <summary>Grabs a single representative frame from a video - cheap enough to run once (on
/// Slit Scan upload, or when caching a video library thumbnail) without a full decode pipeline.</summary>
public static class VideoFrameReader
{
    public static Task<byte[]?> ReadRepresentativeFrameAsync(string videoPath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var cap = new VideoCapture(videoPath);
            if (!cap.IsOpened())
            {
                return null;
            }

            // A frame a little into the video, rather than frame 0, avoids black/fade-in frames
            // some captures start with, while staying cheap (no scan needed - direct seek).
            if (cap.FrameCount > 1)
            {
                cap.Set(VideoCaptureProperties.PosFrames, Math.Min(cap.FrameCount / 10, cap.FrameCount - 1));
            }

            using var frame = new Mat();
            ct.ThrowIfCancellationRequested();
            if (!cap.Read(frame) || frame.Empty())
            {
                return null;
            }

            Cv2.ImEncode(".png", frame, out var bytes);
            return (byte[]?)bytes;
        }, ct);
    }
}
