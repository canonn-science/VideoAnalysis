namespace VideoAnalysis.Core.Recording;

/// <summary>Discovers common game-capture output folders that actually exist on disk, so the
/// first-run default watch list starts populated for whichever capture tools the user has
/// installed rather than watching folders that don't exist. Only ever reads the filesystem -
/// never creates or modifies anything.</summary>
public static class DefaultWatchFolders
{
    /// <param name="userProfileDirectory">Root to resolve user-relative defaults against (defaults
    /// to the real user profile folder) - overridable so tests can point at a fake tree instead of
    /// the real filesystem.</param>
    /// <param name="programFilesX86Directory">Root to resolve the Steam default against (defaults
    /// to the real Program Files (x86) folder) - overridable for the same reason.</param>
    public static List<string> Discover(string? userProfileDirectory = null, string? programFilesX86Directory = null)
    {
        var userProfile = userProfileDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programFilesX86 = programFilesX86Directory ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var folders = new List<string>();

        var gameBarPath = Path.Combine(userProfile, "Videos", "Captures");
        if (Directory.Exists(gameBarPath))
        {
            folders.Add(gameBarPath);
        }

        var steamUserDataRoot = Path.Combine(programFilesX86, "Steam", "userdata");
        if (Directory.Exists(steamUserDataRoot))
        {
            foreach (var userDir in Directory.EnumerateDirectories(steamUserDataRoot))
            {
                var recordingsPath = Path.Combine(userDir, "gamerecordings");
                if (Directory.Exists(recordingsPath))
                {
                    folders.Add(recordingsPath);
                }
            }
        }

        var geForceNowRoot = Path.Combine(userProfile, "Videos", "NVIDIA", "GeForce NOW");
        if (Directory.Exists(geForceNowRoot))
        {
            folders.AddRange(Directory.EnumerateDirectories(geForceNowRoot));
        }

        return folders;
    }
}
