namespace VideoAnalysis.Core.Domain;

/// <summary>
/// Physics helpers for estimating a ring/belt's rotational period.
///
/// The "nominal radius" is a representative orbital radius picked 3/8 of the way
/// between the ring's inner and outer edge, used as the single radius fed into
/// Kepler's third law to get an estimated period.
/// </summary>
public static class RingMath
{
    public const double GravitationalConstant = 6.674e-11; // m^3 kg^-1 s^-2
    public const double SolarMassKg = 1.98892e30;
    public const double EarthMassKg = 5.9722e24;
    public const double SolarRadiusKm = 695_700.0;

    /// <summary>A recording needs to cover roughly this fraction of a full rotation (10 degrees,
    /// i.e. 1/36th of the period) before the horizontal drift a star traces is large enough to fit
    /// reliably above pixel-tracking noise. Drives both the pre-recording suggestion
    /// (<see cref="SuggestedVideoDurationMinutes"/>) and the post-recording minimum-duration gate
    /// (<see cref="MinimumReliableVideoDurationSeconds"/>) so the two stay in lockstep.</summary>
    private const double SuggestedDurationDivisor = 36.0;

    public static double NominalRadiusMeters(double innerRadiusMeters, double outerRadiusMeters)
        => innerRadiusMeters + (outerRadiusMeters - innerRadiusMeters) * (3.0 / 8.0);

    /// <summary>Orbital period via Kepler's third law: T = 2*pi*sqrt(r^3 / (G*M)).</summary>
    public static double KeplerPeriodSeconds(double nominalRadiusMeters, double parentMassKg)
        => 2.0 * Math.PI * Math.Sqrt(Math.Pow(nominalRadiusMeters, 3) / (GravitationalConstant * parentMassKg));

    /// <summary>Suggested recording duration: period/36, rounded up to the nearest whole minute.</summary>
    public static int SuggestedVideoDurationMinutes(double periodSeconds)
        => (int)Math.Ceiling(periodSeconds / SuggestedDurationDivisor / 60.0);

    /// <summary>The precise (unrounded) floor below which a recording is too short for a reliable
    /// rotation reading - same period/36 derivation as <see cref="SuggestedVideoDurationMinutes"/>,
    /// kept as raw seconds instead of a whole-minute display figure since gating logic shouldn't
    /// round an already-borderline recording up in the user's favor.</summary>
    public static double MinimumReliableVideoDurationSeconds(double periodSeconds)
        => periodSeconds / SuggestedDurationDivisor;
}
