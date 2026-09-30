using System.Collections.ObjectModel;
using System.IO;
using Microsoft.VisualBasic.FileIO;
using VideoAnalysis.App.Infrastructure;
using VideoAnalysis.Core.Diagnostics;
using VideoAnalysis.Core.Storage;

namespace VideoAnalysis.App.ViewModels;

/// <summary>Owns the shared video library shown in the left-hand panel: lazy paging, MRU
/// selection, and upload orchestration. The view (MainWindow) owns the actual file-picker and
/// metadata-modal dialogs - this view model only reacts to their results via
/// <see cref="AddFromUpload"/>, matching the existing <c>VideoSelectionRequested</c> pattern
/// used by the per-tab row view models.</summary>
public sealed class VideoLibraryViewModel : ObservableObject
{
    private const int PageSize = 10;

    private readonly VideoLibraryStore _store;
    private readonly Func<bool> _showRecordingBadgeSetting;
    private VideoLibraryEntryViewModel? _selectedEntry;
    private bool _isLoadingMore;

    /// <param name="showRecordingBadgeSetting">Reads the live "Show Recording badge" Configuration
    /// toggle - threaded through rather than read from a static settings object so entry view
    /// models stay decoupled from <c>AppSettingsStore</c>.</param>
    public VideoLibraryViewModel(VideoLibraryStore store, Func<bool>? showRecordingBadgeSetting = null)
    {
        _store = store;
        _showRecordingBadgeSetting = showRecordingBadgeSetting ?? (() => true);
        UploadCommand = new RelayCommand(() => UploadRequested?.Invoke());
        LoadInitialPage();
    }

    private VideoLibraryEntryViewModel CreateRowViewModel(VideoLibraryEntry entry) =>
        new(entry, Select, RequestRemove, _showRecordingBadgeSetting);

    public ObservableCollection<VideoLibraryEntryViewModel> Entries { get; } = new();

