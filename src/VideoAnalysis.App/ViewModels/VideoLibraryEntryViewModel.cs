using System.IO;
using System.Windows.Media.Imaging;
using VideoAnalysis.App.Infrastructure;
using VideoAnalysis.Core.Storage;

namespace VideoAnalysis.App.ViewModels;

/// <summary>Wraps a single <see cref="VideoLibraryEntry"/> for display in the library panel:
/// a lazily-loaded thumbnail, a computed missing-file flag (the library never copies videos, so
/// the original file can vanish out from under it), and display text.</summary>
public sealed class VideoLibraryEntryViewModel : ObservableObject
{
    private BitmapImage? _thumbnailImageSource;
    private bool _thumbnailLoadAttempted;
    private bool _isSelected;

    public VideoLibraryEntryViewModel(VideoLibraryEntry entry, Action<VideoLibraryEntryViewModel>? onSelect = null)
    {
        Entry = entry;
        SelectCommand = new RelayCommand(() => onSelect?.Invoke(this));
    }

    public VideoLibraryEntry Entry { get; }

    public RelayCommand SelectCommand { get; }

    public Guid Id => Entry.Id;

    /// <summary>True for the single entry currently active in the library panel - shows the
    /// player and transport controls inline in place of the static thumbnail, rather than in a
    /// separate duplicate preview area.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public string FilePath => Entry.FilePath;

    public string FileName => Path.GetFileName(Entry.FilePath);

    public bool IsFileMissing => !File.Exists(Entry.FilePath);

    public string DisplayLabel
    {
        get
        {
            var parts = new[] { Entry.SystemName, Entry.BodyName, Entry.RingName, Entry.StationName }
                .Where(s => !string.IsNullOrWhiteSpace(s));
            var joined = string.Join(" / ", parts);
            return joined.Length > 0 ? joined : FileName;
        }
    }

    public string StatusText
    {
        get
        {
            if (IsFileMissing)
            {
                return "Missing file";
            }

            return Entry.Status == VideoLibraryEntryStatus.Analyzed ? "Analyzed" : "Not analyzed";
        }
    }

    /// <summary>Lazily resolved from the on-disk thumbnail cache the first time this is read,
    /// so scrolling a long, lazily-loaded list doesn't decode every thumbnail up front.</summary>
    public BitmapImage? ThumbnailImageSource
    {
        get
        {
            if (!_thumbnailLoadAttempted)
            {
                _thumbnailLoadAttempted = true;
                TryLoadThumbnail();
            }

            return _thumbnailImageSource;
        }
    }

    /// <summary>Called once background thumbnail generation completes (or to retry a thumbnail
    /// that failed to load), so the panel picks up the newly cached image.</summary>
    public void RefreshThumbnail()
    {
        _thumbnailLoadAttempted = false;
        _thumbnailImageSource = null;
        OnPropertyChanged(nameof(ThumbnailImageSource));
    }

    /// <summary>Called after the underlying <see cref="Entry"/> has been mutated in place
    /// (status/path updates) so bound display properties refresh.</summary>
    public void NotifyEntryChanged()
    {
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(IsFileMissing));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DisplayLabel));
    }

    private void TryLoadThumbnail()
    {
        if (Entry.ThumbnailFileName is null)
        {
            return;
        }

        var path = Path.Combine(VideoThumbnailCache.Directory, Entry.ThumbnailFileName);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            _thumbnailImageSource = image;
        }
        catch
        {
            // A partially-written or corrupted thumbnail file shouldn't crash the library panel -
            // just leave the thumbnail blank, RefreshThumbnail can retry later.
        }
    }
}
