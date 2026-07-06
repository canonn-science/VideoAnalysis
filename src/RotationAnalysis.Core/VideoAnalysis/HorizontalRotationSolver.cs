namespace RotationAnalysis.Core.VideoAnalysis;

public sealed record HorizontalChunkResult(
    double Period,
    double F,
    double RollDegrees,
    double MedianOwnPeriod,
    int TracksUsed,
    int TracksTotal);

/// <summary>
/// Fits one chunk of a horizon-facing recording (ship parked on the ring, facing outward
/// toward the horizon). Validated against real footage with a known independently-measured
/// period: with the rotation axis fixed at exactly vertical (matching the physical setup -
/// standing on a flat rotating ring, your own "up" always equals the ring's rotation axis),
/// a star's horizontal pixel position depends only on its azimuth, not elevation at all:
///   x = f * tan(azimuth),  azimuth(t) = phi0 + omega*t
/// So the whole fit reduces to a shared (f, omega) plus each star's own starting phase phi0 -
/// no axis direction to fit at all. This is both simpler and far more robust than a general
/// 3-parameter (axis + focal length) fit, which occasionally converged to a nonsensical
/// axis/focal-length combination on real test footage.
///
/// The one thing that DOES still need correcting is camera roll: if the recording isn't
/// perfectly level, the whole frame is rotated by some small fixed angle relative to true
/// horizontal. Roll is a rigid rotation of the image plane, so it's exactly correctable with a
/// single shared angle - rotate every (x,y) by -roll before computing x's azimuth. This is a far
/// more constrained correction than a free 3D axis search (roll only spins the image about its
/// own center; it can't tilt the axis out of plane), so it stays just as robust as the
/// axis-fixed model while accounting for an imperfectly level recording.
/// </summary>
public static class HorizontalRotationSolver
{
    private const int MinPointsPerTrack = 20;

    /// <summary>Rough seed focal length in pixels, derived from the game's default vertical FOV.
    /// Each chunk's solve refines its own actual value starting from here.</summary>
    public const double DefaultSeedFocalLengthPx = 1347.0;

    public static HorizontalChunkResult Solve(
        IReadOnlyList<StarTrack> tracks, double fps, double frameWidth, double frameHeight, double seedF, double seedPeriod,
        CancellationToken ct = default, Action<double>? onProgress = null)
    {
        double px0 = frameWidth / 2.0;
        double py0 = frameHeight / 2.0;
        double seedOmega = 2 * Math.PI / seedPeriod;

        // Reused across every objective-function call (thousands per chunk) instead of allocating
        // fresh per-track buffers on each evaluation - only the final call's contents are ever read.
        var ownSlopes = new double[tracks.Count];
        var weights = new double[tracks.Count];

        double Objective(double[] p) => EvaluateCost(tracks, fps, px0, py0, p[0], p[1], p[2], ownSlopes, weights, out _);

        (double[] bestX, double bestCost) = (new[] { seedF, seedOmega, 0.0 }, double.MaxValue);
        int[] signs = { 1, -1 };
        for (int passIndex = 0; passIndex < signs.Length; passIndex++)
        {
            int pass = passIndex;
            var x0 = new[] { seedF, signs[pass] * seedOmega, 0.0 };
            void OnIteration(int iter, int maxIter) =>
                onProgress?.Invoke((pass + (double)iter / maxIter) / signs.Length);
            var (cand, cost) = NelderMead.Minimize(Objective, x0, new[] { 100.0, Math.Abs(seedOmega) * 0.2, 0.05 }, ct: ct, onIteration: OnIteration);
            if (cost < bestCost)
            {
                (bestX, bestCost) = (cand, cost);
            }
        }

        double f = bestX[0];
        double omega = bestX[1];
        double roll = bestX[2];
        EvaluateCost(tracks, fps, px0, py0, f, omega, roll, ownSlopes, weights, out int tracksUsed);

        var goodPeriods = new List<double>();
        for (int i = 0; i < ownSlopes.Length; i++)
        {
            if (weights[i] > 0 && Math.Abs(ownSlopes[i]) > 1e-9)
            {
                goodPeriods.Add(2 * Math.PI / Math.Abs(ownSlopes[i]));
            }
        }
        goodPeriods.Sort();
        double medianOwnPeriod = goodPeriods.Count > 0 ? Median(goodPeriods) : double.NaN;

        return new HorizontalChunkResult(
            Period: 2 * Math.PI / Math.Abs(omega),
            F: f,
            RollDegrees: roll * 180.0 / Math.PI,
            MedianOwnPeriod: medianOwnPeriod,
            TracksUsed: tracksUsed,
            TracksTotal: tracks.Count);
    }

    private static double EvaluateCost(
        IReadOnlyList<StarTrack> tracks, double fps, double px0, double py0, double f, double omega, double roll,
        double[] ownSlopes, double[] weights, out int tracksUsed)
    {
        int n = tracks.Count;

        if (f <= 100)
        {
            tracksUsed = 0;
            return 1e6;
        }

        double cosRoll = Math.Cos(roll);
        double sinRoll = Math.Sin(roll);

        for (int i = 0; i < n; i++)
        {
            var track = tracks[i];
            int m = track.Xs.Count;
            if (m < MinPointsPerTrack)
            {
                continue;
            }

            var phi = new double[m];
            var t = new double[m];
            double meanT = 0, meanPhi = 0;
            for (int k = 0; k < m; k++)
            {
                // undo camera roll (a rigid rotation of the image plane about its center)
                // before reading off the level-frame horizontal position.
                double dx = track.Xs[k] - px0;
                double dy = track.Ys[k] - py0;
                double xLevel = dx * cosRoll + dy * sinRoll;

                phi[k] = Math.Atan2(xLevel, f);
                t[k] = track.FrameIndices[k] / fps;
                meanT += t[k];
                meanPhi += phi[k];
            }
            meanT /= m;
            meanPhi /= m;

            double intercept = 0;
            for (int k = 0; k < m; k++)
            {
                intercept += phi[k] - omega * t[k];
            }
            intercept /= m;

            double ssRes = 0, ssTot = 0, sumTT = 0, sumTPhi = 0;
            for (int k = 0; k < m; k++)
            {
                double resid = phi[k] - omega * t[k] - intercept;
                ssRes += resid * resid;
                double dev = phi[k] - meanPhi;
                ssTot += dev * dev;
                double dt = t[k] - meanT;
                sumTT += dt * dt;
                sumTPhi += dt * dev;
            }

            double r2 = ssTot > 1e-12 ? 1 - ssRes / ssTot : 0;
            weights[i] = Math.Max(0, r2);
            ownSlopes[i] = sumTT > 1e-12 ? sumTPhi / sumTT : 0;
        }

        double wsum = weights.Sum();
        tracksUsed = weights.Count(w => w > 0);
        if (wsum <= 1e-9)
        {
            return 1e6;
        }

        double wvar = 0;
        for (int i = 0; i < n; i++)
        {
            double diff = ownSlopes[i] - omega;
            wvar += weights[i] * diff * diff;
        }
        return wvar / wsum;
    }

    private static double Median(List<double> sorted)
    {
        int n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }
}