    public VideoLibraryEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        private set => SetField(ref _selectedEntry, value);
    }

    public RelayCommand UploadCommand { get; }

    /// <summary>Raised when the user clicks the panel's Upload button; the view handles the
    /// file picker and metadata modal, then calls <see cref="AddFromUpload"/> with the result.</summary>
    public event Action? UploadRequested;

    /// <summary>Raised whenever the active/selected library video changes.</summary>
    public event Action<VideoLibraryEntryViewModel>? EntrySelected;

    /// <summary>Raised whenever an already-selected entry's tagged system/body/ring or file path
    /// changes in place - e.g. Ring Rotation's Analyze flow tagging it via
    /// <see cref="UpdateSystemBodyRing"/>, or an in-place rename via <see cref="UpdatePath"/> -
    /// without going through <see cref="Select"/>. Every tab that mirrors the current entry
    /// subscribes to this the same way it subscribes to <see cref="EntrySelected"/>, so tagging (or
    /// renaming) it in one tab is immediately reflected in every other tab already showing it,
    /// instead of only ever being visible the next time the entry is (re-)selected.</summary>
    public event Action<VideoLibraryEntryViewModel>? EntryDataChanged;

    /// <summary>Raised when the user clicks an entry's remove button; the view handles the
    /// confirmation prompt (index-only vs. also deleting the file), then calls <see cref="Remove"/>.</summary>
    public event Action<VideoLibraryEntryViewModel>? RemoveRequested;

    public void LoadInitialPage()
    {
        Entries.Clear();
        foreach (var entry in _store.GetPage(0, PageSize))
        {
            Entries.Add(CreateRowViewModel(entry));
        }
    }

    /// <summary>Appends the next page of entries - called by the panel when the scroll position
    /// nears the bottom of the currently loaded list.</summary>
    public void LoadNextPage()
    {
        if (_isLoadingMore)
        {
            return;
        }

        _isLoadingMore = true;
        try
        {
            foreach (var entry in _store.GetPage(Entries.Count, PageSize))
            {
                Entries.Add(CreateRowViewModel(entry));
            }
        }
        finally
        {
            _isLoadingMore = false;
        }
    }

    /// <summary>Selects whatever is currently first in the list (most-recently-used ordering, so
    /// this is always the last video uploaded or selected in a previous session) - called once
    /// consumers have finished wiring up <see cref="EntrySelected"/>, so every tab that depends on
    /// it (Ring Rotation's system search, Slit Scan's preview) is already in sync the moment the
    /// app opens, without requiring the user to click anything first.</summary>
    public void SelectFirstEntryIfAny()
    {
        if (Entries.Count > 0)
        {
            Select(Entries[0]);
        }
    }

    /// <summary>Marks an entry as most-recently-used, moves it to the top of the visible list,
    /// and raises <see cref="EntrySelected"/>.</summary>
    public void Select(VideoLibraryEntryViewModel entry)
    {
        if (SelectedEntry == entry)
        {
            // Already the active entry - clicking it again shouldn't restart its player.
            return;
        }

        _store.MarkSelected(entry.Id);

        // Move (not Remove+Insert) so WPF repositions the existing row container instead of
        // tearing it down and rebuilding it - a Remove+Insert would destroy the Loaded-based
        // PropertyChanged subscription that wires the player up, and its asynchronous recreation
        // would miss the IsSelected change set moments later below, leaving the player never
        // actually started.
        var currentIndex = Entries.IndexOf(entry);
        if (currentIndex < 0)
        {
            Entries.Insert(0, entry);
        }
        else if (currentIndex > 0)
        {
            Entries.Move(currentIndex, 0);
        }

        if (SelectedEntry is { } previous)
        {
            previous.IsSelected = false;
        }

        entry.IsSelected = true;
        SelectedEntry = entry;
        EntrySelected?.Invoke(entry);
    }

    /// <summary>Adds a freshly uploaded video (with its captured metadata) to the library,
    /// avoiding a duplicate row if this file path is already in the library, kicks off
    /// background thumbnail generation, and selects it.</summary>
    public VideoLibraryEntryViewModel AddFromUpload(VideoLibraryEntry entry)
    {
        var existing = _store.FindByPath(entry.FilePath);
        VideoLibraryEntryViewModel rowVm;
        if (existing is not null)
        {
            rowVm = Entries.FirstOrDefault(e => e.Id == existing.Id) ?? CreateRowViewModel(existing);
        }
        else
        {
            var added = _store.Add(entry);
            rowVm = CreateRowViewModel(added);
            Entries.Insert(0, rowVm);
            _ = GenerateThumbnailAsync(rowVm);
        }

        Select(rowVm);
        return rowVm;
    }

    /// <summary>Adds a placeholder entry for a recording the folder monitor just detected -
    /// same dedupe-by-path/insert/select shape as <see cref="AddFromUpload"/>, but skips thumbnail
    /// generation since the file is still being written (a frame read against a growing/locked
    /// file would just fail). <see cref="MarkRecordingCompleteAsync"/> generates the real thumbnail
    /// once the monitor reports the recording has finished.</summary>
    public VideoLibraryEntryViewModel AddPlaceholder(string filePath)
    {
        var existing = _store.FindByPath(filePath);
        if (existing is not null)
        {
            var existingVm = Entries.FirstOrDefault(e => e.Id == existing.Id) ?? CreateRowViewModel(existing);
            Select(existingVm);
            return existingVm;
        }

        var added = _store.Add(new VideoLibraryEntry { FilePath = filePath, IsRecording = true });
        var rowVm = CreateRowViewModel(added);
        Entries.Insert(0, rowVm);
        Select(rowVm);
        return rowVm;
    }

    /// <summary>Adds a tagged entry for a recording the folder monitor detected that's still in
    /// progress - the single-phase "tag it immediately" flow's equivalent of <see cref="AddFromUpload"/>.
    /// Same dedupe-by-path/insert/select shape, and (like <see cref="AddPlaceholder"/>, unlike
    /// <see cref="AddFromUpload"/>) skips thumbnail generation since the file is still being
    /// written - <see cref="MarkRecordingCompleteAsync"/> generates the real thumbnail once the
    /// monitor reports the recording has finished.</summary>
    public VideoLibraryEntryViewModel AddTaggedInProgressRecording(VideoLibraryEntry entry)
    {
        entry.IsRecording = true;
        var existing = _store.FindByPath(entry.FilePath);
        VideoLibraryEntryViewModel rowVm;
        if (existing is not null)
        {
            rowVm = Entries.FirstOrDefault(e => e.Id == existing.Id) ?? CreateRowViewModel(existing);
        }
        else
        {
            var added = _store.Add(entry);
            rowVm = CreateRowViewModel(added);
            Entries.Insert(0, rowVm);
        }

        Select(rowVm);
        return rowVm;
    }

    /// <summary>Clears the "Recording…" placeholder state and generates the real thumbnail, once
    /// the folder monitor reports the underlying file has stopped growing.</summary>
    public async Task MarkRecordingCompleteAsync(VideoLibraryEntryViewModel entry)
    {
        _store.SetRecording(entry.Id, false);
        entry.Entry.IsRecording = false;
        entry.NotifyEntryChanged();
        await GenerateThumbnailAsync(entry).ConfigureAwait(true);
    }

    private void RequestRemove(VideoLibraryEntryViewModel entry) => RemoveRequested?.Invoke(entry);

    /// <summary>Removes an entry from the library index and, if <paramref name="deleteFile"/> is
    /// set, sends its underlying video file to the Recycle Bin too (never a permanent delete - this
    /// is footage the user may have spent real effort capturing, and a misclick between the two
    /// confirmation options shouldn't be unrecoverable). Deselecting happens before the entry
    /// leaves <see cref="Entries"/>, not after - once removed, the ItemsControl tears down that
    /// row's container (see VideoLibraryPanel's Unloaded handler) and stops listening for the
    /// IsSelected change that would otherwise stop its MediaElement, so doing this in the other
    /// order can leave the file locked out from under the delete below. Returns false if
    /// <paramref name="deleteFile"/> was requested but the file couldn't be removed (e.g. still
    /// locked, or a permissions error), so the caller can tell the user - a silently-failed delete
    /// would contradict what they just confirmed.</summary>
    public async Task<bool> RemoveAsync(VideoLibraryEntryViewModel entry, bool deleteFile)
    {
        if (SelectedEntry == entry)
        {
            entry.IsSelected = false;
            SelectedEntry = null;
            SelectFirstEntryIfAny();
        }

        _store.Remove(entry.Id);
        Entries.Remove(entry);

        if (deleteFile && File.Exists(entry.FilePath))
        {
            return await TryDeleteFileWithRetryAsync(entry.FilePath).ConfigureAwait(true);
        }

        return true;
    }

    /// <summary>Deselecting the entry above asks its <c>MediaElement</c> to let go of the file, but
    /// WPF's underlying media pipeline releases the OS file handle on its own background thread, so
    /// it can still be briefly locked by this same process by the time this runs.
    /// <see cref="FileSystem.DeleteFile"/>'s recycle-bin delete goes through the Windows Shell,
    /// which - if the file turns out to still be locked - shows its own blocking "File In Use"
    /// dialog rather than throwing an exception .NET code can catch and retry. So the file has to
    /// be confirmed actually unlocked *before* ever calling into the shell delete, rather than
    /// retrying the delete itself afterward.</summary>
    private static async Task<bool> TryDeleteFileWithRetryAsync(string filePath)
    {
        await WaitUntilUnlockedAsync(filePath).ConfigureAwait(true);

        try
        {
            FileSystem.DeleteFile(filePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.LogError("VideoLibraryRemove", ex);
            return false;
        }
    }

    /// <summary>Polls for exclusive access to <paramref name="filePath"/> - the only reliable way
    /// to know nothing (including this same process's own just-closed <c>MediaElement</c>) still
    /// has it open. Gives up after ~2 seconds and lets the caller proceed anyway; if something else
    /// entirely still has the file open at that point, the shell's own "File In Use" dialog is a
    /// reasonable fallback rather than a silent failure.</summary>
    private static async Task WaitUntilUnlockedAsync(string filePath)
    {
        const int maxAttempts = 15;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                return;
            }
            catch (IOException)
            {
                await Task.Delay(150).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // Not a sharing violation (e.g. permissions) - waiting longer won't help.
                return;
            }
        }
    }

    /// <summary>Updates the stored path after an in-place rename (e.g. the ring-rename flow),
    /// so the entry doesn't go "missing" once the original file no longer exists under its old name.</summary>
    public void UpdatePath(VideoLibraryEntryViewModel entry, string newPath)
    {
        _store.UpdatePath(entry.Id, newPath);
        entry.Entry.FilePath = newPath;
        entry.NotifyEntryChanged();
        EntryDataChanged?.Invoke(entry);
    }

    /// <summary>Tags an entry with a confirmed system/body/ring - e.g. once Ring Rotation's picker
    /// resolves them, so future selections of the same video auto-populate correctly.</summary>
    public void UpdateSystemBodyRing(
        VideoLibraryEntryViewModel entry, string systemName, long systemId64, double systemX, double systemY, double systemZ,
        string bodyName, string ringName)
    {
        _store.UpdateSystemBodyRing(entry.Id, systemName, systemId64, systemX, systemY, systemZ, bodyName, ringName);
        entry.Entry.SystemName = systemName;
        entry.Entry.SystemId64 = systemId64;
        entry.Entry.SystemX = systemX;
        entry.Entry.SystemY = systemY;
        entry.Entry.SystemZ = systemZ;
        entry.Entry.BodyName = bodyName;
        entry.Entry.RingName = ringName;
        entry.NotifyEntryChanged();
        EntryDataChanged?.Invoke(entry);
    }

    public void MarkAnalyzed(VideoLibraryEntryViewModel entry, string tabId)
    {
        _store.MarkAnalyzed(entry.Id, tabId);
        entry.Entry.Status = VideoLibraryEntryStatus.Analyzed;
        entry.NotifyEntryChanged();
    }

    private async Task GenerateThumbnailAsync(VideoLibraryEntryViewModel rowVm)
    {
        try
        {
            var fileName = await VideoThumbnailCache.GenerateAsync(rowVm.Id, rowVm.FilePath).ConfigureAwait(true);
            if (fileName is not null)
            {
                _store.UpdateThumbnail(rowVm.Id, fileName);
                rowVm.Entry.ThumbnailFileName = fileName;
                rowVm.RefreshThumbnail();
            }
        }
        catch (Exception ex)
        {
            AppLog.LogError("VideoLibraryThumbnail", ex);
        }

        try
        {
            // Same "finished file, never a still-growing recording" moment as the thumbnail above -
            // read via the Shell property system (near-instant, no frame decode) rather than
            // OpenCV, so this doesn't add a second slow file-open on top of the one above.
            var metadata = await Task.Run(() => QuickVideoMetadataReader.Read(rowVm.FilePath)).ConfigureAwait(true);
            var durationSeconds = metadata.Duration?.TotalSeconds;
            _store.UpdateVideoMetadata(rowVm.Id, metadata.Width, metadata.Height, durationSeconds);
            rowVm.Entry.VideoWidth = metadata.Width;
            rowVm.Entry.VideoHeight = metadata.Height;
            rowVm.Entry.VideoDurationSeconds = durationSeconds;
            rowVm.NotifyEntryChanged();
        }
        catch (Exception ex)
        {
            AppLog.LogError("VideoLibraryMetadata", ex);
        }
    }
}
