using OpenCvSharp;

namespace RotationAnalysis.Core.VideoAnalysis.SlitScan;

/// <summary>
/// Standard slit-scan compositing: sample a thin strip ("slit") from each frame at a fixed
/// position/angle and lay the strips side-by-side in scan order, so the output's horizontal axis
/// becomes time instead of space - motion across the slit shows up as diagonal/curved streaks.
/// Reuses the same selection/save workflow as Long Exposure per spec (see
/// <c>LongExposureResultsWindow</c>, which both modes share), but this is a distinct algorithm
/// with its own exposed parameters rather than a Long Exposure variant.
/// </summary>
public static class SlitScanProcessor
{
    public static Task<SlitScanResult> GenerateAsync(
        string videoPath, SlitScanParameters parameters, IProgress<VideoAnalysisProgress>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.Opening, 0, "Opening video…"));
            using var cap = new VideoCapture(videoPath);
            if (!cap.IsOpened())
            {
                throw new InvalidOperationException($"Could not open video file: {videoPath}");
            }

            int estimatedTotal = cap.FrameCount > 0 ? cap.FrameCount : 1;
            int interval = Math.Max(parameters.FrameSamplingInterval, 1);
            int slitWidth = Math.Max(parameters.SlitWidthPixels, 1);
            int scanSpeed = Math.Max(parameters.ScanSpeedPixelsPerFrame, 1);

            var slits = new List<Mat>();
            int slitHeight = 0;
            int frameIndex = 0;
            using (var frame = new Mat())
            {
                while (cap.Read(frame))
                {
                    ct.ThrowIfCancellationRequested();
                    if (frame.Empty())
                    {
                        break;
                    }

                    if (frameIndex % interval == 0)
                    {
                        var slit = ExtractSlit(frame, parameters.SlitAngleDegrees, parameters.SlitPositionFraction, slitWidth);
                        slits.Add(slit);
                        slitHeight = slit.Height;

                        if (slits.Count % 10 == 0)
                        {
                            int percent = estimatedTotal > 0 ? Math.Clamp((int)(frameIndex * 90.0 / estimatedTotal), 0, 90) : 0;
                            Cv2.ImEncode(".jpg", frame, out var previewBytes);
                            progress?.Report(new VideoAnalysisProgress(
                                VideoAnalysisStage.Processing, percent, $"Sampling slit {slits.Count} (frame {frameIndex} of {estimatedTotal})…",
                                FramesProcessed: frameIndex, TotalFrames: estimatedTotal, PreviewImageBytes: previewBytes));
                        }
                    }

                    frameIndex++;
                }
            }

            if (slits.Count == 0)
            {
                throw new InvalidOperationException("No frames were sampled - check the frame sampling interval against the video's length.");
            }

            if (parameters.ScanDirection == SlitScanDirection.Reverse)
            {
                slits.Reverse();
            }

            progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.Processing, 92, "Compositing slit-scan image…"));

            using var composited = Composite(slits, slitHeight, scanSpeed, slitWidth, parameters.BlendMode);
            foreach (var slit in slits)
            {
                slit.Dispose();
            }

            using var final = ApplyMaxWidth(composited, parameters.MaxOutputWidth);

            Cv2.ImEncode(".png", final, out var finalPreview);
            progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.Done, 100, "Done", PreviewImageBytes: finalPreview));

            return new SlitScanResult { ImagePng = finalPreview, FramesSampled = slits.Count };
        }, ct);
    }

    /// <summary>Rotates the frame so the requested slit angle becomes axis-aligned, then crops a
    /// vertical strip at the given fractional position - returns an independent copy sized to the
    /// original frame's diagonal-safe rotation, cropped back to the frame's own height so every
    /// slit has a consistent height regardless of angle.</summary>
    private static Mat ExtractSlit(Mat frame, double angleDegrees, double positionFraction, int width)
    {
        // A 90-degree slit (the common case) is already axis-aligned - skip the rotation
        // entirely rather than resampling pixels for no reason.
        double normalizedAngle = ((angleDegrees - 90.0) % 180.0 + 180.0) % 180.0;
        Mat source = frame;
        Mat? rotated = null;
        if (Math.Abs(normalizedAngle) > 0.01)
        {
            var center = new Point2f(frame.Width / 2f, frame.Height / 2f);
            using var rotationMatrix = Cv2.GetRotationMatrix2D(center, 90.0 - angleDegrees, 1.0);
            rotated = new Mat();
            Cv2.WarpAffine(frame, rotated, rotationMatrix, frame.Size());
            source = rotated;
        }

        int x = Math.Clamp((int)(positionFraction * source.Width) - width / 2, 0, Math.Max(source.Width - width, 0));
        int clampedWidth = Math.Min(width, source.Width - x);
        using var view = new Mat(source, new Rect(x, 0, Math.Max(clampedWidth, 1), source.Height));
        var result = view.Clone();
        rotated?.Dispose();
        return result;
    }

    private static Mat Composite(List<Mat> slits, int slitHeight, int scanSpeed, int slitWidth, SlitScanBlendMode blendMode)
    {
        int outputWidth = (slits.Count - 1) * scanSpeed + slitWidth;
        var canvas = new Mat(new Size(outputWidth, slitHeight), MatType.CV_8UC3, Scalar.All(0));

        if (blendMode != SlitScanBlendMode.Average)
        {
            for (int i = 0; i < slits.Count; i++)
            {
                int x = i * scanSpeed;
                int width = Math.Min(slits[i].Width, outputWidth - x);
                if (width <= 0)
                {
                    continue;
                }
                using var slitCropped = new Mat(slits[i], new Rect(0, 0, width, slitHeight));
                using var dstRegion = new Mat(canvas, new Rect(x, 0, width, slitHeight));
                if (blendMode == SlitScanBlendMode.Lighten)
                {
                    Cv2.Max(dstRegion, slitCropped, dstRegion);
                }
                else
                {
                    slitCropped.CopyTo(dstRegion);
                }
            }
            return canvas;
        }

        // Average: accumulate a running sum per pixel plus a per-column coverage count, then
        // divide at the end. Weight only varies by column (every row of a given slit column is
        // covered identically), so a flat per-column counter is enough - no need for a full 2D
        // weight Mat.
        using var sum = new Mat(canvas.Size(), MatType.CV_64FC3, Scalar.All(0));
        var columnWeight = new int[outputWidth];
        for (int i = 0; i < slits.Count; i++)
        {
            int x = i * scanSpeed;
            int width = Math.Min(slits[i].Width, outputWidth - x);
            if (width <= 0)
            {
                continue;
            }
            using var slitCropped = new Mat(slits[i], new Rect(0, 0, width, slitHeight));
            using var slitDouble = new Mat();
            slitCropped.ConvertTo(slitDouble, MatType.CV_64FC3);
            using var sumRegion = new Mat(sum, new Rect(x, 0, width, slitHeight));
            Cv2.Add(sumRegion, slitDouble, sumRegion);
            for (int c = x; c < x + width; c++)
            {
                columnWeight[c]++;
            }
        }

        for (int x = 0; x < outputWidth; x++)
        {
            int weight = columnWeight[x];
            if (weight <= 1)
            {
                continue;
            }
            using var column = new Mat(sum, new Rect(x, 0, 1, slitHeight));
            Cv2.Multiply(column, new Scalar(1.0 / weight, 1.0 / weight, 1.0 / weight), column);
        }

        sum.ConvertTo(canvas, MatType.CV_8UC3);
        return canvas;
    }

    private static Mat ApplyMaxWidth(Mat image, int? maxWidth)
    {
        if (maxWidth is not int max || image.Width <= max)
        {
            return image.Clone();
        }

        double scale = (double)max / image.Width;
        var resized = new Mat();
        Cv2.Resize(image, resized, new Size(max, (int)(image.Height * scale)), 0, 0, InterpolationFlags.Area);
        return resized;
    }
}
