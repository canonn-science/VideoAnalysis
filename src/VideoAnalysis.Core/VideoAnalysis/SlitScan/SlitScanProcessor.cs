using OpenCvSharp;

namespace VideoAnalysis.Core.VideoAnalysis.SlitScan;

/// <summary>
/// Standard slit-scan compositing: sample a thin strip ("slit") from each frame and lay the
/// strips side-by-side in scan order, so the output's horizontal axis becomes time instead of
/// space - motion across the slit shows up as diagonal/curved streaks. The slit's position within
/// each frame can stay fixed (classic time-slice), sweep between two positions, or orbit a center
/// point (spiral/tunnel), per <see cref="SlitScanParameters.MotionMode"/>. Reuses the same
/// selection/save workflow as Long Exposure per spec (see <c>LongExposureResultsWindow</c>, which
/// both modes share), but this is a distinct algorithm with its own exposed parameters rather
/// than a Long Exposure variant.
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

            int totalFrames = cap.FrameCount > 0 ? cap.FrameCount : 1;
            int interval = Math.Max(parameters.FrameSamplingInterval, 1);
            int scanSpeed = Math.Max(parameters.ScanSpeedPixelsPerFrame, 1);

            double inFraction = Math.Clamp(Math.Min(parameters.InPointFraction, parameters.OutPointFraction), 0.0, 1.0);
            double outFraction = Math.Clamp(Math.Max(parameters.InPointFraction, parameters.OutPointFraction), 0.0, 1.0);
            int inFrameIdx = (int)(inFraction * totalFrames);
            int outFrameIdx = Math.Max((int)(outFraction * totalFrames), inFrameIdx + 1);
            int trimmedRange = Math.Max(outFrameIdx - inFrameIdx, 1);

            if (inFrameIdx > 0)
            {
                cap.Set(VideoCaptureProperties.PosFrames, inFrameIdx);
            }

            var slits = new List<Mat>();
            int slitHeight = 0;
            int frameIndex = inFrameIdx;
            using (var frame = new Mat())
            {
                while (frameIndex < outFrameIdx && cap.Read(frame))
                {
                    ct.ThrowIfCancellationRequested();
                    if (frame.Empty())
                    {
                        break;
                    }

                    if ((frameIndex - inFrameIdx) % interval == 0)
                    {
                        double p = Math.Clamp((double)(frameIndex - inFrameIdx) / trimmedRange, 0.0, 1.0);
                        var slit = ExtractSlit(frame, parameters, p);
                        slits.Add(slit);
                        slitHeight = slit.Height;

                        if (slits.Count % 10 == 0)
                        {
                            int percent = Math.Clamp((int)((frameIndex - inFrameIdx) * 90.0 / trimmedRange), 0, 90);
                            Cv2.ImEncode(".jpg", frame, out var previewBytes);
                            progress?.Report(new VideoAnalysisProgress(
                                VideoAnalysisStage.Processing, percent, $"Sampling slit {slits.Count} (frame {frameIndex} of {outFrameIdx})…",
                                FramesProcessed: frameIndex - inFrameIdx, TotalFrames: trimmedRange, PreviewImageBytes: previewBytes));
                        }
                    }

                    frameIndex++;
                }
            }

            if (slits.Count == 0)
            {
                throw new InvalidOperationException("No frames were sampled - check the frame sampling interval and In/Out trim against the video's length.");
            }

            // Captured before reordering: PingPong duplicates references within `slits`, so
            // disposal must walk the original unique set rather than the (possibly
            // repeated-reference) reordered list.
            var uniqueSlits = new List<Mat>(slits);

            ReorderSlits(slits, parameters.SamplingOrder, parameters.RandomSeed);

            progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.Processing, 92, "Compositing slit-scan image…"));

            using var composited = Composite(slits, slitHeight, scanSpeed, parameters.BlendMode);
            foreach (var slit in uniqueSlits)
            {
                slit.Dispose();
            }

            using var final = ApplyOutputSize(composited, parameters);

            Cv2.ImEncode(".png", final, out var finalPreview);
            progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.Done, 100, "Done", PreviewImageBytes: finalPreview));

            return new SlitScanResult { ImagePng = finalPreview, FramesSampled = slits.Count };
        }, ct);
    }

    /// <summary>Rotates the frame around a center point and crops a fixed-width strip at a fixed
    /// offset from that center - generalizes the classic "rotate then crop the middle column"
    /// slit extraction so the center, angle, and offset can all vary per frame. Static/Sweep use
    /// the frame's own center with a horizontal offset derived from <see cref="SlitScanParameters.SlitPositionFraction"/>;
    /// Rotational uses a user-chosen center with an offset (radius) that stays fixed while the
    /// angle sweeps through the frame, producing the spiral/tunnel look.</summary>
    internal static Mat ExtractSlit(Mat frame, SlitScanParameters parameters, double p)
    {
        Point2f center;
        double rotationAngleDegrees;
        double cropOffsetFromCenterPx;

        if (parameters.MotionMode == SlitScanMotionMode.Rotational)
        {
            center = new Point2f(
                (float)(parameters.RotationCenterXFraction * frame.Width),
                (float)(parameters.RotationCenterYFraction * frame.Height));
            double direction = parameters.RotationDirection == SlitScanRotationDirection.Clockwise ? 1.0 : -1.0;
            rotationAngleDegrees = direction * p * parameters.RotationRevolutions * 360.0;
            cropOffsetFromCenterPx = parameters.RotationRadiusFraction * Math.Min(frame.Width, frame.Height) / 2.0;
        }
        else
        {
            center = new Point2f(frame.Width / 2f, frame.Height / 2f);
            rotationAngleDegrees = 90.0 - parameters.SlitAngleDegrees;
            double positionFraction = parameters.MotionMode == SlitScanMotionMode.Sweep
                ? Lerp(parameters.SlitPositionFraction, parameters.SweepEndPositionFraction, Ease(p, parameters.SweepEasing))
                : parameters.SlitPositionFraction;
            cropOffsetFromCenterPx = positionFraction * frame.Width - frame.Width / 2.0;
        }

        int width = Math.Max(1, parameters.WidthIsAnimated
            ? (int)Math.Round(Lerp(parameters.SlitWidthPixels, parameters.SlitWidthEndPixels, Ease(p, parameters.WidthEasing)))
            : parameters.SlitWidthPixels);

        // A 0/180-degree-congruent rotation is already axis-aligned - skip resampling pixels for no reason.
        double normalizedAngle = ((rotationAngleDegrees % 180.0) + 180.0) % 180.0;
        Mat source = frame;
        Mat? rotated = null;
        Point2f sourceCenter = center;
        int cropHeight = frame.Height;
        int y = 0;

        if (Math.Abs(normalizedAngle) > 0.01)
        {
            // A same-size WarpAffine destination clips whatever rotates outside the original
            // bounding box, so expand to the full rotated bounding box instead and re-center the
            // matrix's translation so the original center point lands at the new canvas's
            // center - the standard "rotate without cropping" technique. The crop below then
            // still takes a frame.Height-tall strip (so all slits stay the same height for
            // compositing), just centered on that recentered point rather than pinned to y=0.
            double radians = rotationAngleDegrees * Math.PI / 180.0;
            double cos = Math.Abs(Math.Cos(radians));
            double sin = Math.Abs(Math.Sin(radians));
            int boundingWidth = Math.Max(1, (int)Math.Ceiling(frame.Height * sin + frame.Width * cos));
            int boundingHeight = Math.Max(1, (int)Math.Ceiling(frame.Height * cos + frame.Width * sin));

            using var rotationMatrix = Cv2.GetRotationMatrix2D(center, rotationAngleDegrees, 1.0);
            rotationMatrix.Set(0, 2, rotationMatrix.Get<double>(0, 2) + boundingWidth / 2.0 - center.X);
            rotationMatrix.Set(1, 2, rotationMatrix.Get<double>(1, 2) + boundingHeight / 2.0 - center.Y);

            rotated = new Mat();
            Cv2.WarpAffine(frame, rotated, rotationMatrix, new Size(boundingWidth, boundingHeight));
            source = rotated;
            sourceCenter = new Point2f(boundingWidth / 2f, boundingHeight / 2f);

            y = Math.Clamp((int)Math.Round(sourceCenter.Y - cropHeight / 2.0), 0, Math.Max(source.Height - cropHeight, 0));
            cropHeight = Math.Min(cropHeight, source.Height - y);
        }

        int x = Math.Clamp((int)(sourceCenter.X + cropOffsetFromCenterPx) - width / 2, 0, Math.Max(source.Width - width, 0));
        int clampedWidth = Math.Min(width, source.Width - x);
        using var view = new Mat(source, new Rect(x, y, Math.Max(clampedWidth, 1), Math.Max(cropHeight, 1)));
        var result = view.Clone();
        rotated?.Dispose();
        return result;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static double Ease(double t, SlitScanEasing easing) => easing switch
    {
        SlitScanEasing.EaseIn => t * t,
        SlitScanEasing.EaseOut => 1.0 - (1.0 - t) * (1.0 - t),
        SlitScanEasing.EaseInOut => t < 0.5 ? 2.0 * t * t : 1.0 - Math.Pow(-2.0 * t + 2.0, 2) / 2.0,
        _ => t,
    };

    /// <summary>Reorders the already-extracted slits so which frame's content lands at which
    /// output position can differ from the order it was sampled in, independent of where within
    /// each frame that content came from (motion/geometry is computed before this runs).</summary>
    private static void ReorderSlits(List<Mat> slits, SlitScanSamplingOrder order, int randomSeed)
    {
        switch (order)
        {
            case SlitScanSamplingOrder.Reverse:
                slits.Reverse();
                break;
            case SlitScanSamplingOrder.PingPong:
                // Forward then back, without repeating either endpoint: [0,1,2,3] -> [0,1,2,3,2,1].
                var mirrored = new List<Mat>(slits);
                for (int i = slits.Count - 2; i >= 1; i--)
                {
                    mirrored.Add(slits[i]);
                }
                slits.Clear();
                slits.AddRange(mirrored);
                break;
            case SlitScanSamplingOrder.Random:
                var rng = new Random(randomSeed);
                for (int i = slits.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (slits[i], slits[j]) = (slits[j], slits[i]);
                }
                break;
        }
    }

    private static Mat Composite(List<Mat> slits, int slitHeight, int scanSpeed, SlitScanBlendMode blendMode)
    {
        int outputWidth = (slits.Count - 1) * scanSpeed + slits[0].Width;
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

    private static Mat ApplyOutputSize(Mat image, SlitScanParameters parameters)
    {
        if (!parameters.CustomOutputSize)
        {
            return image.Clone();
        }

        int width = Math.Max(parameters.OutputWidth, 1);
        int height = Math.Max(parameters.OutputHeight, 1);
        var interpolation = parameters.Interpolation switch
        {
            SlitScanInterpolation.Nearest => InterpolationFlags.Nearest,
            SlitScanInterpolation.Linear => InterpolationFlags.Linear,
            _ => InterpolationFlags.Cubic,
        };

        var resized = new Mat();
        Cv2.Resize(image, resized, new Size(width, height), 0, 0, interpolation);
        return resized;
    }
}
