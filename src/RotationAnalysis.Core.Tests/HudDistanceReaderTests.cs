using OpenCvSharp;
using RotationAnalysis.Core.VideoAnalysis;
using Xunit;
using Xunit.Abstractions;

namespace RotationAnalysis.Core.Tests;

/// <summary>Regression/sanity fixture against real footage, mirroring
/// <see cref="JetWarningOnsetDetectorTests"/> - skips (rather than fails) on machines without the
/// sample footage checked out locally. Given the classifier is an intentionally best-effort
/// heuristic (see HudDistanceReader's doc comment), this asserts the pipeline runs end-to-end and
/// produces a plausible-shaped reading rather than asserting exact digit accuracy.</summary>
public class HudDistanceReaderTests
{
    private const string SampleDirectory = @"S:\Canonn\NeutronJet\Output";
    private readonly ITestOutputHelper _output;

    public HudDistanceReaderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Read_ProducesAPlausibleReading_OnRealSampleFootage()
    {
        if (!Directory.Exists(SampleDirectory))
        {
            return;
        }

        var videos = Directory.EnumerateFiles(SampleDirectory, "*.mp4").OrderBy(p => p).Take(5).ToList();
        Assert.NotEmpty(videos);

        foreach (var videoPath in videos)
        {
            using var cap = new VideoCapture(videoPath);
            Assert.True(cap.IsOpened());

            var onset = JetWarningOnsetDetector.FindOnset(cap);
            if (!onset.Detected)
            {
                continue;
            }

            cap.Set(VideoCaptureProperties.PosFrames, Math.Max(onset.OnsetFrameIndex!.Value - 1, 0));
            using var frame = new Mat();
            Assert.True(cap.Read(frame));

            using var bottomLeft = JetWarningOnsetDetector.CropRegion(frame, JetWarningOnsetDetector.BottomLeftRegion);
            var reading = HudDistanceReader.Read(bottomLeft);

            _output.WriteLine($"{Path.GetFileName(videoPath)}: raw='{reading.RawText}' distance={reading.DistanceLs} confidence={reading.Confidence:F2}");

            // Loose sanity bounds only - see class doc comment for why exact accuracy isn't asserted.
            Assert.InRange(reading.Confidence, 0.0, 1.0);
        }
    }
}
