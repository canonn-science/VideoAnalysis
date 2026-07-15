namespace VideoAnalysis.Core.Storage;

public sealed class AppSettings
{
    public string? CommanderName { get; set; }
    public bool MonitorJournals { get; set; }
    public bool OverrideUsername { get; set; }
    public bool OrganizeRenamedVideosBySystem { get; set; }

    /// <summary>Null means "use the default Pictures\RotationAnalysisLab\LongExposure folder" -
    /// set once the user saves somewhere else, so future saves default there instead.</summary>
    public string? LongExposureOutputDirectory { get; set; }

    public bool MonitorVideoFolders { get; set; }

    /// <summary>Null means "not yet seeded" - populated once, from <c>DefaultWatchFolders.Discover()</c>,
    /// the first time the app runs (see <c>MainViewModel</c>'s <c>IsFirstRun</c> check), so later
    /// saves persist whatever the user has actually configured instead of re-discovering defaults
    /// every launch.</summary>
    public List<string>? WatchedVideoFolders { get; set; }

    public List<string> WatchedVideoExtensions { get; set; } = new() { ".mp4", ".mkv", ".mov" };

    public bool PromptOnNewRecording { get; set; } = true;

    public bool AutoAddWithoutPrompting { get; set; }

    public bool ShowRecordingBadge { get; set; } = true;
}
