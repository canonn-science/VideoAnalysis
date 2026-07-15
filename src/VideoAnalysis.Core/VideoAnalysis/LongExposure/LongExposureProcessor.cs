using OpenCvSharp;

namespace VideoAnalysis.Core.VideoAnalysis.LongExposure;

/// <summary>
/// Ports the five stacking methods from the Canonn Long-Exposure project
/// (https://github.com/canonn-science/Long-Exposure, <c>longexposure.py</c>) into native C# -
/// same per-pixel accumulation math, just done in one pass over the video instead of Python's
/// separate numpy arrays, and re-implemented in OpenCvSharp rather than shelling out to Python so
/// the app stays a single self-contained executable. Adds a sixth "Motion Blur" variant per spec,
/// distinct from Motion Variance (which is a static-vs-moving heatmap, not a blur render): an
/// exponential moving average across frames, producing a comet-trail/light-trail look where
/// recent frames dominate but older bright spots fade rather than persisting forever the way
/// Maximum's per-pixel brightest-wins trails do. Adds a seventh "Chronological Trails" variant on
/// top of Maximum's own per-pixel rule: instead of keeping each pixel's original color, it
/// recolors the brightest-wins trail by *when* each point was captured (blue = early, red = late).
/// </summary>
public static class LongExposureProcessor
{
    /// <summary>Default blend weight for each new frame in the motion-blur exponential moving
    /// average - lower = longer, fainter trails; higher = shorter, sharper ones. Chosen empirically
    /// as a reasonable middle ground for typical Elite Dangerous flight/orbit footage.</summary>
    public const double DefaultMotionBlurAlpha = 0.15;

