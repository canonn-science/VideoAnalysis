namespace VideoAnalysis.Core.Storage;

public enum VideoLibraryEntryStatus
{
    NotAnalyzed,
    Analyzed,
}

/// <summary>A video the user has added to the shared library, together with whatever
/// system/body/ring/station metadata they supplied (or that was auto-filled from the game's
/// journal) at upload time. Only the original file path is stored - the library never copies
/// the video itself, so <see cref="FilePath"/> can go stale if the user moves/renames/deletes
/// it outside the app; that's detected at read time via <c>File.Exists</c>, not persisted here.</summary>
public sealed class VideoLibraryEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string FilePath { get; set; }

    public string? SystemName { get; set; }
    public long? SystemId64 { get; set; }
    public double? SystemX { get; set; }
    public double? SystemY { get; set; }
    public double? SystemZ { get; set; }

    public string? BodyName { get; set; }

    /// <summary>Optional - only set if the chosen body had a ring/belt and the user picked one
    /// at upload time. When set, Ring Rotation can auto-select the matching row instead of
    /// requiring the user to pick it again.</summary>
    public string? RingName { get; set; }

    public string? StationName { get; set; }
    public string? StationType { get; set; }

    public DateTime AddedUtc { get; set; }

    /// <summary>Drives most-recently-used ordering in the library panel.</summary>
    public DateTime LastSelectedUtc { get; set; }

    /// <summary>Filename only (e.g. "{Id}.png"), resolved against <see cref="VideoThumbnailCache.Directory"/>.</summary>
    public string? ThumbnailFileName { get; set; }

    public VideoLibraryEntryStatus Status { get; set; } = VideoLibraryEntryStatus.NotAnalyzed;

    /// <summary>True while this entry is a placeholder for a recording detected by the folder
    /// monitor that hasn't finished writing yet - drives the "Recording…" badge and suppresses
    /// thumbnail generation until the monitor reports completion.</summary>
    public bool IsRecording { get; set; }

    /// <summary>Tab identifiers (e.g. "RingRotation") that have produced a saved measurement
    /// against this entry - a list rather than a single flag so later phases (Station Rotation,
    /// Jet Cone, Long Exposure) don't need a schema change.</summary>
    public List<string> AnalyzedByTabs { get; set; } = new();
}
