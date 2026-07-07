using System.Text.Json;
using RotationAnalysis.Core.Updates.Models;

namespace RotationAnalysis.Core.Updates;

public sealed class UpdateChecker : IDisposable
{
    private const string InstallerAssetSuffix = "-win-x64-Setup.exe";

    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public UpdateChecker(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri("https://api.github.com/"),
            Timeout = TimeSpan.FromSeconds(15),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("RotationAnalysisLab-UpdateChecker");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>Returns null if the app is up to date, the latest release is a prerelease, or it has no Windows installer asset.</summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(string repoOwner, string repoName, Version currentVersion, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"repos/{repoOwner}/{repoName}/releases/latest", ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, ct).ConfigureAwait(false);
        if (release is null || release.Prerelease || !TryParseVersion(release.TagName, out var latestVersion))
        {
            return null;
        }

        if (NormalizeToThreePart(latestVersion) <= NormalizeToThreePart(currentVersion))
        {
            return null;
        }

        var installer = release.Assets.FirstOrDefault(a => a.Name.EndsWith(InstallerAssetSuffix, StringComparison.OrdinalIgnoreCase));
        if (installer is null)
        {
            return null;
        }

        return new UpdateInfo(latestVersion, release.HtmlUrl, installer.BrowserDownloadUrl, installer.Name);
    }

    public async Task DownloadInstallerAsync(string downloadUrl, string destinationPath, IProgress<double>? progress, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var httpStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long totalRead = 0;
        int read;
        while ((read = await httpStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            totalRead += read;
            if (totalBytes is > 0)
            {
                progress?.Report((double)totalRead / totalBytes.Value);
            }
        }
    }

    private static bool TryParseVersion(string tagName, out Version version)
    {
        if (Version.TryParse(tagName.TrimStart('v', 'V'), out var parsed))
        {
            version = parsed;
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }

    private static Version NormalizeToThreePart(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public void Dispose() => _http.Dispose();
}
