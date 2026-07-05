namespace RotationAnalysis.Core.VideoAnalysis;

/// <summary>Minimal dependency-free Nelder-Mead simplex minimizer, used where the rotation
/// model has no closed-form solution (a shared rate + per-star phase fit to noisy trajectories).</summary>
public static class NelderMead
{
    public static (double[] Best, double BestCost) Minimize(
        Func<double[], double> f, double[] x0, double[] step, double xatol = 1e-7, double fatol = 1e-13, int maxIter = 3000,
        CancellationToken ct = default, Action<int, int>? onIteration = null)
    {
        int n = x0.Length;
        var simplex = new double[n + 1][];
        simplex[0] = (double[])x0.Clone();
        for (int i = 0; i < n; i++)
        {
            var p = (double[])x0.Clone();
            p[i] += step[i];
            simplex[i + 1] = p;
        }
        var fvals = new double[n + 1];
        for (int i = 0; i <= n; i++)
        {
            fvals[i] = f(simplex[i]);
        }

        const double alpha = 1.0, gamma = 2.0, rho = 0.5, sigma = 0.5;

        for (int iter = 0; iter < maxIter; iter++)
        {
            if (iter % 20 == 0)
            {
                ct.ThrowIfCancellationRequested();
                onIteration?.Invoke(iter, maxIter);
            }

            var order = Enumerable.Range(0, n + 1).OrderBy(i => fvals[i]).ToArray();
            simplex = order.Select(i => simplex[i]).ToArray();
            fvals = order.Select(i => fvals[i]).ToArray();

            double maxDiff = 0;
            for (int i = 1; i <= n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    maxDiff = Math.Max(maxDiff, Math.Abs(simplex[i][j] - simplex[0][j]));
                }
            }
            if (maxDiff < xatol && (fvals[n] - fvals[0]) < fatol)
            {
                break;
            }

            var centroid = new double[n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    centroid[j] += simplex[i][j];
                }
            }
            for (int j = 0; j < n; j++)
            {
                centroid[j] /= n;
            }

            var worst = simplex[n];
            var xr = Reflect(centroid, worst, alpha);
            double fr = f(xr);

            if (fvals[0] <= fr && fr < fvals[n - 1])
            {
                simplex[n] = xr; fvals[n] = fr;
            }
            else if (fr < fvals[0])
            {
                var xe = Reflect(centroid, worst, alpha * gamma);
                double fe = f(xe);
                if (fe < fr) { simplex[n] = xe; fvals[n] = fe; }
                else { simplex[n] = xr; fvals[n] = fr; }
            }
            else
            {
                var xc = Reflect(centroid, worst, -rho);
                double fc = f(xc);
                if (fc < fvals[n])
                {
                    simplex[n] = xc; fvals[n] = fc;
                }
                else
                {
                    for (int i = 1; i <= n; i++)
                    {
                        for (int j = 0; j < n; j++)
                        {
                            simplex[i][j] = simplex[0][j] + sigma * (simplex[i][j] - simplex[0][j]);
                        }
                        fvals[i] = f(simplex[i]);
                    }
                }
            }
        }

        var bestOrder = Enumerable.Range(0, n + 1).OrderBy(i => fvals[i]).First();
        return (simplex[bestOrder], fvals[bestOrder]);
    }

    private static double[] Reflect(double[] centroid, double[] worst, double factor)
    {
        int n = centroid.Length;
        var result = new double[n];
        for (int j = 0; j < n; j++)
        {
            result[j] = centroid[j] + factor * (centroid[j] - worst[j]);
        }
        return result;
    }
}
