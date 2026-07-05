using System.Text.Json.Serialization;

namespace RotationAnalysis.Core.Spansh.Models;

public sealed class SpanshSearchResponse
{
    [JsonPropertyName("min_max")]
    public List<SpanshSearchSystem> MinMax { get; set; } = new();

    [JsonPropertyName("values")]
    public List<string> Values { get; set; } = new();
}

public sealed class SpanshSearchSystem
{
    [JsonPropertyName("id64")]
    public long Id64 { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }
}
