using CsvHelper.Configuration.Attributes;

namespace VideoAnalysis.Core.Storage;

/// <summary>One row of the jet-length CSV. Column set and column names match
/// <c>S:\Canonn\NeutronJet\jetlength.csv</c> exactly (its <c>CSV_FIELDS</c> list: systemName,
/// bodyName, distance, then the 19 <c>BODY_CSV_FIELDS</c> copied verbatim from the matched Spansh
/// body) - no Timestamp/video-filename columns beyond that, since the spec asked this app's
/// output to use "the same format and values" as the existing tool.</summary>
public sealed class JetLengthRecord
{
    [Name("systemName")]
    public string SystemName { get; set; } = string.Empty;

    [Name("bodyName")]
    public string BodyName { get; set; } = string.Empty;

    /// <summary>Light seconds - the jet-cone-onset distance read from the HUD.</summary>
    [Name("distance")]
    public double Distance { get; set; }

    [Name("absoluteMagnitude")]
    public double? AbsoluteMagnitude { get; set; }

    [Name("age")]
    public double? Age { get; set; }

    [Name("argOfPeriapsis")]
    public double? ArgOfPeriapsis { get; set; }

    [Name("ascendingNode")]
    public double? AscendingNode { get; set; }

    [Name("axialTilt")]
    public double? AxialTilt { get; set; }

    [Name("bodyId")]
    public int? BodyId { get; set; }

    [Name("distanceToArrival")]
    public double? DistanceToArrival { get; set; }

    [Name("luminosity")]
    public string? Luminosity { get; set; }

    [Name("mainStar")]
    public bool? MainStar { get; set; }

    [Name("meanAnomaly")]
    public double? MeanAnomaly { get; set; }

    [Name("orbitalEccentricity")]
    public double? OrbitalEccentricity { get; set; }

    [Name("orbitalInclination")]
    public double? OrbitalInclination { get; set; }

    [Name("orbitalPeriod")]
    public double? OrbitalPeriod { get; set; }

    [Name("rotationalPeriod")]
    public double? RotationalPeriod { get; set; }

    [Name("semiMajorAxis")]
    public double? SemiMajorAxis { get; set; }

    [Name("solarMasses")]
    public double? SolarMasses { get; set; }

    [Name("solarRadius")]
    public double? SolarRadius { get; set; }

    [Name("spectralClass")]
    public string? SpectralClass { get; set; }

    [Name("surfaceTemperature")]
    public double? SurfaceTemperature { get; set; }

    [Name("updateTime")]
    public string? UpdateTime { get; set; }

    /// <summary>Whether this app has submitted this measurement to Canonn. Local hint only, same
    /// as <see cref="MeasurementRecord.Submitted"/> - added on top of the NeutronJet tool's original
    /// column set, but old rows without it just read back as <c>false</c> (CsvHelper is configured
    /// with <c>MissingFieldFound = null</c>).</summary>
    [Name("submitted")]
    public bool Submitted { get; set; }
}
