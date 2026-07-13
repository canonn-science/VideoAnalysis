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
}
