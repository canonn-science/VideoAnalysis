using OpenCvSharp;

namespace RotationAnalysis.Core.VideoAnalysis;

public sealed class StarTrackingResult
{
    public required List<StarTrack> Tracks { get; init; }
    public required double Fps { get; init; }
    public required int FrameCount { get; init; }
    public required Size FrameSize { get; init; }

    /// <summary>Running max-blend of every frame read during tracking. Caller owns disposal.</summary>
    public required Mat Timelapse { get; init; }
}

/// <summary>
/// Detects star-like point features on the first frame (Shi-Tomasi corners) and chains
/// Lucas-Kanade sparse optical flow frame-to-frame to build a pixel trajectory per star.
///
/// Parameters below were tuned and validated against a real Elite Dangerous neutron-star-ring
/// recording: chaining frame-to-frame (rather than jumping between distant frames) is essential
/// in a dense star field, since long-baseline flow produces ambiguous matches. Shi-Tomasi corner
/// detection combined with the LK error threshold naturally ignores the jet's smooth nebulosity
/// without any separate masking step.
/// </summary>
public static class StarTracker
{
    public static StarTrackingResult Track(
        string videoPath,
        IProgress<VideoAnalysisProgress>? progress = null,
        CancellationToken ct = default,
        int maxCorners = 800,
        double qualityLevel = 0.05,
        double minDistance = 10,
        int winSize = 11,
        int maxLevel = 2,
        float errorThreshold = 15f)
    {
        using var capture = new VideoCapture(videoPath);
        if (!capture.IsOpened())
        {
            throw new InvalidOperationException($"Could not open video file: {videoPath}");
        }

        double fps = capture.Fps > 0 ? capture.Fps : 30.0;
        var frameSize = new Size(capture.FrameWidth, capture.FrameHeight);
        int estimatedTotal = capture.FrameCount > 0 ? capture.FrameCount : 1;

        using var firstFrame = new Mat();
        if (!capture.Read(firstFrame) || firstFrame.Empty())
        {
            throw new InvalidOperationException("Video has no frames.");
        }

        Mat currentGray = new Mat();
        Cv2.CvtColor(firstFrame, currentGray, ColorConversionCodes.BGR2GRAY);

        var initialPoints = Cv2.GoodFeaturesToTrack(currentGray, maxCorners, qualityLevel, minDistance, mask: new Mat(), blockSize: 3, useHarrisDetector: false, k: 0.04);

        var tracks = new List<StarTrack>(initialPoints.Length);
        var alive = new bool[initialPoints.Length];
        for (int i = 0; i < initialPoints.Length; i++)
        {
            var track = new StarTrack { Id = i };
            track.FrameIndices.Add(0);
            track.Xs.Add(initialPoints[i].X);
            track.Ys.Add(initialPoints[i].Y);
            tracks.Add(track);
            alive[i] = true;
        }

        var currentPts = initialPoints;
        int frameIndex = 0;

        // Every frame read here for tracking is also folded into a running max-blend
        // "timelapse" image (classic star-trail compositing), so the two heaviest parts of
        // the pipeline - optical flow and timelapse rendering - happen in a single video pass
        // instead of two. A PNG-encoded snapshot is reported periodically so the UI can show
        // the timelapse actually being built up in real time.
        Mat timelapse = firstFrame.Clone();
        const int previewIntervalFrames = 10;

        using var frame = new Mat();
        while (capture.Read(frame))
        {
            ct.ThrowIfCancellationRequested();
            frameIndex++;

            Cv2.Max(timelapse, frame, timelapse);

            var nextGray = new Mat();
            Cv2.CvtColor(frame, nextGray, ColorConversionCodes.BGR2GRAY);

            Point2f[] nextPts = Array.Empty<Point2f>();
            Cv2.CalcOpticalFlowPyrLK(
                currentGray, nextGray, currentPts, ref nextPts,
                out var status, out var err,
                winSize: new Size(winSize, winSize),
                maxLevel: maxLevel);

            for (int i = 0; i < currentPts.Length; i++)
            {
                if (!alive[i])
                {
                    continue;
                }
                if (status[i] == 0 || err[i] > errorThreshold)
                {
                    alive[i] = false;
                    continue;
                }
                tracks[i].FrameIndices.Add(frameIndex);
                tracks[i].Xs.Add(nextPts[i].X);
                tracks[i].Ys.Add(nextPts[i].Y);
            }

            currentPts = nextPts;
            currentGray.Dispose();
            currentGray = nextGray;

            int percent = Math.Min(85, 5 + frameIndex * 80 / Math.Max(1, estimatedTotal));
            byte[]? previewBytes = null;
            if (frameIndex % previewIntervalFrames == 0)
            {
                Cv2.ImEncode(".png", timelapse, out previewBytes);
            }
            progress?.Report(new VideoAnalysisProgress(
                VideoAnalysisStage.Tracking, percent, $"Tracking frame {frameIndex} of {estimatedTotal}",
                FramesProcessed: frameIndex, TotalFrames: estimatedTotal, PreviewImageBytes: previewBytes));
        }

        currentGray.Dispose();

        return new StarTrackingResult
        {
            Tracks = tracks,
            Fps = fps,
            FrameCount = frameIndex + 1,
            FrameSize = frameSize,
            Timelapse = timelapse,
        };
    }
}
