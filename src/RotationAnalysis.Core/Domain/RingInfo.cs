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

    /// <summary>Body subType from Spansh (falls back to the coarser type if subType is absent).</summary>
    public string? BodyType { get; init; }

    /// <summary>Body mass, converted to Earth masses. Null if the parent body's mass could not be
    /// determined from the dump.</summary>
    public double? BodyMassEarthMasses { get; init; }

    /// <summary>Body radius, converted to kilometers. Null if not present in the dump.</summary>
    public double? BodyRadiusKm { get; init; }

    /// <summary>Ring/belt mass, as reported by Spansh. Null if not present in the dump.</summary>
    public double? RingMassKg { get; init; }

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
