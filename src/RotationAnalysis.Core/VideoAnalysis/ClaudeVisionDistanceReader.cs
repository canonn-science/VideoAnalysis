using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RotationAnalysis.Core.VideoAnalysis;

/// <summary>
/// Opt-in fallback for reading the jet-cone distance value when local OCR
/// (<see cref="HudDistanceReader"/>) can't read a frame reliably - per spec, only used if the user
/// has provided a Claude API key (stored via <c>SecretStore</c>) after being asked. Raw
/// <see cref="HttpClient"/> against the Messages API, matching the existing style of
/// <c>SpanshClient</c>/<c>CanonnClient</c> in this codebase rather than adding an SDK dependency.
/// Narrower than the original Python tool's HUD-OCR step (<c>gather_hud_data</c> in
/// process_jet_footage.py): only the distance is needed here, not the body name, since the user
/// has already selected the system/body in the UI.
/// </summary>
public sealed class ClaudeVisionDistanceReader : IDisposable
{
    private const string Model = "claude-opus-4-8";
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly object DistanceSchema = new
    {
        type = "object",
        properties = new
        {
            distance_ls = new { type = "number" },
            confidence = new { type = "integer" },
        },
        required = new[] { "distance_ls", "confidence" },
        additionalProperties = false,
    };

    private const string Prompt =
        "This is a cropped HUD readout from an Elite Dangerous screenshot, taken while " +
        "approaching a neutron star or white dwarf's jet cone during supercruise. Read the " +
        "distance value shown in light seconds (Ls) - it's a short decimal number immediately " +
        "followed by the letters \"Ls\". Ignore any body/system name text and ignore any other " +
        "distance readout that may be visible elsewhere in the crop (only report the one paired " +
        "with the nearer / primary reticle line, not a secondary waypoint). Report distance_ls as " +
        "a plain number (no unit). For confidence, score how legible the actual glyphs were, " +
        "0-100.";

    public sealed record DistanceReading(double DistanceLs, int Confidence);

    private readonly HttpClient _http;

    public ClaudeVisionDistanceReader(string apiKey, HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Remove("x-api-key");
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Remove("anthropic-version");
        _http.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
    }

    public async Task<DistanceReading> ReadDistanceAsync(byte[] cropPngBytes, CancellationToken ct = default)
    {
        var requestBody = new
        {
            model = Model,
            max_tokens = 256,
            output_config = new
            {
                format = new
                {
                    type = "json_schema",
                    schema = DistanceSchema,
                },
            },
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "image",
                            source = new
                            {
                                type = "base64",
                                media_type = "image/png",
                                data = Convert.ToBase64String(cropPngBytes),
                            },
                        },
                        new { type = "text", text = Prompt },
                    },
                },
            },
        };

        using var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await _http.PostAsync(MessagesUrl, content, ct).ConfigureAwait(false);
        var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Claude API request failed ({(int)response.StatusCode}): {responseJson}");
        }

        var parsed = JsonSerializer.Deserialize<MessagesResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("Claude API returned an empty response.");

        var textBlock = parsed.Content.FirstOrDefault(b => b.Type == "text")
            ?? throw new InvalidOperationException("Claude API response had no text content block.");

        var reading = JsonSerializer.Deserialize<DistanceReadingJson>(textBlock.Text ?? string.Empty, JsonOptions)
            ?? throw new InvalidOperationException("Could not parse the distance reading from Claude's response.");

        return new DistanceReading(reading.DistanceLs, reading.Confidence);
    }

    public void Dispose() => _http.Dispose();

    private sealed class MessagesResponse
    {
        [JsonPropertyName("content")]
        public List<ContentBlock> Content { get; set; } = new();
    }

    private sealed class ContentBlock
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private sealed class DistanceReadingJson
    {
        [JsonPropertyName("distance_ls")]
        public double DistanceLs { get; set; }

        [JsonPropertyName("confidence")]
        public int Confidence { get; set; }
    }
}
