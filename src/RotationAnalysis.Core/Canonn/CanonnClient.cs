using System.Globalization;

namespace RotationAnalysis.Core.Canonn;

/// <summary>Submits ring measurements to Canonn's public Google Form and downloads the published
/// TSV of previously submitted measurements, following the same shape as <c>SpanshClient</c>/
/// <c>UpdateChecker</c> (owns its own <see cref="HttpClient"/>, disposed by the caller).</summary>
public sealed class CanonnClient : IDisposable
{
    private const string FormResponseUrl =
        "https://docs.google.com/forms/d/e/1FAIpQLScTNmAkzdBGCot592M1CP7BlkEMfiAxx5Qb39g5n-Rgv8YlXg/formResponse";

    private const string SubmittedTsvUrl =
        "https://docs.google.com/spreadsheets/d/e/2PACX-1vTKhRygX2IiZlu_HAGK2vR68dxKIiPHMyjjwMRaoQjnq9lqCqt_jXE-MZCvhdRBvq7YaU_tiPPb9Q1d/pub?gid=947491494&single=true&output=tsv";

    private readonly HttpClient _http;

    public CanonnClient(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public async Task SubmitAsync(CanonnSubmission submission, CancellationToken ct = default)
    {
        var query = string.Join('&',
            $"entry.600905391={Uri.EscapeDataString(submission.CommanderName)}",
            $"entry.1130968439={Uri.EscapeDataString(submission.SystemName)}",
            $"entry.472013560={Uri.EscapeDataString(submission.Id64.ToString(CultureInfo.InvariantCulture))}",
            $"entry.1151578825={Uri.EscapeDataString(FormatNumber(submission.X))}",
            $"entry.525275561={Uri.EscapeDataString(FormatNumber(submission.Y))}",
            $"entry.459250128={Uri.EscapeDataString(FormatNumber(submission.Z))}",
            $"entry.500354492={Uri.EscapeDataString(submission.BodyName)}",
            $"entry.352807454={Uri.EscapeDataString(submission.BodyType ?? string.Empty)}",
            $"entry.311252220={Uri.EscapeDataString(FormatNumber(submission.BodyRadiusKm))}",
            $"entry.1550981279={Uri.EscapeDataString(FormatNumber(submission.BodyMassEarthMasses))}",
            $"entry.1805555353={Uri.EscapeDataString(submission.RingName)}",
            $"entry.1045222536={Uri.EscapeDataString(submission.RingType ?? string.Empty)}",
            $"entry.1741677072={Uri.EscapeDataString(FormatNumber(submission.InnerRadiusKm))}",
            $"entry.1379006716={Uri.EscapeDataString(FormatNumber(submission.OuterRadiusKm))}",
            $"entry.1306863839={Uri.EscapeDataString(FormatNumber(submission.WidthKm))}",
            $"entry.467530390={Uri.EscapeDataString(FormatNumber(submission.EstimatedPeriodSeconds))}",
            $"entry.1394317518={Uri.EscapeDataString(FormatNumber(submission.ObservedPeriodSeconds))}");

        using var response = await _http.GetAsync($"{FormResponseUrl}?{query}", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<CanonnSubmittedMeasurement>> GetSubmittedMeasurementsAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(SubmittedTsvUrl, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var tsv = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseTsv(tsv);
    }

    private static string FormatNumber(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatNumber(double? value) => value is double v ? FormatNumber(v) : string.Empty;

    /// <summary>Looks up columns by header name rather than position, so the parser keeps working
    /// even if Canonn reorders or adds columns to the published sheet.</summary>
    private static List<CanonnSubmittedMeasurement> ParseTsv(string tsv)
    {
        var result = new List<CanonnSubmittedMeasurement>();
        using var reader = new StringReader(tsv);
        var headerLine = reader.ReadLine();
        if (headerLine is null)
        {
            return result;
        }

        var headers = headerLine.Split('\t');
        int commanderIdx = IndexOf(headers, "Commander Name");
        int systemIdx = IndexOf(headers, "System Name");
        int bodyIdx = IndexOf(headers, "Body Name");
        int ringIdx = IndexOf(headers, "Ring Name");
        int innerIdx = IndexOf(headers, "Inner Radius (km)");
        int outerIdx = IndexOf(headers, "Outer Radius (km)");
        int observedIdx = IndexOf(headers, "Observed Period (seconds)");

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = line.Split('\t');
            result.Add(new CanonnSubmittedMeasurement
            {
                CommanderName = Field(fields, commanderIdx),
                SystemName = Field(fields, systemIdx),
                BodyName = Field(fields, bodyIdx),
                RingName = Field(fields, ringIdx),
                InnerRadiusKm = ParseDouble(Field(fields, innerIdx)),
                OuterRadiusKm = ParseDouble(Field(fields, outerIdx)),
                ObservedPeriodSeconds = ParseDouble(Field(fields, observedIdx)),
            });
        }

        return result;
    }

    private static int IndexOf(string[] headers, string name) =>
        Array.FindIndex(headers, h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase));

    private static string Field(string[] fields, int index) =>
        index >= 0 && index < fields.Length ? fields[index].Trim() : string.Empty;

    private static double ParseDouble(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : double.NaN;

    public void Dispose() => _http.Dispose();
}
