namespace RotationAnalysis.Core.VideoAnalysis;

public sealed record RotationResult(
    double PeriodSeconds,
    double AngularVelocityRadPerSec,
    double CenterX,
    double CenterY,
    int UsedTrackCount,
    double StdDevDegreesPerSecond);

/// <summary>
/// For each star track, fits angular velocity (unwrapped angle around the shared center vs. elapsed
/// time) via linear regression, then robustly combines all tracks' angular velocities (median, with
/// MAD-based outlier trimming) into a single rotation period. Using every track's full trajectory
/// rather than just its first/last point is what keeps this stable in a dense star field.
/// </summary>
public static class RotationSolver
{
    public static RotationResult Solve(IReadOnlyList<StarTrack> tracks, double centerX, double centerY, double fps, int minPointsPerTrack = 5)
    {
        var angularVelocities = new List<double>();

        foreach (var track in tracks)
        {
            int n = track.Xs.Count;
            if (n < minPointsPerTrack)
            {
                continue;
            }

            var thetas = new double[n];
            for (int i = 0; i < n; i++)
            {
                thetas[i] = Math.Atan2(track.Ys[i] - centerY, track.Xs[i] - centerX);
            }

            var unwrapped = new double[n];
            unwrapped[0] = thetas[0];
            for (int i = 1; i < n; i++)
            {
                double delta = Math.Atan2(Math.Sin(thetas[i] - thetas[i - 1]), Math.Cos(thetas[i] - thetas[i - 1]));
                unwrapped[i] = unwrapped[i - 1] + delta;
            }

            double sumT = 0, sumTheta = 0, sumTT = 0, sumTTheta = 0;
            for (int i = 0; i < n; i++)
            {
                double t = track.FrameIndices[i] / fps;
                sumT += t;
                sumTheta += unwrapped[i];
                sumTT += t * t;
                sumTTheta += t * unwrapped[i];
            }

            double meanT = sumT / n;
            double meanTheta = sumTheta / n;
            double denom = sumTT - n * meanT * meanT;
            if (Math.Abs(denom) < 1e-12)
            {
                continue;
            }

            double slopeRadPerSec = (sumTTheta - n * meanT * meanTheta) / denom;
            angularVelocities.Add(slopeRadPerSec);
        }

        if (angularVelocities.Count == 0)
        {
            throw new InvalidOperationException("No star track had enough points to estimate an angular velocity.");
        }

        double median = Median(angularVelocities);
        double mad = Median(angularVelocities.Select(v => Math.Abs(v - median)).ToList());
        double madThreshold = 5 * 1.4826 * mad + 1e-9;
        var trimmed = angularVelocities.Where(v => Math.Abs(v - median) < madThreshold).ToList();

        var finalSet = trimmed.Count > 0 ? trimmed : angularVelocities;
        double finalOmega = Median(finalSet);
        double periodSeconds = 2 * Math.PI / Math.Abs(finalOmega);
        double stdDev = StdDev(finalSet) * 180.0 / Math.PI;

        return new RotationResult(periodSeconds, finalOmega, centerX, centerY, finalSet.Count, stdDev);
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    private static double StdDev(IReadOnlyCollection<double> values)
    {
        if (values.Count < 2)
        {
            return 0;
        }
        double mean = values.Average();
        double sumSq = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / (values.Count - 1));
    }
}
