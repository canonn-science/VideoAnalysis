using System.Text.Json;

namespace VideoAnalysis.Core.Storage;

/// <summary>Persists the shared video library as a single JSON array file - chosen over the
/// CSV pattern used by the measurement stores because entries have optional/nested fields
/// (nullable coords, a thumbnail filename, a tab-id list) that don't map cleanly onto flat CSV
/// rows, and there's no spreadsheet-export use case for this file. Whole-file read/mutate/write
/// under a lock, same simplicity tradeoff as <see cref="AppSettingsStore"/> - fine at the scale
/// of a personal video library.</summary>
public sealed class VideoLibraryStore
{
    private readonly object _lock = new();

    public string LibraryPath { get; }

    public VideoLibraryStore(string? libraryPath = null)
    {
        LibraryPath = libraryPath ?? Path.Combine(StoragePaths.Root, "video_library.json");
    }

    /// <summary>All entries, most-recently-selected first.</summary>
    public List<VideoLibraryEntry> GetAll()
    {
        lock (_lock)
        {
            return LoadUnlocked().OrderByDescending(e => e.LastSelectedUtc).ToList();
        }
    }

    /// <summary>The next <paramref name="take"/> entries after <paramref name="skip"/>, in MRU order.</summary>
    public List<VideoLibraryEntry> GetPage(int skip, int take) => GetAll().Skip(skip).Take(take).ToList();

    public VideoLibraryEntry? FindByPath(string filePath)
    {
        lock (_lock)
        {
            return LoadUnlocked().FirstOrDefault(e => string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        }
    }

    public VideoLibraryEntry Add(VideoLibraryEntry entry)
    {
        lock (_lock)
        {
            var all = LoadUnlocked();
            entry.AddedUtc = entry.LastSelectedUtc = DateTime.UtcNow;
            all.Add(entry);
            SaveUnlocked(all);
            return entry;
        }
    }

    /// <summary>Moves an entry to the top of MRU order by touching <see cref="VideoLibraryEntry.LastSelectedUtc"/>.</summary>
    public void MarkSelected(Guid id) => Mutate(id, e => e.LastSelectedUtc = DateTime.UtcNow);

    public void MarkAnalyzed(Guid id, string tabId) => Mutate(id, e =>
    {
        if (!e.AnalyzedByTabs.Contains(tabId))
        {
            e.AnalyzedByTabs.Add(tabId);
        }
        e.Status = VideoLibraryEntryStatus.Analyzed;
    });

    /// <summary>Updates the stored path after an in-place rename (e.g. <c>VideoFileNamer</c>) so
    /// the entry keeps pointing at the real file instead of going "missing".</summary>
    public void UpdatePath(Guid id, string newPath) => Mutate(id, e => e.FilePath = newPath);

    public void UpdateThumbnail(Guid id, string thumbnailFileName) => Mutate(id, e => e.ThumbnailFileName = thumbnailFileName);

    public void SetRecording(Guid id, bool isRecording) => Mutate(id, e => e.IsRecording = isRecording);

    /// <summary>Tags an entry with a confirmed system/body/ring - e.g. once the user resolves them
    /// via Ring Rotation's picker, so future selections of the same video auto-populate correctly.
    /// Leaves station fields untouched, since that's an orthogonal dimension this doesn't know about.</summary>
    public void UpdateSystemBodyRing(
        Guid id, string systemName, long systemId64, double systemX, double systemY, double systemZ,
        string bodyName, string ringName)
        => Mutate(id, e =>
        {
            e.SystemName = systemName;
            e.SystemId64 = systemId64;
            e.SystemX = systemX;
            e.SystemY = systemY;
            e.SystemZ = systemZ;
            e.BodyName = bodyName;
            e.RingName = ringName;
        });

    /// <summary>Overwrites every system/body/ring/station field from <paramref name="source"/>
    /// onto the stored entry - used to tag an already-added placeholder entry after the fact (the
    /// "recording finished, tag it now" flow), where <see cref="UpdateSystemBodyRing"/>'s stricter
    /// signature (requiring a resolved body and ring) doesn't fit, since any of these fields may
    /// be left unset.</summary>
    public void UpdateMetadata(Guid id, VideoLibraryEntry source) => Mutate(id, e =>
    {
        e.SystemName = source.SystemName;
        e.SystemId64 = source.SystemId64;
        e.SystemX = source.SystemX;
        e.SystemY = source.SystemY;
        e.SystemZ = source.SystemZ;
        e.BodyName = source.BodyName;
        e.RingName = source.RingName;
        e.StationName = source.StationName;
        e.StationType = source.StationType;
    });

    public void Remove(Guid id)
    {
        lock (_lock)
        {
            var all = LoadUnlocked();
            all.RemoveAll(e => e.Id == id);
            SaveUnlocked(all);
        }
    }

    private void Mutate(Guid id, Action<VideoLibraryEntry> apply)
    {
        lock (_lock)
        {
            var all = LoadUnlocked();
            var entry = all.FirstOrDefault(e => e.Id == id);
            if (entry is null)
            {
                return;
            }

            apply(entry);
            SaveUnlocked(all);
        }
    }

    private List<VideoLibraryEntry> LoadUnlocked()
    {
        if (!File.Exists(LibraryPath))
        {
            return new List<VideoLibraryEntry>();
        }

        try
        {
            var json = File.ReadAllText(LibraryPath);
            return JsonSerializer.Deserialize<List<VideoLibraryEntry>>(json) ?? new List<VideoLibraryEntry>();
        }
        catch
        {
            // A corrupted library file shouldn't prevent the app from starting.
            return new List<VideoLibraryEntry>();
        }
    }

    private void SaveUnlocked(List<VideoLibraryEntry> entries)
    {
        var directory = Path.GetDirectoryName(LibraryPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(entries);
        var tempPath = LibraryPath + ".tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(LibraryPath))
        {
            File.Replace(tempPath, LibraryPath, null);
        }
        else
        {
            File.Move(tempPath, LibraryPath);
        }
    }
}
