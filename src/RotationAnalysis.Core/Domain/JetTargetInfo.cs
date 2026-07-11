namespace RotationAnalysis.Core.Domain;

/// <summary>A neutron star or white dwarf in a system - a candidate jet-cone target. Unlike
/// <see cref="RingInfo"/>/<see cref="StationInfo"/> there's no estimated-period/suggested-video-
/// length concept here: the user free-flies toward the star and records themselves, so this is
/// just the body identity needed to file the resulting measurement.</summary>
public sealed class JetTargetInfo
{
    public required string SystemName { get; init; }
    public required long SystemId64 { get; init; }
    public required double SystemX { get; init; }
    public required double SystemY { get; init; }
    public required double SystemZ { get; init; }

    public required string BodyName { get; init; }

    /// <summary>"Neutron Star" or "White Dwarf (DA/DB/DC/...) Star", as reported by Spansh.</summary>
    public required string BodyType { get; init; }

    public double? AbsoluteMagnitude { get; init; }
    public double? Age { get; init; }
    public double? ArgOfPeriapsis { get; init; }
    public double? AscendingNode { get; init; }
    public double? AxialTilt { get; init; }
    public int? BodyId { get; init; }
    public double? DistanceToArrival { get; init; }
    public string? Luminosity { get; init; }
    public bool? MainStar { get; init; }
    public double? MeanAnomaly { get; init; }
    public double? OrbitalEccentricity { get; init; }
    public double? OrbitalInclination { get; init; }
    public double? OrbitalPeriod { get; init; }
    public double? RotationalPeriod { get; init; }
    public double? SemiMajorAxis { get; init; }
    public double? SolarMasses { get; init; }
    public double? SolarRadius { get; init; }
    public string? SpectralClass { get; init; }
    public double? SurfaceTemperature { get; init; }
    public string? UpdateTime { get; init; }

    public bool IsNeutronStar => BodyType.StartsWith("Neutron Star", StringComparison.OrdinalIgnoreCase);
}
