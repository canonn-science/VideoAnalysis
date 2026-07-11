using OpenCvSharp;

namespace RotationAnalysis.Core.VideoAnalysis;

public sealed class JetConeAnalysisResult
{
    public required bool OnsetDetected { get; init; }
    public byte[] BottomLeftCropPng { get; init; } = Array.Empty<byte>();
    public byte[] ReticleCropPng { get; init; } = Array.Empty<byte>();
    public double? LocalDistanceLs { get; init; }
    public double LocalConfidence { get; init; }
}

/// <summary>
/// Orchestrates the jet-cone video -> distance-reading pipeline off the UI thread: find the
/// warning-overlay onset frame, crop both HUD regions there, and run the local distance reader
/// against the (cleaner) bottom-left region. Unlike <see cref="HorizontalVideoAnalyzer"/> this
/// never throws for a normal "nothing found" outcome - a video with no detectable onset is a
/// valid result (<see cref="JetConeAnalysisResult.OnsetDetected"/> false), not a failure, since
/// the user will always review the crop (or lack of one) before anything is saved.
/// </summary>
public static class JetConeAnalyzer
{
    /// <summary>Both HUD regions are a small fraction of the source frame - displayed at native
    /// crop resolution they're too small to read comfortably in the review dialog. Upscaling here
    /// (display only; <see cref="HudDistanceReader"/> reads the un-upscaled crop and does its own
    /// internal upscaling) means the review dialog always shows a sharp, cubic-interpolated crop
    /// rather than relying on the WPF Image control to stretch a tiny bitmap.</summary>
    private const double DisplayUpscaleFactor = 4.0;

    public static Task<JetConeAnalysisResult> AnalyzeAsync(
        string videoPath, IProgress<VideoAnalysisProgress>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.Opening, 0, "Opening video…"));
            using var cap = new VideoCapture(videoPath);
            if (!cap.IsOpened())
            {
                throw new InvalidOperationException($"Could not open video file: {videoPath}");
            }

            var onset = JetWarningOnsetDetector.FindOnset(cap, progress, ct);
            if (!onset.Detected)
            {
                progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.Done, 100, "Done"));
                return new JetConeAnalysisResult { OnsetDetected = false };
            }

            cap.Set(VideoCaptureProperties.PosFrames, Math.Max(onset.OnsetFrameIndex!.Value - 1, 0));
            using var frame = new Mat();
            if (!cap.Read(frame) || frame.Empty())
            {
                progress?.Report(new VideoAnalysisProgress(VideoAnalysisStage.Done, 100, "Done"));
                return new JetConeAnalysisResult { OnsetDetected = false };
            }

            using var bottomLeft = JetWarningOnsetDetector.CropRegion(frame, JetWarningOnsetDetector.BottomLeftRegion);
            using var reticle = JetWarningOnsetDetector.CropRegion(frame, JetWarningOnsetDetector.ReticleRegion);

            var reading = HudDistanceReader.Read(bottomLeft);

            using var bottomLeftDisplay = UpscaleForDisplay(bottomLeft);
            using var reticleDisplay = UpscaleForDisplay(reticle);
            Cv2.ImEncode(".png", bottomLeftDisplay, out var bottomLeftBytes);
            Cv2.ImEncode(".png", reticleDisplay, out var reticleBytes);

            progress?.Report(new VideoAnalysisProgress(
                VideoAnalysisStage.Done, 100, "Done", PreviewImageBytes: reticleBytes));

            return new JetConeAnalysisResult
            {
                OnsetDetected = true,
                BottomLeftCropPng = bottomLeftBytes,
                ReticleCropPng = reticleBytes,
                LocalDistanceLs = reading.DistanceLs,
                LocalConfidence = reading.Confidence,
            };
        }, ct);
    }

    private static Mat UpscaleForDisplay(Mat crop)
    {
        var upscaled = new Mat();
        Cv2.Resize(crop, upscaled, new Size(), DisplayUpscaleFactor, DisplayUpscaleFactor, InterpolationFlags.Cubic);
        return upscaled;
    }
}
