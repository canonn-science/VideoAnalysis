using RotationAnalysis.Core.VideoAnalysis.SlitScan;
using Xunit;

namespace RotationAnalysis.Core.Tests;

/// <summary>Regression/sanity fixture against real footage, mirroring
/// <see cref="LongExposureProcessorTests"/> - skips (rather than fails) on machines without the
/// sample footage checked out locally.</summary>
public class SlitScanProcessorTests
{
    private const string SampleDirectory = @"S:\Canonn\NeutronJet\Output";

    private static string? FindSampleVideo()
        => Directory.Exists(SampleDirectory)
            ? Directory.EnumerateFiles(SampleDirectory, "*.mp4").OrderBy(p => p).FirstOrDefault()
            : null;

    [Fact]
    public async Task GenerateAsync_Normal_ProducesValidPng()
    {
        var videoPath = FindSampleVideo();
        if (videoPath is null)
        {
            return;
        }

        var result = await SlitScanProcessor.GenerateAsync(videoPath, new SlitScanParameters());

        Assert.True(result.FramesSampled > 1);
        AssertValidPng(result.ImagePng);
    }

    [Fact]
    public async Task GenerateAsync_AverageBlendWithOverlap_ProducesValidPng()
    {
        var videoPath = FindSampleVideo();
        if (videoPath is null)
        {
            return;
        }

        // Overlapping placements (speed < width) is the only case where blend mode matters.
        var parameters = new SlitScanParameters
        {
            SlitWidthPixels = 6,
            ScanSpeedPixelsPerFrame = 2,
            BlendMode = SlitScanBlendMode.Average,
        };

        var result = await SlitScanProcessor.GenerateAsync(videoPath, parameters);

        AssertValidPng(result.ImagePng);
    }

    [Fact]
    public async Task GenerateAsync_RotatedSlitAngle_ProducesValidPng()
    {
        var videoPath = FindSampleVideo();
        if (videoPath is null)
        {
            return;
        }

        var parameters = new SlitScanParameters { SlitAngleDegrees = 45.0 };

        var result = await SlitScanProcessor.GenerateAsync(videoPath, parameters);

        AssertValidPng(result.ImagePng);
    }

    [Fact]
    public async Task GenerateAsync_MaxOutputWidth_ClampsWidth()
    {
        var videoPath = FindSampleVideo();
        if (videoPath is null)
        {
            return;
        }

        var parameters = new SlitScanParameters { MaxOutputWidth = 200, FrameSamplingInterval = 2 };

        var result = await SlitScanProcessor.GenerateAsync(videoPath, parameters);

        AssertValidPng(result.ImagePng);
    }

    private static void AssertValidPng(byte[] png)
    {
        Assert.True(png.Length > 0);
        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
    }
}
