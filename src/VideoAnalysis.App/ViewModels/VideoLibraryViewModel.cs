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
    private VideoLibraryEntryViewModel? _selectedEntry;
    private bool _isLoadingMore;

    public VideoLibraryViewModel(VideoLibraryStore store)
    {
        _store = store;
        UploadCommand = new RelayCommand(() => UploadRequested?.Invoke());
        LoadInitialPage();
    }

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

    /// <summary>Raised when the user clicks an entry's remove button; the view handles the
    /// confirmation prompt (index-only vs. also deleting the file), then calls <see cref="Remove"/>.</summary>
    public event Action<VideoLibraryEntryViewModel>? RemoveRequested;

    public void LoadInitialPage()
    {
        Entries.Clear();
        foreach (var entry in _store.GetPage(0, PageSize))
        {
            Entries.Add(new VideoLibraryEntryViewModel(entry, Select, RequestRemove));
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
                Entries.Add(new VideoLibraryEntryViewModel(entry, Select, RequestRemove));
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
            rowVm = Entries.FirstOrDefault(e => e.Id == existing.Id) ?? new VideoLibraryEntryViewModel(existing, Select, RequestRemove);
        }
        else
        {
            var added = _store.Add(entry);
            rowVm = new VideoLibraryEntryViewModel(added, Select, RequestRemove);
            Entries.Insert(0, rowVm);
            _ = GenerateThumbnailAsync(rowVm);
        }

        Select(rowVm);
        return rowVm;
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
    public bool Remove(VideoLibraryEntryViewModel entry, bool deleteFile)
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
            try
            {
                FileSystem.DeleteFile(entry.FilePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            catch (Exception ex)
            {
                AppLog.LogError("VideoLibraryRemove", ex);
                return false;
            }
        }

        return true;
    }

    /// <summary>Updates the stored path after an in-place rename (e.g. the ring-rename flow),
    /// so the entry doesn't go "missing" once the original file no longer exists under its old name.</summary>
    public void UpdatePath(VideoLibraryEntryViewModel entry, string newPath)
    {
        _store.UpdatePath(entry.Id, newPath);
        entry.Entry.FilePath = newPath;
        entry.NotifyEntryChanged();
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
    }
}
