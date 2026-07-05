using System.Net;
using System.Text.Json;
using RotationAnalysis.Core.Spansh.Models;

namespace RotationAnalysis.Core.Spansh;

public sealed class SpanshClient : IDisposable
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public SpanshClient(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri("https://spansh.co.uk/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public async Task<SpanshSearchResponse> SearchSystemsAsync(string query, CancellationToken ct = default)
    {
        var url = $"api/systems/field_values/system_names?q={Uri.EscapeDataString(query)}";
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync<SpanshSearchResponse>(stream, JsonOptions, ct).ConfigureAwait(false);
        return result ?? new SpanshSearchResponse();
    }

    /// <summary>Returns null if the system id64 could not be found (404).</summary>
    public async Task<SpanshDumpResponse?> GetDumpAsync(long id64, CancellationToken ct = default)
    {
        var url = $"api/dump/{id64}";
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<SpanshDumpResponse>(stream, JsonOptions, ct).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();
}
