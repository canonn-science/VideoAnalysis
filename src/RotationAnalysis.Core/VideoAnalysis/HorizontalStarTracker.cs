using OpenCvSharp;

namespace RotationAnalysis.Core.VideoAnalysis;

public sealed class HorizontalChunk
{
    public required List<StarTrack> Tracks { get; init; }
    public required int StartFrame { get; init; }
    public required int FrameCount { get; init; }
}

public sealed class HorizontalTrackingResult
{
    public required List<HorizontalChunk> Chunks { get; init; }
    public required double Fps { get; init; }
    public required Size FrameSize { get; init; }

    /// <summary>Running max-blend of every frame read during tracking. Caller owns disposal.</summary>
    public required Mat Timelapse { get; init; }
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
        int chunkStartFrame = 0;
        int globalFrameIndex = -1;
        const int previewIntervalFrames = 5;
        Mat? timelapse = null;

        using var frame = new Mat();
        while (capture.Read(frame))
        {
            ct.ThrowIfCancellationRequested();
            globalFrameIndex++;

            if (timelapse is null)
            {
                timelapse = frame.Clone();
            }
            else
            {
                Cv2.Max(timelapse, frame, timelapse);
            }

            if (posInChunk == 0)
            {
                if (currentTracks != null)
                {
                    chunks.Add(new HorizontalChunk { Tracks = currentTracks, StartFrame = chunkStartFrame, FrameCount = chunkFrames });
                }

                chunkStartFrame = globalFrameIndex;
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
                posInChunk = 1;
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

                posInChunk++;
                if (posInChunk >= chunkFrames)
                {
                    posInChunk = 0;
                }
            }

            int percent = Math.Min(85, 5 + (globalFrameIndex + 1) * 80 / Math.Max(1, estimatedTotal));
            byte[]? previewBytes = null;
            if (globalFrameIndex % previewIntervalFrames == 0)
            {
                Cv2.ImEncode(".jpg", frame, out previewBytes);
            }
            progress?.Report(new VideoAnalysisProgress(
                VideoAnalysisStage.Tracking, percent, $"Tracking frame {globalFrameIndex + 1} of {estimatedTotal}",
                FramesProcessed: globalFrameIndex + 1, TotalFrames: estimatedTotal, PreviewImageBytes: previewBytes));
        }

        if (currentTracks != null)
        {
            int frameCount = posInChunk == 0 ? chunkFrames : posInChunk;
            if (frameCount >= 30)
            {
                chunks.Add(new HorizontalChunk { Tracks = currentTracks, StartFrame = chunkStartFrame, FrameCount = frameCount });
            }
        }
        prevGray?.Dispose();

        if (timelapse is null)
        {
            throw new InvalidOperationException("Video has no frames.");
        }

        return new HorizontalTrackingResult { Chunks = chunks, Fps = fps, FrameSize = frameSize, Timelapse = timelapse };
    }
}