    /// <summary><paramref name="motionBlurAlpha"/> is clamped to (0, 1] - 0 would never let a new
    /// frame contribute anything, freezing the accumulator at the first frame forever.
    /// <paramref name="selectedVariants"/> skips both the per-frame accumulation and the final
    /// compositing work for anything not selected, rather than just discarding it afterward - so
    /// deselecting variants you don't need actually speeds up generation.</summary>
    public static Task<LongExposureResult> GenerateAsync(
        string videoPath, double motionBlurAlpha = DefaultMotionBlurAlpha,
        LongExposureVariants selectedVariants = LongExposureVariants.All,
        IProgress<VideoAnalysisProgress>? progress = null, CancellationToken ct = default)
    {
        if (selectedVariants == LongExposureVariants.None)
        {
            throw new ArgumentException("At least one variant must be selected.", nameof(selectedVariants));
        }

        var alpha = Math.Clamp(motionBlurAlpha, 0.01, 1.0);

        // Max Minus Min can't exist without both accumulators, so any one of the three selects all.
        const LongExposureVariants maxMinGroup = LongExposureVariants.Maximum | LongExposureVariants.Minimum | LongExposureVariants.MaxMinusMin;
        var needMaxMin = (selectedVariants & maxMinGroup) != LongExposureVariants.None;
        var needAverage = (selectedVariants & LongExposureVariants.Average) != LongExposureVariants.None;
        var needMotionBlur = (selectedVariants & LongExposureVariants.MotionBlur) != LongExposureVariants.None;
        var needMotionVariance = (selectedVariants & LongExposureVariants.MotionVariance) != LongExposureVariants.None;
        var needChronoTrails = (selectedVariants & LongExposureVariants.ChronologicalTrails) != LongExposureVariants.None;

        return Task.Run(() =>
        {
            progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.Opening, 0, "Opening video…"));
            using var cap = new VideoCapture(videoPath);
            if (!cap.IsOpened())
            {
                throw new InvalidOperationException($"Could not open video file: {videoPath}");
            }

            using var firstFrame = new Mat();
            if (!cap.Read(firstFrame) || firstFrame.Empty())
            {
                throw new InvalidOperationException("Video has no frames.");
            }

            var size = firstFrame.Size();
            int estimatedTotal = cap.FrameCount > 0 ? cap.FrameCount : 1;

            using var maxFrame = needMaxMin ? firstFrame.Clone() : new Mat();
            using var minFrame = needMaxMin ? firstFrame.Clone() : new Mat();

            using var sumFrame = new Mat();
            if (needAverage)
            {
                firstFrame.ConvertTo(sumFrame, MatType.CV_64FC3);
            }

            using var blurAccumulator = new Mat();
            if (needMotionBlur)
            {
                firstFrame.ConvertTo(blurAccumulator, MatType.CV_64FC3);
            }

            using var prevGray = new Mat();
            if (needMotionVariance)
            {
                Cv2.CvtColor(firstFrame, prevGray, ColorConversionCodes.BGR2GRAY);
            }

using var motionSum = needMotionVariance ? Mat.Zeros(size, MatType.CV_64FC1).ToMat() : new Mat();
using var motionSqSum = needMotionVariance ? Mat.Zeros(size, MatType.CV_64FC1).ToMat() : new Mat();

            // Grayscale "brightest wins" accumulator (by BGR-to-GRAY luminance), plus a parallel map
            // recording the 1-based frame index each pixel's max was (most recently) set from -
            // together these let the final image recolor the trail by *when* each point on it was
            // captured instead of by the pixel's original color.
            using var trailMaxGray = new Mat();
            if (needChronoTrails)
            {
                Cv2.CvtColor(firstFrame, trailMaxGray, ColorConversionCodes.BGR2GRAY);
            }
            // Starts at 1 (not 0) to match trailMaxGray's initial content: both describe "frame 1
            // is the brightest seen so far" until a later frame overtakes a given pixel.
            using var trailTimeMap = needChronoTrails ? Mat.Ones(size, MatType.CV_32FC1).ToMat() : new Mat();

            int count = 1;
            using var frame = new Mat();
            while (cap.Read(frame))
            {
                ct.ThrowIfCancellationRequested();
                if (frame.Empty())
                {
                    break;
                }

                if (needMaxMin)
                {
                    Cv2.Max(maxFrame, frame, maxFrame);
                    Cv2.Min(minFrame, frame, minFrame);
                }

                if (needAverage || needMotionBlur)
                {
                    using var frameDouble = new Mat();
                    frame.ConvertTo(frameDouble, MatType.CV_64FC3);
                    if (needAverage)
                    {
                        Cv2.Add(sumFrame, frameDouble, sumFrame);
                    }
                    if (needMotionBlur)
                    {
                        Cv2.AddWeighted(frameDouble, alpha, blurAccumulator, 1.0 - alpha, 0, blurAccumulator);
                    }
                }

                if (needMotionVariance)
                {
                    using var gray = new Mat();
                    using var diff = new Mat();
                    using var diffDouble = new Mat();
                    using var diffSquared = new Mat();
                    Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
                    Cv2.Absdiff(gray, prevGray, diff);
                    diff.ConvertTo(diffDouble, MatType.CV_64FC1);
                    Cv2.Add(motionSum, diffDouble, motionSum);
                    Cv2.Multiply(diffDouble, diffDouble, diffSquared);
                    Cv2.Add(motionSqSum, diffSquared, motionSqSum);
                    gray.CopyTo(prevGray);
                }

                if (needChronoTrails)
                {
                    using var gray = new Mat();
                    Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
                    using var brighterMask = new Mat();
                    Cv2.Compare(gray, trailMaxGray, brighterMask, CmpTypes.GT);
                    trailTimeMap.SetTo(new Scalar(count + 1), brighterMask);
                    gray.CopyTo(trailMaxGray, brighterMask);
                }

                count++;
                if (count % 10 == 0)
                {
                    int percent = estimatedTotal > 0 ? Math.Clamp((int)(count * 90.0 / estimatedTotal), 0, 90) : 0;
                    Cv2.ImEncode(".jpg", frame, out var previewBytes);
                    progress?.Report(new VideoAnalysisProgress(
                        VideoAnalysisStage.Processing, percent, $"Stacking frame {count} of {estimatedTotal}…",
                        FramesProcessed: count, TotalFrames: estimatedTotal, PreviewImageBytes: previewBytes));
                }
            }

            progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.Processing, 92, "Compositing results…", FramesProcessed: count, TotalFrames: estimatedTotal));

            byte[]? averagePng = null;
            if (needAverage)
            {
                using var averageFrame = new Mat();
                sumFrame.ConvertTo(averageFrame, MatType.CV_8UC3, 1.0 / count);
                averagePng = Encode(averageFrame);
            }

            byte[]? maximumPng = (selectedVariants & LongExposureVariants.Maximum) != LongExposureVariants.None ? Encode(maxFrame) : null;
            byte[]? minimumPng = (selectedVariants & LongExposureVariants.Minimum) != LongExposureVariants.None ? Encode(minFrame) : null;

            byte[]? maxMinusMinPng = null;
            if ((selectedVariants & LongExposureVariants.MaxMinusMin) != LongExposureVariants.None)
            {
                using var maxMinusMin = new Mat();
                Cv2.Absdiff(maxFrame, minFrame, maxMinusMin);
                using var maxMinusMinNorm = NormalizeToFullRange(maxMinusMin, MatType.CV_8UC3);
                maxMinusMinPng = Encode(maxMinusMinNorm);
            }

            byte[]? motionVariancePng = null;
            if (needMotionVariance)
            {
                using var motionVarianceColorized = BuildMotionVarianceImage(motionSum, motionSqSum, Math.Max(count - 1, 1));
                motionVariancePng = Encode(motionVarianceColorized);
            }

            byte[]? motionBlurPng = null;
            if (needMotionBlur)
            {
                using var motionBlurFrame = new Mat();
                blurAccumulator.ConvertTo(motionBlurFrame, MatType.CV_8UC3);
                motionBlurPng = Encode(motionBlurFrame);
            }

            byte[]? chronologicalTrailsPng = null;
            if (needChronoTrails)
            {
                using var chronoImage = BuildChronologicalTrailsImage(trailMaxGray, trailTimeMap, count);
                chronologicalTrailsPng = Encode(chronoImage);
            }

            var finalPreviewBytes = averagePng ?? maximumPng ?? minimumPng ?? motionBlurPng ?? motionVariancePng ?? maxMinusMinPng ?? chronologicalTrailsPng;
            progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.Done, 100, "Done", PreviewImageBytes: finalPreviewBytes));

            return new LongExposureResult
            {
                AveragePng = averagePng,
                MaximumPng = maximumPng,
                MinimumPng = minimumPng,
                MaxMinusMinPng = maxMinusMinPng,
                MotionVariancePng = motionVariancePng,
                MotionBlurPng = motionBlurPng,
                ChronologicalTrailsPng = chronologicalTrailsPng,
                FrameCount = count,
            };
        }, ct);
    }

    /// <summary>Scales a Mat so its brightest value (across all channels, matching numpy's flat
    /// <c>.max()</c> over the whole array) becomes 255 - same normalization
    /// <c>longexposure.py</c>'s max-minus-min step does.</summary>
    private static Mat NormalizeToFullRange(Mat src, MatType outputType)
    {
        using var flat = src.Reshape(1);
        Cv2.MinMaxLoc(flat, out _, out double maxVal);

        var normalized = new Mat();
        if (maxVal > 0)
        {
            src.ConvertTo(normalized, outputType, 255.0 / maxVal);
        }
        else
        {
            src.ConvertTo(normalized, outputType);
        }
        return normalized;
    }

    /// <summary>Per-pixel variance of the frame-to-frame grayscale difference, normalized to
    /// 0-255, colorized (JET colormap), with zero-variance pixels forced back to black so static
    /// background doesn't pick up a stray color from the colormap's zero point.</summary>
    private static Mat BuildMotionVarianceImage(Mat motionSum, Mat motionSqSum, int sampleCount)
    {
        using var meanMotion = new Mat();
        motionSum.ConvertTo(meanMotion, MatType.CV_64FC1, 1.0 / sampleCount);
        using var meanSqMotion = new Mat();
        motionSqSum.ConvertTo(meanSqMotion, MatType.CV_64FC1, 1.0 / sampleCount);

        using var meanMotionSquared = new Mat();
        Cv2.Multiply(meanMotion, meanMotion, meanMotionSquared);
        using var variance = new Mat();
        Cv2.Subtract(meanSqMotion, meanMotionSquared, variance);

        Cv2.MinMaxLoc(variance, out double minVar, out double maxVar);

        using var varianceNorm = new Mat();
        if (maxVar > minVar)
        {
            using var shifted = new Mat();
            Cv2.Subtract(variance, new Scalar(minVar), shifted);
            shifted.ConvertTo(varianceNorm, MatType.CV_8UC1, 255.0 / (maxVar - minVar));
        }
        else
        {
            Mat.Zeros(variance.Size(), MatType.CV_8UC1).ToMat().CopyTo(varianceNorm);
        }

        var colorized = new Mat();
        Cv2.ApplyColorMap(varianceNorm, colorized, ColormapTypes.Jet);

        using var zeroMask = new Mat();
        Cv2.Compare(varianceNorm, 0, zeroMask, CmpTypes.EQ);
        colorized.SetTo(Scalar.All(0), zeroMask);

        return colorized;
    }

    /// <summary>Recolors the grayscale "brightest wins" trail (<paramref name="maxGray"/>, same
    /// per-pixel rule as Maximum) by when each point on it was captured: hue sweeps blue (early
    /// frames) to red (late frames) - a "cool to warm" progression rather than a full rainbow, so
    /// early-vs-late reads intuitively and there's no wrap-around back to a repeated color -
    /// while brightness carries over as HSV Value. Pixels that were never overtaken from their
    /// first-frame level stay at whatever brightness that was; since space backgrounds are
    /// near-black, they render as black regardless of hue, the same "background stays dark" look
    /// Motion Variance gets from its explicit zero mask, just for free here.</summary>
    private static Mat BuildChronologicalTrailsImage(Mat maxGray, Mat timeMap, int totalFrameCount)
    {
        const double startHue = 120.0; // blue
        const double endHue = 0.0; // red

        var denominator = Math.Max(totalFrameCount - 1, 1);
        using var normalizedTime = new Mat();
        Cv2.Subtract(timeMap, new Scalar(1.0), normalizedTime); // 1-based frame indices -> 0-based
        normalizedTime.ConvertTo(normalizedTime, MatType.CV_32FC1, 1.0 / denominator); // -> 0..1

        using var hueFloat = new Mat();
        Cv2.Subtract(new Scalar(1.0), normalizedTime, hueFloat); // invert so time=0 -> 1, time=1 -> 0
        using var hue = new Mat();
        hueFloat.ConvertTo(hue, MatType.CV_8UC1, (startHue - endHue) / 1.0, endHue);

        using var saturation = new Mat(maxGray.Size(), MatType.CV_8UC1, Scalar.All(255));
        using var hsv = new Mat();
        Cv2.Merge(new[] { hue, saturation, maxGray }, hsv);

        var bgr = new Mat();
        Cv2.CvtColor(hsv, bgr, ColorConversionCodes.HSV2BGR);
        return bgr;
    }

    private static byte[] Encode(Mat image)
    {
        Cv2.ImEncode(".png", image, out var bytes);
        return bytes;
    }
}
