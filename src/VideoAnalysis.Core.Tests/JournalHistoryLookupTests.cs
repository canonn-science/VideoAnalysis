using VideoAnalysis.Core.Journal;
using Xunit;

namespace VideoAnalysis.Core.Tests;

public class JournalHistoryLookupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "JournalHistoryLookupTests_" + Guid.NewGuid());

    public JournalHistoryLookupTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string JournalPath(string name) => Path.Combine(_directory, name);

    private static DateTime LocalStartToUtc(string localIso) =>
        DateTime.SpecifyKind(DateTime.Parse(localIso), DateTimeKind.Local).ToUniversalTime();

    [Fact]
    public void FindLocationAt_GoesStraightToThePrimaryFile_IgnoringLaterEvents()
    {
        File.WriteAllText(JournalPath("Journal.2026-01-01T000000.01.log"),
            "{\"timestamp\":\"2026-01-01T00:00:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\"}\n" +
            "{\"timestamp\":\"2026-01-01T01:00:00Z\",\"event\":\"SupercruiseExit\",\"Body\":\"Earth\"}\n" +
            "{\"timestamp\":\"2026-01-01T02:00:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Deciat\"}\n");

        var snapshot = JournalHistoryLookup.FindLocationAt(_directory, new DateTime(2026, 1, 1, 1, 30, 0, DateTimeKind.Utc));

        Assert.NotNull(snapshot);
        Assert.Equal("Sol", snapshot!.SystemName);
        Assert.Equal("Earth", snapshot.BodyName);
    }

    [Fact]
    public void FindLocationAt_PicksCorrectSessionFile_AmongManyByEmbeddedFilenameTimestamp()
    {
        File.WriteAllText(JournalPath("Journal.2026-01-01T000000.01.log"),
            "{\"timestamp\":\"2026-01-01T00:00:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\"}\n");
        File.WriteAllText(JournalPath("Journal.2026-01-02T000000.01.log"),
            "{\"timestamp\":\"2026-01-02T00:00:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Deciat\"}\n");
        File.WriteAllText(JournalPath("Journal.2026-01-03T000000.01.log"),
            "{\"timestamp\":\"2026-01-03T00:00:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Merope\"}\n");

        var snapshot = JournalHistoryLookup.FindLocationAt(_directory, new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc));

        Assert.NotNull(snapshot);
        Assert.Equal("Deciat", snapshot!.SystemName);
    }

    [Fact]
    public void FindLocationAt_FillsGapsFromEarlierFiles_WhenPrimaryFileLacksThem()
    {
        File.WriteAllText(JournalPath("Journal.2026-01-01T000000.01.log"),
            "{\"timestamp\":\"2026-01-01T00:00:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\"}\n" +
            "{\"timestamp\":\"2026-01-01T00:05:00Z\",\"event\":\"SupercruiseExit\",\"Body\":\"Earth\"}\n");
        // Second session jumps to a new system but the video's creation time is before any
        // ApproachBody/SupercruiseExit happens in *this* session - body should fall back to the
        // last one known from the previous file.
        File.WriteAllText(JournalPath("Journal.2026-01-02T000000.01.log"),
            "{\"timestamp\":\"2026-01-02T00:00:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Deciat\"}\n");

        var snapshot = JournalHistoryLookup.FindLocationAt(_directory, new DateTime(2026, 1, 2, 0, 30, 0, DateTimeKind.Utc));

        Assert.NotNull(snapshot);
        Assert.Equal("Deciat", snapshot!.SystemName);
        Assert.Equal("Earth", snapshot.BodyName);
    }

    [Fact]
    public void FindLocationAt_UndockedInPrimaryFile_IsNotOverriddenByOlderDockedEvent()
    {
        File.WriteAllText(JournalPath("Journal.2026-01-01T000000.01.log"),
            "{\"timestamp\":\"2026-01-01T00:00:00Z\",\"event\":\"Docked\",\"StationName\":\"Jameson Memorial\",\"StationType\":\"Orbis\"}\n");
        File.WriteAllText(JournalPath("Journal.2026-01-02T000000.01.log"),
            "{\"timestamp\":\"2026-01-02T00:00:00Z\",\"event\":\"Undocked\",\"StationName\":\"Jameson Memorial\"}\n");

        var snapshot = JournalHistoryLookup.FindLocationAt(_directory, new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Utc));

        Assert.NotNull(snapshot);
        Assert.Null(snapshot!.StationName);
        Assert.Null(snapshot.StationType);
    }

    [Fact]
    public void FindLocationAt_RespectsMultiPartSessions_OrderingByPartNumber()
    {
        File.WriteAllText(JournalPath("Journal.2026-01-01T000000.01.log"),
            "{\"timestamp\":\"2026-01-01T00:00:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\"}\n");
        File.WriteAllText(JournalPath("Journal.2026-01-01T000000.02.log"),
            "{\"timestamp\":\"2026-01-01T05:00:00Z\",\"event\":\"SupercruiseExit\",\"Body\":\"Earth\"}\n");

        var snapshot = JournalHistoryLookup.FindLocationAt(_directory, new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc));

        Assert.NotNull(snapshot);
        Assert.Equal("Sol", snapshot!.SystemName);
        Assert.Equal("Earth", snapshot.BodyName);
    }

    [Fact]
    public void FindLocationAt_StopsAtMaxFilesToScan_WithoutReachingOlderFiles()
    {
        File.WriteAllText(JournalPath("Journal.2026-01-01T000000.01.log"),
            "{\"timestamp\":\"2026-01-01T00:00:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\"}\n");
        File.WriteAllText(JournalPath("Journal.2026-01-02T000000.01.log"),
            "{\"timestamp\":\"2026-01-02T00:00:00Z\",\"event\":\"Fileheader\",\"part\":1}\n");

        var snapshot = JournalHistoryLookup.FindLocationAt(
            _directory, new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Utc), maxFilesToScan: 1);

        Assert.NotNull(snapshot);
        Assert.Null(snapshot!.SystemName);
    }

    [Fact]
    public void FindLocationAt_ReturnsNull_WhenTargetIsBeforeAnyJournalFile()
    {
        File.WriteAllText(JournalPath("Journal.2026-01-01T000000.01.log"),
            "{\"timestamp\":\"2026-01-01T00:00:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\"}\n");

        var snapshot = JournalHistoryLookup.FindLocationAt(_directory, LocalStartToUtc("2025-12-30T00:00:00"));

        Assert.Null(snapshot);
    }

    [Fact]
    public void FindLocationAt_IgnoresFilesNotMatchingExpectedNamingPattern()
    {
        File.WriteAllText(JournalPath("Journal.log"), "{\"timestamp\":\"2026-01-01T00:00:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\"}\n");

        var snapshot = JournalHistoryLookup.FindLocationAt(_directory, DateTime.UtcNow);

        Assert.Null(snapshot);
    }

    [Fact]
    public void FindLocationAt_ReturnsNull_WhenDirectoryDoesNotExist()
    {
        var snapshot = JournalHistoryLookup.FindLocationAt(Path.Combine(_directory, "missing"), DateTime.UtcNow);

        Assert.Null(snapshot);
    }

    [Theory]
    [InlineData("{\"timestamp\":\"2026-07-01T12:30:00Z\",\"event\":\"FSDJump\"}", "2026-07-01T12:30:00Z")]
    [InlineData("{\"event\":\"FSDJump\"}", null)]
    [InlineData("not json", null)]
    [InlineData("", null)]
    public void TryExtractTimestamp_ParsesExpectedLines(string line, string? expectedIso)
    {
        var expected = expectedIso is null ? (DateTime?)null : DateTime.Parse(expectedIso).ToUniversalTime();
        Assert.Equal(expected, JournalHistoryLookup.TryExtractTimestamp(line));
    }
}
