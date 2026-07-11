using OpenCvSharp;
using RotationAnalysis.Core.VideoAnalysis;
using Xunit;

namespace RotationAnalysis.Core.Tests;

/// <summary>Regression fixture against real footage rather than synthetic frames - the whole
/// point of this detector is matching Elite's actual HUD rendering, which is impractical to fake
/// convincingly. Skips (rather than fails) on machines without the sample footage checked out
/// locally, since S:\Canonn\NeutronJet\Output isn't part of this repo.</summary>
public class JetWarningOnsetDetectorTests
{
    private const string SampleDirectory = @"S:\Canonn\NeutronJet\Output";

    [Fact]
    public void FindOnset_DetectsWarningOverlay_OnRealSampleFootage()
    {
        if (!Directory.Exists(SampleDirectory))
        {
            return;
        }

        var videos = Directory.EnumerateFiles(SampleDirectory, "*.mp4")
            .OrderBy(p => p)
            .Take(3)
            .ToList();

        Assert.NotEmpty(videos);

        foreach (var videoPath in videos)
        {
            using var cap = new VideoCapture(videoPath);
            Assert.True(cap.IsOpened(), $"could not open {videoPath}");

            var result = JetWarningOnsetDetector.FindOnset(cap);

            Assert.True(result.Detected, $"onset not detected in {Path.GetFileName(videoPath)}");
            Assert.NotNull(result.OnsetFrameIndex);
            Assert.True(result.OnsetFrameIndex!.Value > 0);
        }
    }

    [Fact]
    public void CropRegion_ReturnsNonEmptyMat_ForBothHudRegions()
    {
        if (!Directory.Exists(SampleDirectory))
        {
            return;
        }

        var videoPath = Directory.EnumerateFiles(SampleDirectory, "*.mp4").OrderBy(p => p).FirstOrDefault();
        if (videoPath is null)
        {
            return;
        }

        using var cap = new VideoCapture(videoPath);
        Assert.True(cap.IsOpened());
        var onset = JetWarningOnsetDetector.FindOnset(cap);
        Assert.True(onset.Detected);

        cap.Set(VideoCaptureProperties.PosFrames, Math.Max(onset.OnsetFrameIndex!.Value - 1, 0));
        using var frame = new Mat();
        Assert.True(cap.Read(frame));

        using var reticle = JetWarningOnsetDetector.CropRegion(frame, JetWarningOnsetDetector.ReticleRegion);
        using var bottomLeft = JetWarningOnsetDetector.CropRegion(frame, JetWarningOnsetDetector.BottomLeftRegion);

        Assert.False(reticle.Empty());
        Assert.False(bottomLeft.Empty());
    }
}
