using RotationAnalysis.Core.VideoAnalysis;
using Xunit;

namespace RotationAnalysis.Core.Tests;

public class FullRotationMathTests
{
    /// <summary>Builds synthetic (t, offset) samples the way phase correlation would near a
    /// re-alignment: offset crosses zero linearly at <paramref name="trueMatchTime"/>, with a
    /// small amount of noise, mirroring how <see cref="HorizontalRotationSolverTests"/> feeds
    /// synthetic star tracks instead of a real video.</summary>
    private static (List<double> Times, List<double> Offsets) GenerateCrossingSamples(
        double trueMatchTime, double slope, double windowStart, double windowEnd, double sampleIntervalSeconds,
        double noiseAmplitude, int seed)
    {
        var rand = new Random(seed);
        var times = new List<double>();
        var offsets = new List<double>();
        for (double t = windowStart; t <= windowEnd; t += sampleIntervalSeconds)
        {
            times.Add(t);
            double noise = (rand.NextDouble() - 0.5) * 2 * noiseAmplitude;
            offsets.Add(slope * (t - trueMatchTime) + noise);
        }
        return (times, offsets);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.02)]
    [InlineData(0.1)]
    public void FitZeroCrossing_RecoversKnownCrossingTime_WithinTargetError(double noiseAmplitude)
    {
        const double truePeriod = 900.0;
        const double trueMatchTime = 905.37; // slightly off nominal period, as a real measurement would be
        const double slope = 2.5; // px per second, typical near-crossing rate

        var (times, offsets) = GenerateCrossingSamples(
            trueMatchTime, slope, windowStart: 860, windowEnd: 950, sampleIntervalSeconds: 0.5,
            noiseAmplitude, seed: 1);

        var (tMatch, r2, atEdge) = FullRotationMath.FitZeroCrossing(times, offsets);

        Assert.False(atEdge);
        double relativeError = Math.Abs(tMatch - trueMatchTime) / truePeriod;
        Assert.True(relativeError < 0.001, $"expected <0.1% error, got {relativeError * 100:F4}% (tMatch={tMatch})");
        Assert.True(r2 > 0.9, $"expected a strong local linear fit, got R2={r2}");
    }

    [Fact]
    public void FitZeroCrossing_FlagsAtEdge_WhenNoCrossingInWindow()
    {
        // offset never reaches zero: window was too narrow / T_est was off.
        var times = Enumerable.Range(0, 40).Select(i => (double)i).ToList();
        var offsets = times.Select(t => 5.0 + 0.01 * t).ToList();

        var (tMatch, _, atEdge) = FullRotationMath.FitZeroCrossing(times, offsets);

        Assert.True(atEdge);
        Assert.True(double.IsNaN(tMatch));
    }

    [Fact]
    public void FitZeroCrossing_FlagsAtEdge_WhenCrossingRightAtWindowBoundary()
    {
        // Crossing happens in the very first couple of samples - the window likely started too late.
        var (times, offsets) = GenerateCrossingSamples(
            trueMatchTime: 100.5, slope: 1.0, windowStart: 100, windowEnd: 140, sampleIntervalSeconds: 1.0,
            noiseAmplitude: 0, seed: 2);

        var (_, _, atEdge) = FullRotationMath.FitZeroCrossing(times, offsets);

        Assert.True(atEdge);
    }

    [Fact]
    public void Aggregate_ReturnsMedianAndMadBasedUncertainty()
    {
        var samples = new List<double> { 890.0, 895.0, 900.0, 905.0, 950.0 }; // one outlier

        var (median, uncertainty) = FullRotationMath.Aggregate(samples);

        Assert.Equal(900.0, median);
        // MAD from median: |{-10,-5,0,5,50}| sorted -> {0,5,5,10,50}, median=5, scaled by 1.4826
        Assert.Equal(5.0 * 1.4826, uncertainty, precision: 4);
    }

    [Fact]
    public void Aggregate_ReturnsZeroUncertainty_WhenAllSamplesAgree()
    {
        var samples = new List<double> { 600.0, 600.0, 600.0 };

        var (median, uncertainty) = FullRotationMath.Aggregate(samples);

        Assert.Equal(600.0, median);
        Assert.Equal(0.0, uncertainty);
    }
}
