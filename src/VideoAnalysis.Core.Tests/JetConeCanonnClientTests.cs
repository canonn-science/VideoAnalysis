using System.Net;
using System.Text.Json;
using VideoAnalysis.Core.Canonn;
using VideoAnalysis.Core.Storage;
using Xunit;

namespace VideoAnalysis.Core.Tests;

public class JetConeCanonnClientTests
{
    private static JetLengthRecord MakeRecord() => new()
    {
        SystemName = "Leamue KY-Q d5-721",
        BodyName = "Leamue KY-Q d5-721 A",
        Distance = 3.33,
        AbsoluteMagnitude = 4.806976,
        BodyId = 1,
        MainStar = true,
        SpectralClass = "N0",
        UpdateTime = "2026-07-04 17:20:37+00",
    };

    [Fact]
    public async Task SubmitAsync_SendsCmdrNameAndCamelCaseFields_AndSucceeds()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"ok":true,"inserted":1}"""),
        });
        using var client = new JetConeCanonnClient(new HttpClient(handler));

        await client.SubmitAsync(new[] { MakeRecord() }, "CMDR Example", CancellationToken.None);

        Assert.NotNull(handler.LastRequestBody);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("secret", out _));
        var row = root.GetProperty("data")[0];
        Assert.Equal("CMDR Example", row.GetProperty("CMDR Name").GetString());
        Assert.Equal("Leamue KY-Q d5-721", row.GetProperty("systemName").GetString());
        Assert.Equal(3.33, row.GetProperty("distance").GetDouble());
        Assert.True(row.GetProperty("mainStar").GetBoolean());
    }

    [Fact]
    public async Task SubmitAsync_Throws_WhenServerReportsUnauthorized()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"ok":false,"error":"unauthorized"}"""),
        });
        using var client = new JetConeCanonnClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SubmitAsync(new[] { MakeRecord() }, "CMDR Example", CancellationToken.None));
        Assert.Contains("unauthorized", ex.Message);
    }

    [Fact]
    public async Task SubmitAsync_DoesNotSend_WhenNoRecords()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Should not be called"));
        using var client = new JetConeCanonnClient(new HttpClient(handler));

        await client.SubmitAsync(Array.Empty<JetLengthRecord>(), "CMDR Example", CancellationToken.None);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}
