using VideoAnalysis.App.Infrastructure;

namespace VideoAnalysis.App.ViewModels;

/// <summary>A single row in the Configuration tab's watched-folders list - wraps a plain path
/// string with Remove/Move-Up/Move-Down commands that delegate back to
/// <see cref="MainViewModel"/>, matching the callback-command shape
/// <see cref="VideoLibraryEntryViewModel"/> already uses for its own row actions.</summary>
public sealed class WatchedFolderRowViewModel
{
    public WatchedFolderRowViewModel(
        string path, Action<WatchedFolderRowViewModel> onRemove,
        Action<WatchedFolderRowViewModel> onMoveUp, Action<WatchedFolderRowViewModel> onMoveDown)
    {
        Path = path;
        RemoveCommand = new RelayCommand(() => onRemove(this));
        MoveUpCommand = new RelayCommand(() => onMoveUp(this));
        MoveDownCommand = new RelayCommand(() => onMoveDown(this));
    }

    public string Path { get; }

    public RelayCommand RemoveCommand { get; }

    public RelayCommand MoveUpCommand { get; }

    public RelayCommand MoveDownCommand { get; }
}
