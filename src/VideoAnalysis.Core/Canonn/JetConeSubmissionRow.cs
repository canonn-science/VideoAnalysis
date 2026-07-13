using System.Text.Json.Serialization;
using VideoAnalysis.Core.Storage;

namespace VideoAnalysis.Core.Canonn;

/// <summary>One row of the Jet Cone submission payload sent to Canonn's Apps Script endpoint.
/// Mirrors <see cref="JetLengthRecord"/> field-for-field (camelCase property names match the
/// sheet's column headers exactly) plus the commander name, which the sheet expects as the first
/// column under the literal header "CMDR Name" - hence the explicit <see cref="JsonPropertyNameAttribute"/>
/// rather than relying on a camelCase naming policy for that one field.</summary>
public sealed class JetConeSubmissionRow
{
    [JsonPropertyName("CMDR Name")]
    public string CmdrName { get; init; } = string.Empty;

    public string SystemName { get; init; } = string.Empty;
    public string BodyName { get; init; } = string.Empty;
    public double Distance { get; init; }
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

    public static JetConeSubmissionRow FromRecord(JetLengthRecord record, string commanderName) => new()
    {
        CmdrName = commanderName,
        SystemName = record.SystemName,
        BodyName = record.BodyName,
        Distance = record.Distance,
        AbsoluteMagnitude = record.AbsoluteMagnitude,
        Age = record.Age,
        ArgOfPeriapsis = record.ArgOfPeriapsis,
        AscendingNode = record.AscendingNode,
        AxialTilt = record.AxialTilt,
        BodyId = record.BodyId,
        DistanceToArrival = record.DistanceToArrival,
        Luminosity = record.Luminosity,
        MainStar = record.MainStar,
        MeanAnomaly = record.MeanAnomaly,
        OrbitalEccentricity = record.OrbitalEccentricity,
        OrbitalInclination = record.OrbitalInclination,
        OrbitalPeriod = record.OrbitalPeriod,
        RotationalPeriod = record.RotationalPeriod,
        SemiMajorAxis = record.SemiMajorAxis,
        SolarMasses = record.SolarMasses,
        SolarRadius = record.SolarRadius,
        SpectralClass = record.SpectralClass,
        SurfaceTemperature = record.SurfaceTemperature,
        UpdateTime = record.UpdateTime,
    };
}
