using System.Text.Json.Serialization;

namespace RotationAnalysis.Core.Spansh.Models;

public sealed class SpanshDumpResponse
{
    [JsonPropertyName("system")]
    public SpanshSystem System { get; set; } = new();
}

public sealed class SpanshSystem
{
    [JsonPropertyName("id64")]
    public long Id64 { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("coords")]
    public SpanshCoords Coords { get; set; } = new();

    [JsonPropertyName("bodies")]
    public List<SpanshBody> Bodies { get; set; } = new();
}

public sealed class SpanshCoords
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }
}

public sealed class SpanshBody
{
    [JsonPropertyName("bodyId")]
    public int BodyId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("subType")]
    public string? SubType { get; set; }

    /// <summary>Star mass, in solar masses. Present only on bodies of type "Star".</summary>
    [JsonPropertyName("solarMasses")]
    public double? SolarMasses { get; set; }

    /// <summary>Planet mass, in Earth masses. Present only on bodies of type "Planet".</summary>
    [JsonPropertyName("earthMasses")]
    public double? EarthMasses { get; set; }

    /// <summary>Planet radius, in kilometers. Present only on bodies of type "Planet".</summary>
    [JsonPropertyName("radius")]
    public double? Radius { get; set; }

    /// <summary>Star radius, in multiples of the Sun's radius. Present only on bodies of type "Star".</summary>
    [JsonPropertyName("solarRadius")]
    public double? SolarRadius { get; set; }

    [JsonPropertyName("rings")]
    public List<SpanshRingOrBelt>? Rings { get; set; }

    [JsonPropertyName("belts")]
    public List<SpanshRingOrBelt>? Belts { get; set; }
}

public sealed class SpanshRingOrBelt
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>Meters.</summary>
    [JsonPropertyName("innerRadius")]
    public double InnerRadius { get; set; }

    /// <summary>Meters.</summary>
    [JsonPropertyName("outerRadius")]
    public double OuterRadius { get; set; }

    [JsonPropertyName("mass")]
    public double? Mass { get; set; }
}
