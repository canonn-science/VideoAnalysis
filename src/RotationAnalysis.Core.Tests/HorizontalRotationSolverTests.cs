using RotationAnalysis.Core.VideoAnalysis;
using Xunit;

namespace RotationAnalysis.Core.Tests;

public class HorizontalRotationSolverTests
{
    private const double FrameWidth = 2560;
    private const double FrameHeight = 1440;

    /// <summary>Generates tracks with x = f*tan(azimuth), y = tan(elevation)*sqrt(f^2+x^2) (the
    /// exact level-frame relationship), then rotates the whole frame by <paramref name="rollRadians"/>
    /// to simulate an imperfectly level recording.</summary>
    private static List<StarTrack> GenerateSyntheticTracks(
        double f, double omega, double rollRadians, IReadOnlyList<(double Phi0, double Elevation)> stars,
        double fps, int frameCount)
    {
        double px0 = FrameWidth / 2.0;
        double py0 = FrameHeight / 2.0;
        double cosRoll = Math.Cos(rollRadians);
        double sinRoll = Math.Sin(rollRadians);

        var tracks = new List<StarTrack>();
        for (int id = 0; id < stars.Count; id++)
        {
            var (phi0, elevation) = stars[id];
            var track = new StarTrack { Id = id };
            for (int k = 0; k < frameCount; k++)
            {
                double t = k / fps;
                double phi = phi0 + omega * t;
                double xLevel = f * Math.Tan(phi);
                double yLevel = Math.Tan(elevation) * Math.Sqrt(f * f + xLevel * xLevel);

                // apply roll (rotate the level-frame point about the image center), then re-center to pixels
                double xRolled = xLevel * cosRoll - yLevel * sinRoll;
                double yRolled = xLevel * sinRoll + yLevel * cosRoll;
                double xPixel = xRolled + px0;
                double yPixel = yRolled + py0;

                if (xPixel < 0 || xPixel > FrameWidth || yPixel < 0 || yPixel > FrameHeight)
                {
                    continue;
                }
                track.FrameIndices.Add(k);
                track.Xs.Add((float)xPixel);
                track.Ys.Add((float)yPixel);
            }
            if (track.Xs.Count >= 20)
            {
                tracks.Add(track);
            }
        }
        return tracks;
    }

    [Fact]
    public void Solve_RecoversKnownPeriodAndFocalLength_NoRoll()
    {
        const double trueF = 1347.0;
        const double truePeriod = 874.0;
        const double fps = 27.0;
        const int frameCount = 630;

        double omega = 2 * Math.PI / truePeriod;
        var rand = new Random(42);
        var stars = Enumerable.Range(0, 40)
            .Select(_ => ((rand.NextDouble() - 0.5) * 1.4, (rand.NextDouble() - 0.5) * 0.4))
            .ToList();

        var tracks = GenerateSyntheticTracks(trueF, omega, rollRadians: 0.0, stars, fps, frameCount);
        Assert.True(tracks.Count > 10, "expected a reasonable number of synthetic stars to stay in frame");

        var result = HorizontalRotationSolver.Solve(tracks, fps, FrameWidth, FrameHeight, seedF: 1300.0, seedPeriod: 700.0);

        double relativeError = Math.Abs(result.Period - truePeriod) / truePeriod;
        Assert.True(relativeError < 0.01, $"expected period near {truePeriod}s, got {result.Period}s");

        double fRelativeError = Math.Abs(result.F - trueF) / trueF;
        Assert.True(fRelativeError < 0.02, $"expected f near {trueF}, got {result.F}");

        Assert.True(Math.Abs(result.RollDegrees) < 1.0, $"expected ~0 roll, got {result.RollDegrees} deg");
    }

    [Fact]
    public void Solve_RecoversPeriod_EvenWhenSeedDirectionIsWrong()
    {
        const double trueF = 1347.0;
        const double truePeriod = 900.0;
        const double fps = 26.4;
        const int frameCount = 630;

        // negative omega: rotation the opposite direction from what a naive positive seed would assume
        double omega = -2 * Math.PI / truePeriod;
        var rand = new Random(7);
        var stars = Enumerable.Range(0, 40)
            .Select(_ => ((rand.NextDouble() - 0.5) * 1.4, (rand.NextDouble() - 0.5) * 0.4))
            .ToList();

        var tracks = GenerateSyntheticTracks(trueF, omega, rollRadians: 0.0, stars, fps, frameCount);
        Assert.True(tracks.Count > 10);

        // seed period is positive; solver must try both signs internally to find this
        var result = HorizontalRotationSolver.Solve(tracks, fps, FrameWidth, FrameHeight, seedF: 1347.0, seedPeriod: 900.0);

        double relativeError = Math.Abs(result.Period - truePeriod) / truePeriod;
        Assert.True(relativeError < 0.01, $"expected period near {truePeriod}s, got {result.Period}s");
    }

    [Theory]
    [InlineData(5.0)]
    [InlineData(-8.0)]
    public void Solve_RecoversPeriodAndRoll_WhenCameraIsTilted(double rollDegrees)
    {
        const double trueF = 1347.0;
        const double truePeriod = 900.0;
        const double fps = 26.4;
        const int frameCount = 630;
        double rollRadians = rollDegrees * Math.PI / 180.0;

        double omega = 2 * Math.PI / truePeriod;
        var rand = new Random(11);
        // include a spread of elevations so the roll signal (consistent across all stars) is
        // distinguishable from each star's own small elevation-dependent curvature
        var stars = Enumerable.Range(0, 60)
            .Select(_ => ((rand.NextDouble() - 0.5) * 1.4, (rand.NextDouble() - 0.5) * 0.5))
            .ToList();

        var tracks = GenerateSyntheticTracks(trueF, omega, rollRadians, stars, fps, frameCount);
        Assert.True(tracks.Count > 10);

        var result = HorizontalRotationSolver.Solve(tracks, fps, FrameWidth, FrameHeight, seedF: 1347.0, seedPeriod: 700.0);

        double relativeError = Math.Abs(result.Period - truePeriod) / truePeriod;
        Assert.True(relativeError < 0.01, $"expected period near {truePeriod}s, got {result.Period}s");
        Assert.True(Math.Abs(result.RollDegrees - rollDegrees) < 0.5, $"expected roll near {rollDegrees} deg, got {result.RollDegrees} deg");
    }
}
