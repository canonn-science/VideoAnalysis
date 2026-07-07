namespace RotationAnalysis.Core.Canonn;

/// <summary>One row parsed from Canonn's published TSV of already-submitted measurements.</summary>
public sealed class CanonnSubmittedMeasurement
{
    public string CommanderName { get; init; } = string.Empty;
    public string SystemName { get; init; } = string.Empty;
    public string BodyName { get; init; } = string.Empty;
    public string RingName { get; init; } = string.Empty;
    public double InnerRadiusKm { get; init; }
    public double OuterRadiusKm { get; init; }
    public double ObservedPeriodSeconds { get; init; }
}
