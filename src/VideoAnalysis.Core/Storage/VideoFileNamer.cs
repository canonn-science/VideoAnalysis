namespace VideoAnalysis.Core.Storage;

/// <summary>
/// Suggests a filename that matches a ring/body/system name for a video, keeping the source
/// file's extension (renaming never transcodes the container) and picking the next free
/// "_vN" suffix if a file already occupies the plain name.
/// </summary>
public static class VideoFileNamer
{
    public static bool MatchesRingName(string videoPath, string suggestedName)
    {
        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        return string.Equals(baseName, Sanitize(suggestedName), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><paramref name="targetDirectory"/> defaults to the video's own directory - pass an
    /// explicit one (e.g. a per-system subfolder) to place the renamed file elsewhere instead.</summary>
    public static string GetNextAvailableFileName(string videoPath, string suggestedName, string? targetDirectory = null)
    {
        var directory = targetDirectory ?? Path.GetDirectoryName(videoPath) ?? string.Empty;
        var extension = Path.GetExtension(videoPath);
        var sanitized = Sanitize(suggestedName);

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

    /// <summary>Replaces characters invalid in a Windows file or directory name with "_" - shared
    /// so a system-name folder segment can be sanitized the same way as the file name itself.</summary>
    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
