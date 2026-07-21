using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VideoAnalysis.Core.Journal;

/// <summary>The commander's system/body/station as of some point in the past, reconstructed by
/// replaying journal history rather than watching live events (contrast with
/// <see cref="JournalMonitor"/>'s live "last known" state).</summary>
public sealed record JournalLocationSnapshot(string? SystemName, long? SystemId64, string? BodyName, string? StationName, string? StationType);

/// <summary>Answers "where was the commander at time T?" without reading years of journal
/// history: Elite's journal filenames embed the session's start time in local clock time (e.g.
/// <c>Journal.2026-07-11T185610.01.log</c>), so the file covering <paramref name="targetUtc"/> -
/// the "primary" file - can be found directly instead of streaming through every older file first.
/// From there this only walks backward (oldest direction) as far as needed to fill in whichever of
/// system/body/station the primary file didn't establish itself (e.g. no ApproachBody yet that
/// session), capped at <see cref="DefaultMaxFilesToScan"/> files so a commander who simply hasn't
/// docked in a very long time doesn't turn this back into a full-history scan.
///
/// Used to guess a video upload's system/body/ring when its filename doesn't already give one
/// away: the file's creation time stands in for "when this was recorded", and whatever the
/// commander was doing at that moment is a reasonable guess for what's in the footage.</summary>
public static class JournalHistoryLookup
{
    private const string JournalFilePattern = "Journal.*.log";
    private const int DefaultMaxFilesToScan = 20;

    private static readonly Regex FileNamePattern = new(
        @"^Journal\.(?<start>\d{4}-\d{2}-\d{2}T\d{6})\.(?<part>\d+)\.log$", RegexOptions.Compiled);

    private readonly record struct JournalFile(string Path, DateTime StartUtc, int Part);

    /// <summary>Returns null if the directory is missing, no journal file's embedded start time is
    /// at or before <paramref name="targetUtc"/>, or nothing was learned within <paramref name="maxFilesToScan"/>.</summary>
    public static JournalLocationSnapshot? FindLocationAt(string journalDirectory, DateTime targetUtc, int maxFilesToScan = DefaultMaxFilesToScan)
    {
        if (!Directory.Exists(journalDirectory))
        {
            return null;
        }

        var files = EnumerateSortedFiles(journalDirectory);
        var primaryIndex = -1;
        for (var i = 0; i < files.Count; i++)
        {
            if (files[i].StartUtc > targetUtc)
            {
                break;
            }
            primaryIndex = i;
        }

        if (primaryIndex < 0)
        {
            // Target predates every journal file we have an embedded start time for.
            return null;
        }

        string? systemName = null;
        long? systemId64 = null;
        string? bodyName = null;
        string? stationName = null;
        string? stationType = null;
        var systemDetermined = false;
        var bodyDetermined = false;
        var stationDetermined = false;
        var foundAny = false;

        var filesScanned = 0;
        for (var i = primaryIndex; i >= 0 && filesScanned < maxFilesToScan; i--, filesScanned++)
        {
            var isPrimaryFile = i == primaryIndex;
            string? fileSystemName = null;
            long? fileSystemId64 = null;
            string? fileBodyName = null;
            string? fileStationName = null;
            string? fileStationType = null;
            var fileStationTouched = false;

            foreach (var line in ReadLinesSafely(files[i].Path))
            {
                if (TryExtractTimestamp(line) is not { } lineTimeUtc)
                {
                    continue;
                }

                // Only the primary file's content can straddle the target moment - every earlier
                // file is guaranteed entirely before it, since journal files don't overlap in time.
                if (isPrimaryFile && lineTimeUtc > targetUtc)
                {
                    break;
                }

                foundAny = true;

                if (!systemDetermined)
                {
                    fileSystemName = JournalMonitor.TryExtractSystemName(line) ?? fileSystemName;
                    fileSystemId64 = JournalMonitor.TryExtractSystemId64(line) ?? fileSystemId64;
                }

                if (!bodyDetermined)
                {
                    fileBodyName = JournalMonitor.TryExtractBodyName(line) ?? fileBodyName;
                }

                if (!stationDetermined)
                {
                    var docked = JournalMonitor.TryExtractDockedStation(line);
                    if (docked is not null)
                    {
                        fileStationTouched = true;
                        fileStationName = docked.Value.StationName;
                        fileStationType = docked.Value.StationType;
                    }
                    else if (JournalMonitor.IsUndockedEvent(line))
                    {
                        fileStationTouched = true;
                        fileStationName = null;
                        fileStationType = null;
                    }
                }
            }

            // A field found in this file "wins" over anything an older file might also have,
            // since we're walking backward from the moment closest to the target - once
            // determined, a field is locked in and later (older) files can't override it.
            if (!systemDetermined && fileSystemName is not null)
            {
                systemName = fileSystemName;
                systemId64 = fileSystemId64;
                systemDetermined = true;
            }

            if (!bodyDetermined && fileBodyName is not null)
            {
                bodyName = fileBodyName;
                bodyDetermined = true;
            }

            if (!stationDetermined && fileStationTouched)
            {
                stationName = fileStationName;
                stationType = fileStationType;
                stationDetermined = true;
            }

            if (systemDetermined && bodyDetermined && stationDetermined)
            {
                break;
            }
        }

        return foundAny ? new JournalLocationSnapshot(systemName, systemId64, bodyName, stationName, stationType) : null;
    }

    private static List<JournalFile> EnumerateSortedFiles(string journalDirectory)
    {
        var files = new List<JournalFile>();
        foreach (var path in Directory.EnumerateFiles(journalDirectory, JournalFilePattern))
        {
            var match = FileNamePattern.Match(Path.GetFileName(path));
            if (!match.Success)
            {
                continue;
            }

            if (!DateTime.TryParseExact(
                    match.Groups["start"].Value, "yyyy-MM-ddTHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var localStart))
            {
                continue;
            }

            // The filename's embedded start time is local clock time (unlike the UTC timestamps
            // inside the file), per Elite's own journal-naming convention.
            var startUtc = DateTime.SpecifyKind(localStart, DateTimeKind.Local).ToUniversalTime();
            var part = int.Parse(match.Groups["part"].Value, CultureInfo.InvariantCulture);
            files.Add(new JournalFile(path, startUtc, part));
        }

        files.Sort((a, b) => a.StartUtc != b.StartUtc ? a.StartUtc.CompareTo(b.StartUtc) : a.Part.CompareTo(b.Part));
        return files;
    }

    private static IEnumerable<string> ReadLinesSafely(string path)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (IOException)
        {
            // The game may hold a brief exclusive lock while rotating files; skip it - this is a
            // best-effort lookup, not a hard requirement.
            yield break;
        }

        using (stream)
        using (var reader = new StreamReader(stream))
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                yield return line;
            }
        }
    }

    public static DateTime? TryExtractTimestamp(string line)
    {
        if (line.Length == 0)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("timestamp", out var timestampProp) &&
                timestampProp.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(
                    timestampProp.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var timestamp))
            {
                return timestamp;
            }
        }
        catch (JsonException)
        {
            // malformed line; skip
        }

        return null;
    }
}
