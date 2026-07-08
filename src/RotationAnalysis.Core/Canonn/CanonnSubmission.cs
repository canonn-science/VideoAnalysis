namespace RotationAnalysis.Core.Canonn;

/// <summary>A single ring measurement ready to submit to Canonn's Google Form. Radii/width are
/// in kilometers and periods in seconds - the units the form itself expects.</summary>
public sealed class CanonnSubmission
{
    public required string CommanderName { get; init; }
    public required string SystemName { get; init; }
    public required long Id64 { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Z { get; init; }
    public required string BodyName { get; init; }
    public string? BodyType { get; init; }
    public double? BodyRadiusKm { get; init; }
    public double? BodyMassEarthMasses { get; init; }
    public required string RingName { get; init; }
    public string? RingType { get; init; }
    public required double InnerRadiusKm { get; init; }
    public required double OuterRadiusKm { get; init; }
    public required double WidthKm { get; init; }
    public required double EstimatedPeriodSeconds { get; init; }
    public required double ObservedPeriodSeconds { get; init; }
}
