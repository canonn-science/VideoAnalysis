namespace RotationAnalysis.Core.VideoAnalysis;

/// <summary>
/// Fits a single shared rotation center across many star tracks, each of which is assumed to move
/// along its own circle (fixed radius) around that common center — the physical model for a camera
/// rotating rigidly around a fixed axis while stars remain fixed in space.
///
/// Uses the linear form of the circle equation: for any two points (x1,y1) and (x2,y2) belonging to
/// the same star (so they lie on the same circle, same radius, around the shared center (cx,cy)):
///   (x1-cx)^2 + (y1-cy)^2 = (x2-cx)^2 + (y2-cy)^2
///   => 2*cx*(x2-x1) + 2*cy*(y2-y1) = (x2^2+y2^2) - (x1^2+y1^2)
/// This is linear in (cx, cy), so every consecutive frame-pair of every track contributes one
/// equation to a single 2x2 least-squares solve — far more data (and far more robust) than fitting
/// each star's circle independently and averaging the centers.
/// </summary>
public static class CircleFit
{
    public static (double Cx, double Cy) FitSharedCenter(IEnumerable<StarTrack> tracks)
    {
        double sA11 = 0, sA12 = 0, sA22 = 0, sB1 = 0, sB2 = 0;

        foreach (var track in tracks)
        {
            var xs = track.Xs;
            var ys = track.Ys;
            for (int i = 0; i < xs.Count - 1; i++)
            {
                double x1 = xs[i], y1 = ys[i], x2 = xs[i + 1], y2 = ys[i + 1];
                double a1 = 2 * (x2 - x1);
                double a2 = 2 * (y2 - y1);
                double b = (x2 * x2 + y2 * y2) - (x1 * x1 + y1 * y1);

                sA11 += a1 * a1;
                sA12 += a1 * a2;
                sA22 += a2 * a2;
                sB1 += a1 * b;
                sB2 += a2 * b;
            }
        }

        double det = sA11 * sA22 - sA12 * sA12;
        if (Math.Abs(det) < 1e-9)
        {
            throw new InvalidOperationException("Track data is degenerate (all stars moved in parallel, not around a common center); cannot fit a rotation center.");
        }

        double cx = (sB1 * sA22 - sB2 * sA12) / det;
        double cy = (sA11 * sB2 - sA12 * sB1) / det;
        return (cx, cy);
    }
}
