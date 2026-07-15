using OpenCvSharp;
using VideoAnalysis.Core.VideoAnalysis.SlitScan;
using Xunit;

namespace VideoAnalysis.Core.Tests;

/// <summary>Pixel-level regression coverage for the WarpAffine same-size-canvas bug (GitHub
/// issue #36): rotating into a fixed frame.Size() canvas either discarded source content that
/// rotated outside the original bounding box, or left the extracted slit column partially
/// covered by WarpAffine's black padding. Uses a synthetic solid-color frame rather than sample
/// footage so it runs everywhere - since the frame has no black content of its own, any black
/// pixel that comes back out must be padding.</summary>
public class SlitScanExtractSlitTests
{
    private static Mat CreateFrame(int width = 192, int height = 108)
        => new(new Size(width, height), MatType.CV_8UC3, new Scalar(180, 160, 140));

    private static double MinGrayValue(Mat slit)
    {
        using var gray = new Mat();
        Cv2.CvtColor(slit, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.MinMaxLoc(gray, out double min, out _);
        return min;
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(0.5)]
    [InlineData(0.95)]
    public void ExtractSlit_TopToBottomAngle_NoBlackPaddingAcrossFullPositionRange(double positionFraction)
    {
        // SlitAngleDegrees = 0 (the "Top to bottom" motion-direction preset) rotates by exactly
        // 90 degrees, which is axis-aligned: the rotated bounding box exactly matches the
        // rotated content with zero corner overhang, so every reachable position should be
        // real content - this is the 1920x1080 repro from the issue, scaled down.
        using var frame = CreateFrame();
        var parameters = new SlitScanParameters
        {
            SlitAngleDegrees = 0.0,
            SlitPositionFraction = positionFraction,
            SlitWidthPixels = 4,
        };

        using var slit = SlitScanProcessor.ExtractSlit(frame, parameters, p: 0.0);

        Assert.True(MinGrayValue(slit) > 0, $"slit at position {positionFraction} contains WarpAffine padding");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.125)]
    [InlineData(0.25)]
    [InlineData(0.375)]
    [InlineData(0.5)]
    [InlineData(0.625)]
    [InlineData(0.75)]
    [InlineData(0.875)]
    public void ExtractSlit_RotationalModeCenteredOrbit_NoBlackBandsAcrossFullSweep(double p)
    {
        // A zero-radius orbit samples straight through the (recentered) rotation center at
        // every angle. For a wider-than-tall frame, a full-height strip through the center is
        // geometrically guaranteed to stay inside the rotated rectangle at any angle, so this
        // sweep should be free of the periodic black banding the issue describes.
        using var frame = CreateFrame();
        var parameters = new SlitScanParameters
        {
            MotionMode = SlitScanMotionMode.Rotational,
            RotationCenterXFraction = 0.5,
            RotationCenterYFraction = 0.5,
            RotationRadiusFraction = 0.0,
            RotationRevolutions = 1.0,
            SlitWidthPixels = 4,
        };

        using var slit = SlitScanProcessor.ExtractSlit(frame, parameters, p);

        Assert.True(MinGrayValue(slit) > 0, $"slit at p={p} (angle {p * 360}deg) contains WarpAffine padding");
    }

    [Theory]
    [InlineData(192, 108)]
    [InlineData(108, 192)]
    public void ExtractSlit_RotationalMode_HeightIsConstantAcrossAngles(int width, int height)
    {
        // Composite() requires every slit to share the first slit's height; the rotated
        // bounding box's own height varies continuously with angle, so ExtractSlit must always
        // crop back down to frame.Height regardless of the current rotation angle.
        using var frame = CreateFrame(width, height);
        var parameters = new SlitScanParameters
        {
            MotionMode = SlitScanMotionMode.Rotational,
            RotationRadiusFraction = 0.5,
            RotationRevolutions = 2.0,
        };

        int? expectedHeight = null;
        for (double p = 0.0; p < 1.0; p += 0.1)
        {
            using var slit = SlitScanProcessor.ExtractSlit(frame, parameters, p);
            expectedHeight ??= slit.Height;
            Assert.Equal(expectedHeight!.Value, slit.Height);
        }
    }
}
