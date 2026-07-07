using CsvHelper.Configuration.Attributes;

namespace RotationAnalysis.Core.Storage;

/// <summary>One row of the measurements CSV. Column names/order match DESIGN.md exactly.</summary>
public sealed class MeasurementRecord
{
    [Name("Timestamp")]
    public DateTime Timestamp { get; set; }

    [Name("System Name")]
    public string SystemName { get; set; } = string.Empty;

    [Name("id64")]
    public long Id64 { get; set; }

    [Name("x")]
    public double X { get; set; }

    [Name("y")]
    public double Y { get; set; }

    [Name("z")]
    public double Z { get; set; }

    [Name("Body Name")]
    public string BodyName { get; set; } = string.Empty;

    [Name("Ring Name")]
    public string RingName { get; set; } = string.Empty;

    [Name("innerRadius")]
    public double InnerRadius { get; set; }

    [Name("outerRadius")]
    public double OuterRadius { get; set; }

    [Name("Width")]
    public double Width { get; set; }

    /// <summary>Kepler-estimated rotation period, in seconds.</summary>
    [Name("estimated rotation")]
    public double EstimatedRotationSeconds { get; set; }

    /// <summary>Video-measured rotation period, in seconds.</summary>
    [Name("observed rotation")]
    public double ObservedRotationSeconds { get; set; }

    [Name("video filename")]
    public string VideoFilename { get; set; } = string.Empty;

    /// <summary>Whether this app has submitted this measurement to Canonn. This is a local hint
    /// only - the authoritative check is matching against Canonn's published TSV, since a
    /// measurement can also have been submitted from another machine or app version.</summary>
    [Name("submitted")]
    public bool Submitted { get; set; }
}
