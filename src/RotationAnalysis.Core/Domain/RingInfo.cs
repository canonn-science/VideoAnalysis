namespace RotationAnalysis.Core.Domain;

/// <summary>A single ring or belt found in a system, with its estimated rotation already computed.</summary>
public sealed class RingInfo
{
    public required string SystemName { get; init; }
    public required long SystemId64 { get; init; }
    public required double SystemX { get; init; }
    public required double SystemY { get; init; }
    public required double SystemZ { get; init; }

    public required string BodyName { get; init; }
    public required string RingName { get; init; }
    public required bool IsBelt { get; init; }
    public required string MaterialType { get; init; }

    public required double InnerRadiusMeters { get; init; }
    public required double OuterRadiusMeters { get; init; }
    public double WidthMeters => OuterRadiusMeters - InnerRadiusMeters;
    public double NominalRadiusMeters => RingMath.NominalRadiusMeters(InnerRadiusMeters, OuterRadiusMeters);

    /// <summary>Null if the parent body's mass could not be determined from the dump.</summary>
    public double? EstimatedPeriodSeconds { get; init; }

    public int? SuggestedVideoDurationMinutes =>
        EstimatedPeriodSeconds is double s ? RingMath.SuggestedVideoDurationMinutes(s) : null;

    public string DisplayKind => IsBelt ? $"Belt ({MaterialType})" : $"Ring ({MaterialType})";
}
