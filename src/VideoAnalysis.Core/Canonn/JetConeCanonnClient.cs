using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VideoAnalysis.Core.Storage;

namespace VideoAnalysis.Core.Canonn;

/// <summary>Submits Jet Cone Length measurements to Canonn's "Lab Results" sheet via a Google Apps
/// Script web app - Jet Cone's counterpart to <see cref="CanonnClient"/>, but POSTing a JSON batch
/// instead of hitting a Google Form, per the endpoint Canonn supplied for this mode. Multiple
/// records can be sent in a single request (preferred for batch history submissions, both for
/// speed and to stay within Apps Script quotas).</summary>
public sealed class JetConeCanonnClient : IDisposable
{
    private const string EndpointUrl =
        "https://script.google.com/macros/s/AKfycbxJeajcYryGkGcbRg6SXp6gkzuzjuDUUXufqZYMR6jIgFPER7ELbcYOwFQoL-jXZx_PDQ/exec";

    private const string SharedSecret = "9b5b0700-1e96-43dd-83d0-bb79fd377db7";

    /// <summary>Small backoff between retries - only used for connection failures or an explicit
    /// <c>"ok": false</c> response, never for a failure reading the response body (see
    /// <see cref="SubmitAsync"/>), since the row insert already happened server-side by then and a
    /// blind retry would risk a duplicate row.</summary>
    private static readonly TimeSpan[] RetryDelays = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3) };

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public JetConeCanonnClient(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    /// <summary>Submits one or more Jet Cone measurements as a single batched request. Throws on
    /// failure - callers should catch and surface <see cref="Exception.Message"/> the same way
    /// <see cref="CanonnClient.SubmitAsync"/> callers already do.</summary>
    public async Task SubmitAsync(IReadOnlyList<JetLengthRecord> records, string commanderName, CancellationToken ct = default)
    {
        if (records.Count == 0)
        {
            return;
        }

        var payload = new
        {
            secret = SharedSecret,
            data = records.Select(r => JetConeSubmissionRow.FromRecord(r, commanderName)),
        };
        var json = JsonSerializer.Serialize(payload, RequestJsonOptions);

        Exception lastError = new InvalidOperationException("Submission to Canonn failed for an unknown reason.");
        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            using var content = new StringContent(json, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response;
            try
            {
                // Apps Script answers the POST with a 302 to a GET-only result URL; HttpClient's
                // default handler already follows redirects and switches to GET for a 302, so no
                // special handling is needed here (per the endpoint's documented behavior).
                response = await _http.PostAsync(EndpointUrl, content, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Failed before any response was received - nothing was written server-side yet,
                // so this is safe to retry.
                lastError = ex;
                if (attempt < RetryDelays.Length)
                {
                    await Task.Delay(RetryDelays[attempt], ct).ConfigureAwait(false);
                    continue;
                }

                throw;
            }

            using (response)
            {
                string body;
                try
                {
                    body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // The insert happens server-side before the redirect is followed, so a failure
                    // reading the response here doesn't mean the submission failed - don't retry
                    // (that would risk duplicate rows). The row is presumed written; the caller
                    // just can't get a definite confirmation.
                    throw new InvalidOperationException(
                        "Couldn't confirm the result, but the data was likely already submitted - check Measurement History before resubmitting.", ex);
                }

                JetConeSubmissionResponse? result;
                try
                {
                    result = JsonSerializer.Deserialize<JetConeSubmissionResponse>(body, ResponseJsonOptions);
                }
                catch (JsonException)
                {
                    result = null;
                }

                if (result is { Ok: true })
                {
                    return;
                }

                lastError = new InvalidOperationException(result?.Error ?? $"Unexpected response: {body}");
                if (attempt < RetryDelays.Length)
                {
                    await Task.Delay(RetryDelays[attempt], ct).ConfigureAwait(false);
                    continue;
                }
            }
        }

        throw lastError;
    }

    public void Dispose() => _http.Dispose();

    private sealed class JetConeSubmissionResponse
    {
        public bool Ok { get; set; }
        public int? Inserted { get; set; }
        public string? Error { get; set; }
    }
}
