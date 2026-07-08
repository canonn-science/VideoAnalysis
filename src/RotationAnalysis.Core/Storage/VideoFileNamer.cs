namespace RotationAnalysis.Core.Storage;

/// <summary>
/// Suggests a filename that matches a ring's name for an uploaded video, keeping the source
/// file's extension (renaming never transcodes the container) and picking the next free
/// "_vN" suffix if a file already occupies the plain name.
/// </summary>
public static class VideoFileNamer
{
    public static bool MatchesRingName(string videoPath, string ringName)
    {
        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        return string.Equals(baseName, Sanitize(ringName), StringComparison.OrdinalIgnoreCase);
    }

    public static string GetNextAvailableFileName(string videoPath, string ringName)
    {
        var directory = Path.GetDirectoryName(videoPath) ?? string.Empty;
        var extension = Path.GetExtension(videoPath);
        var sanitized = Sanitize(ringName);

        var candidate = Path.Combine(directory, sanitized + extension);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (var version = 2; ; version++)
        {
            candidate = Path.Combine(directory, $"{sanitized}_v{version}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
