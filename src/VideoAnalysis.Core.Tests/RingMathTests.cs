using VideoAnalysis.Core.Domain;
using Xunit;

namespace VideoAnalysis.Core.Tests;

public class RingMathTests
{
    [Fact]
    public void NominalRadius_IsThreeEighthsOfTheWayFromInnerToOuter()
    {
        double result = RingMath.NominalRadiusMeters(100_000, 180_000);
        Assert.Equal(130_000, result, precision: 6);
    }

    [Fact]
    public void KeplerPeriod_MatchesEarthsOrbitalPeriod()
    {
        // Earth around the Sun: ~1.496e11 m, ~1 solar mass -> ~365.25 days.
        double periodSeconds = RingMath.KeplerPeriodSeconds(1.495978707e11, 1 * RingMath.SolarMassKg);
        double periodDays = periodSeconds / 86400.0;
        Assert.InRange(periodDays, 365.0, 365.5);
    }

    [Theory]
    [InlineData(28296.0, 14)] // 13.1 minutes worth of seconds -> rounds up to 14, per DESIGN.md's own example
    [InlineData(36.0, 1)]
    [InlineData(0.0, 0)]
    public void SuggestedVideoDuration_RoundsUpToNearestMinute(double periodSeconds, int expectedMinutes)
    {
        Assert.Equal(expectedMinutes, RingMath.SuggestedVideoDurationMinutes(periodSeconds));
    }

    [Theory]
    [InlineData(600.0, 16.666666666666668)] // the solver's default seed period -> ~16.7s floor
    [InlineData(36.0, 1.0)]
    [InlineData(0.0, 0.0)]
    public void MinimumReliableVideoDuration_IsPeriodOverThirtySix(double periodSeconds, double expectedSeconds)
    {
        Assert.Equal(expectedSeconds, RingMath.MinimumReliableVideoDurationSeconds(periodSeconds), precision: 9);
    }
}
