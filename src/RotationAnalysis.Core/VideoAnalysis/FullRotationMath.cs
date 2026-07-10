namespace RotationAnalysis.Core.VideoAnalysis;

/// <summary>
/// Pure numerical core of the full-rotation alignment measurement: given a series of
/// (time, horizontal offset) samples produced by phase-correlating candidate frames against a
/// reference frame, finds the sub-frame instant the offset crosses zero (the re-alignment event),
/// and aggregates several such crossings into a single period estimate with an uncertainty.
///
/// Kept free of any video/OpenCV dependency so it can be tested directly against synthetic
/// (t, offset) arrays, the same way <see cref="HorizontalRotationSolver"/> is tested against
/// synthetic <see cref="StarTrack"/> data instead of a real video.
/// </summary>
public static class FullRotationMath
{
    /// <summary>How many samples on each side of the sign change to include in the local linear
    /// fit used to interpolate the zero crossing.</summary>
    private const int NeighborhoodRadius = 3;

    /// <summary>
    /// Locates the zero crossing of <paramref name="offsets"/> vs <paramref name="times"/> (both
    /// assumed sorted ascending by time) via a local linear fit around the sign change, rather
    /// than just taking the sample with the smallest offset.
    /// </summary>
    /// <returns>
    /// TMatch: interpolated crossing time (NaN if none found).
    /// R2: coefficient of determination of the local linear fit (0 if none found).
    /// AtEdge: true if no crossing was found at all, or the crossing sits within one neighborhood
    /// of either end of the series - both indicate the search window was likely mis-sized.
    /// </returns>
    public static (double TMatch, double R2, bool AtEdge) FitZeroCrossing(
        IReadOnlyList<double> times, IReadOnlyList<double> offsets)
    {
        int n = times.Count;
        if (n < 2 || n != offsets.Count)
        {
            return (double.NaN, 0, true);
        }

        int crossingIndex = -1;
        for (int i = 0; i < n - 1; i++)
        {
            if (offsets[i] == 0 || (offsets[i] < 0) != (offsets[i + 1] < 0))
            {
                crossingIndex = i;
                break;
            }
        }

        if (crossingIndex < 0)
        {
            return (double.NaN, 0, true);
        }

        bool atEdge = crossingIndex < NeighborhoodRadius || crossingIndex > n - 2 - NeighborhoodRadius;

        int lo = Math.Max(0, crossingIndex - NeighborhoodRadius + 1);
        int hi = Math.Min(n - 1, crossingIndex + NeighborhoodRadius);
        int count = hi - lo + 1;

        double meanT = 0, meanO = 0;
        for (int i = lo; i <= hi; i++)
        {
            meanT += times[i];
            meanO += offsets[i];
        }
        meanT /= count;
        meanO /= count;

        double sumTT = 0, sumTO = 0, ssTot = 0;
        for (int i = lo; i <= hi; i++)
        {
            double dt = times[i] - meanT;
            double dOffset = offsets[i] - meanO;
            sumTT += dt * dt;
            sumTO += dt * dOffset;
            ssTot += dOffset * dOffset;
        }

        if (sumTT < 1e-12)
        {
            return (double.NaN, 0, true);
        }

        double slope = sumTO / sumTT;
        double intercept = meanO - slope * meanT;

        if (Math.Abs(slope) < 1e-12)
        {
            return (double.NaN, 0, true);
        }

        double ssRes = 0;
        for (int i = lo; i <= hi; i++)
        {
            double resid = offsets[i] - (intercept + slope * times[i]);
            ssRes += resid * resid;
        }
        double r2 = ssTot > 1e-12 ? 1 - ssRes / ssTot : 1.0;

        double tMatch = -intercept / slope;
        return (tMatch, r2, atEdge);
    }

    /// <summary>Median of the samples, with the median absolute deviation (scaled by 1.4826 so it
    /// estimates a standard deviation for normally-distributed samples) as the uncertainty.</summary>
    public static (double Median, double UncertaintySeconds) Aggregate(IReadOnlyList<double> periodSamples)
    {
        var sorted = periodSamples.OrderBy(x => x).ToList();
        double median = Median(sorted);

        var absDeviations = sorted.Select(x => Math.Abs(x - median)).OrderBy(x => x).ToList();
        double mad = Median(absDeviations);
        double uncertainty = mad * 1.4826;

        return (median, uncertainty);
    }

    private static double Median(List<double> sorted)
    {
        int n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }
}
