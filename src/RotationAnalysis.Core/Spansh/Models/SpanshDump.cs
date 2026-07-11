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

    /// <summary>Orbital stations (starports, mega ships, rescue ships) with no body link in
    /// Spansh's data - confirmed by inspecting a real dump (Sol): every body-orbiting starport
    /// (Orbis/Coriolis/Ocellus) appears only here, never in any <see cref="SpanshBody.Stations"/>
    /// list, and Spansh's JSON simply has no body/bodyId field on these entries at all. Surface
    /// stations (Planetary Outpost/Port, Settlement) appear exclusively in the owning body's own
    /// <see cref="SpanshBody.Stations"/> instead - the two lists are disjoint, not overlapping.</summary>
    [JsonPropertyName("stations")]
    public List<SpanshStation>? Stations { get; set; }
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

    /// <summary>Days.</summary>
    [JsonPropertyName("rotationalPeriod")]
    public double? RotationalPeriod { get; set; }

    /// <summary>Degrees - the body's own orbital inclination around its parent.</summary>
    [JsonPropertyName("orbitalInclination")]
    public double? OrbitalInclination { get; set; }

    // The fields below are only used by Jet Cone Length (copied verbatim into jetlength.csv,
    // matching S:\Canonn\NeutronJet\jetlength.csv's BODY_CSV_FIELDS list exactly) - Ring/Station
    // Rotation don't touch them.
    [JsonPropertyName("absoluteMagnitude")]
    public double? AbsoluteMagnitude { get; set; }

    [JsonPropertyName("age")]
    public double? Age { get; set; }

    [JsonPropertyName("argOfPeriapsis")]
    public double? ArgOfPeriapsis { get; set; }

    [JsonPropertyName("ascendingNode")]
    public double? AscendingNode { get; set; }

    [JsonPropertyName("axialTilt")]
    public double? AxialTilt { get; set; }

    [JsonPropertyName("distanceToArrival")]
    public double? DistanceToArrival { get; set; }

    [JsonPropertyName("luminosity")]
    public string? Luminosity { get; set; }

    [JsonPropertyName("mainStar")]
    public bool? MainStar { get; set; }

    [JsonPropertyName("meanAnomaly")]
    public double? MeanAnomaly { get; set; }

    [JsonPropertyName("orbitalEccentricity")]
    public double? OrbitalEccentricity { get; set; }

    [JsonPropertyName("orbitalPeriod")]
    public double? OrbitalPeriod { get; set; }

    [JsonPropertyName("semiMajorAxis")]
    public double? SemiMajorAxis { get; set; }

    [JsonPropertyName("spectralClass")]
    public string? SpectralClass { get; set; }

    /// <summary>Kelvin.</summary>
    [JsonPropertyName("surfaceTemperature")]
    public double? SurfaceTemperature { get; set; }

    [JsonPropertyName("updateTime")]
    public string? UpdateTime { get; set; }

    [JsonPropertyName("rings")]
    public List<SpanshRingOrBelt>? Rings { get; set; }

    [JsonPropertyName("belts")]
    public List<SpanshRingOrBelt>? Belts { get; set; }

    /// <summary>Surface stations (Planetary Outpost/Port, Settlement) sitting on this specific
    /// body - see the doc comment on <see cref="SpanshSystem.Stations"/> for why this is a
    /// separate list from the system-level one rather than a filtered view of it.</summary>
    [JsonPropertyName("stations")]
    public List<SpanshStation>? Stations { get; set; }
}

public sealed class SpanshStation
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>E.g. "Orbis Starport", "Outpost", "Planetary Outpost", "Planetary Port",
    /// "Settlement", "Odyssey Settlement", "Installation", "Mega ship". Null for a small number
    /// of unclassified entries.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("distanceToArrival")]
    public double DistanceToArrival { get; set; }
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
