using RotationAnalysis.Core.VideoAnalysis;
using Xunit;

namespace RotationAnalysis.Core.Tests;

public class RotationPipelineTests
{
    /// <summary>
    /// Generates synthetic star tracks that orbit a known center at a known angular velocity,
    /// mimicking what StarTracker would produce from real footage - without needing a video file.
    /// </summary>
    private static List<StarTrack> GenerateSyntheticTracks(
        double centerX, double centerY, IReadOnlyList<double> radii,
        double angularVelocityRadPerSec, double fps, int frameCount)
    {
        var tracks = new List<StarTrack>();
        for (int id = 0; id < radii.Count; id++)
        {
            double radius = radii[id];
            double phase0 = id * 0.37; // stagger starting angles like real stars would have
            var track = new StarTrack { Id = id };
            for (int f = 0; f < frameCount; f++)
            {
                double t = f / fps;
                double theta = phase0 + angularVelocityRadPerSec * t;
                track.FrameIndices.Add(f);
                track.Xs.Add((float)(centerX + radius * Math.Cos(theta)));
                track.Ys.Add((float)(centerY + radius * Math.Sin(theta)));
            }
            tracks.Add(track);
        }
        return tracks;
    }

    [Fact]
    public void CircleFitAndRotationSolver_RecoverKnownCenterAndPeriod()
    {
        const double trueCenterX = 1280.0;
        const double trueCenterY = 720.0;
        const double truePeriodSeconds = 500.0;
        const double fps = 30.0;
        const int frameCount = 750; // 25 seconds, matching the real sample clip's length

        double angularVelocity = 2 * Math.PI / truePeriodSeconds;
        var radii = Enumerable.Range(1, 50).Select(i => 100.0 + i * 20.0).ToList();
        var tracks = GenerateSyntheticTracks(trueCenterX, trueCenterY, radii, angularVelocity, fps, frameCount);

        var (cx, cy) = CircleFit.FitSharedCenter(tracks);
        Assert.Equal(trueCenterX, cx, precision: 1);
        Assert.Equal(trueCenterY, cy, precision: 1);

        var rotation = RotationSolver.Solve(tracks, cx, cy, fps);
        double relativeError = Math.Abs(rotation.PeriodSeconds - truePeriodSeconds) / truePeriodSeconds;
        Assert.True(relativeError < 0.01, $"Expected period near {truePeriodSeconds}s, got {rotation.PeriodSeconds}s");
        Assert.Equal(radii.Count, rotation.UsedTrackCount);
    }

    [Fact]
    public void RotationSolver_IgnoresASingleOutlierTrack()
    {
        const double trueCenterX = 640.0;
        const double trueCenterY = 480.0;
        const double truePeriodSeconds = 300.0;
        const double fps = 30.0;
        const int frameCount = 300;

        double angularVelocity = 2 * Math.PI / truePeriodSeconds;
        var radii = Enumerable.Range(1, 30).Select(i => 50.0 + i * 15.0).ToList();
        var tracks = GenerateSyntheticTracks(trueCenterX, trueCenterY, radii, angularVelocity, fps, frameCount);

        // Corrupt one track with a wildly different (bogus) angular velocity, simulating a false detection.
        var bogus = GenerateSyntheticTracks(trueCenterX, trueCenterY, new[] { 200.0 }, angularVelocity * 20, fps, frameCount)[0];
        tracks.Add(bogus);

        var (cx, cy) = CircleFit.FitSharedCenter(tracks);
        var rotation = RotationSolver.Solve(tracks, cx, cy, fps);

        double relativeError = Math.Abs(rotation.PeriodSeconds - truePeriodSeconds) / truePeriodSeconds;
        Assert.True(relativeError < 0.02, $"Expected period near {truePeriodSeconds}s despite outlier, got {rotation.PeriodSeconds}s");
        Assert.Equal(radii.Count, rotation.UsedTrackCount); // the bogus track should have been trimmed
    }
}
