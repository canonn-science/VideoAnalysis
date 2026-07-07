using System.Net;
using RotationAnalysis.Core.Updates;
using Xunit;

namespace RotationAnalysis.Core.Tests;

public class UpdateCheckerTests
{
    private static UpdateChecker MakeChecker(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var http = new HttpClient(new StubHandler(json, statusCode)) { BaseAddress = new Uri("https://api.github.com/") };
        return new UpdateChecker(http);
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReturnsUpdate_WhenLatestReleaseIsNewer()
    {
        const string json = """
        {
          "tag_name": "v0.3.0",
          "html_url": "https://github.com/canonn-science/RotationAnalysis/releases/tag/v0.3.0",
          "prerelease": false,
          "assets": [
            { "name": "RotationAnalysisLab-0.3.0-win-x64-Setup.exe", "browser_download_url": "https://example.com/setup.exe" }
          ]
        }
        """;
        using var checker = MakeChecker(json);

        var update = await checker.CheckForUpdateAsync("canonn-science", "RotationAnalysis", new Version(0, 2, 0));

        Assert.NotNull(update);
        Assert.Equal(new Version(0, 3, 0), update!.Version);
        Assert.Equal("https://example.com/setup.exe", update.InstallerDownloadUrl);
        Assert.Equal("RotationAnalysisLab-0.3.0-win-x64-Setup.exe", update.InstallerFileName);
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReturnsNull_WhenCurrentVersionIsUpToDate()
    {
        const string json = """
        {
          "tag_name": "v0.2.0",
          "html_url": "https://github.com/canonn-science/RotationAnalysis/releases/tag/v0.2.0",
          "prerelease": false,
          "assets": [
            { "name": "RotationAnalysisLab-0.2.0-win-x64-Setup.exe", "browser_download_url": "https://example.com/setup.exe" }
          ]
        }
        """;
        using var checker = MakeChecker(json);

        var update = await checker.CheckForUpdateAsync("canonn-science", "RotationAnalysis", new Version(0, 2, 0, 0));

        Assert.Null(update);
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReturnsNull_WhenLatestReleaseIsPrerelease()
    {
        const string json = """
        {
          "tag_name": "v0.3.0",
          "html_url": "https://example.com",
          "prerelease": true,
          "assets": [
            { "name": "RotationAnalysisLab-0.3.0-win-x64-Setup.exe", "browser_download_url": "https://example.com/setup.exe" }
          ]
        }
        """;
        using var checker = MakeChecker(json);

        var update = await checker.CheckForUpdateAsync("canonn-science", "RotationAnalysis", new Version(0, 2, 0));

        Assert.Null(update);
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReturnsNull_WhenNoInstallerAssetPresent()
    {
        const string json = """
        {
          "tag_name": "v0.3.0",
          "html_url": "https://example.com",
          "prerelease": false,
          "assets": [
            { "name": "RotationAnalysisLab-v0.3.0-win-x64.zip", "browser_download_url": "https://example.com/zip" }
          ]
        }
        """;
        using var checker = MakeChecker(json);

        var update = await checker.CheckForUpdateAsync("canonn-science", "RotationAnalysis", new Version(0, 2, 0));

        Assert.Null(update);
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReturnsNull_WhenRequestFails()
    {
        using var checker = MakeChecker(json: "{}", statusCode: HttpStatusCode.NotFound);

        var update = await checker.CheckForUpdateAsync("canonn-science", "RotationAnalysis", new Version(0, 2, 0));

        Assert.Null(update);
    }

    private sealed class StubHandler(string json, HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(json) });
    }
}
