using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using VideoAnalysis.App.ViewModels;

namespace VideoAnalysis.App.Views;

public partial class VideoLibraryPanel : UserControl
{
    private static readonly TimeSpan SeekStep = TimeSpan.FromSeconds(5);

    public VideoLibraryPanel()
    {
        InitializeComponent();
        DataContextChanged += VideoLibraryPanel_DataContextChanged;
    }

    private void VideoLibraryPanel_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is VideoLibraryViewModel oldViewModel)
        {
            oldViewModel.EntrySelected -= OnEntrySelectedScrollToTop;
        }

        if (e.NewValue is VideoLibraryViewModel newViewModel)
        {
            newViewModel.EntrySelected += OnEntrySelectedScrollToTop;
        }
    }

    /// <summary>Selecting an entry always moves it to the top of the MRU-ordered list (see
    /// <see cref="VideoLibraryViewModel.Select"/>) - without this, picking one from further down
    /// the (possibly scrolled) list makes it look like it vanished, since it immediately jumps
    /// out of view to a spot the user isn't looking at (issue #54).</summary>
    private void OnEntrySelectedScrollToTop(VideoLibraryEntryViewModel entry) => LibraryScrollViewer.ScrollToTop();

    /// <summary>Each library row carries its own <c>Player</c> MediaElement (shown in place of
    /// the static thumbnail once that row becomes selected). Wiring is per-instance because
    /// WPF gives each DataTemplate instantiation its own name scope - there is no single shared
    /// player to reach from a panel-level property.</summary>
    private void EntryRoot_Loaded(object sender, RoutedEventArgs e)
    {
        var root = (FrameworkElement)sender;
        if (root.DataContext is not VideoLibraryEntryViewModel entryVm)
        {
            return;
        }

        var player = (MediaElement)root.FindName("Player");
        var loadingOverlay = (FrameworkElement)root.FindName("LoadingOverlay");

        player.MediaOpened += (_, _) =>
        {
            // The video is decoded enough to show a frame - pause it there rather than letting
            // it auto-play, and swap the loading overlay/thumbnail out for the real player.
            player.Pause();
            loadingOverlay.Visibility = Visibility.Collapsed;
            player.Visibility = Visibility.Visible;
        };
        player.MediaFailed += (_, _) =>
        {
            // Fall back to the static thumbnail rather than leaving a blank/broken player visible.
            loadingOverlay.Visibility = Visibility.Collapsed;
            player.Visibility = Visibility.Collapsed;
        };

        void Handler(object? s, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(VideoLibraryEntryViewModel.IsSelected))
            {
                UpdatePlayerSource(player, loadingOverlay, entryVm);
            }
        }

        entryVm.PropertyChanged += Handler;
        root.Tag = (PropertyChangedEventHandler)Handler;
        UpdatePlayerSource(player, loadingOverlay, entryVm);
    }

    private void EntryRoot_Unloaded(object sender, RoutedEventArgs e)
    {
        var root = (FrameworkElement)sender;
        if (root.DataContext is VideoLibraryEntryViewModel entryVm && root.Tag is PropertyChangedEventHandler handler)
        {
            entryVm.PropertyChanged -= handler;
        }
    }

    private static void UpdatePlayerSource(MediaElement player, FrameworkElement loadingOverlay, VideoLibraryEntryViewModel entryVm)
    {
        if (entryVm.IsSelected && !entryVm.IsFileMissing)
        {
            // Keep the player hidden (thumbnail still showing underneath, loading overlay on
            // top) until MediaOpened fires - Play() is what actually triggers WPF to open and
            // decode the file; MediaOpened then pauses it back to a static first frame.
            player.Visibility = Visibility.Collapsed;
            loadingOverlay.Visibility = Visibility.Visible;
            player.Source = new Uri(entryVm.FilePath);
            player.Play();
        }
        else
        {
            // Close(), not just Stop()+Source=null - Close() is what actually tears down the
            // underlying media session and releases the OS file handle, rather than leaving it to
            // be cleaned up whenever the next Source assignment happens to trigger it. Still not a
            // synchronous guarantee (WPF releases the handle on its own background thread), which
            // is why VideoLibraryViewModel.RemoveAsync retries the delete that follows this.
            player.Close();
            player.Visibility = Visibility.Collapsed;
            loadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Keeps the thumbnail/player box locked to a 16:9 aspect ratio of whatever width
    /// it's actually given, rather than a guessed fixed height - that's what lets it span the
    /// full width of the panel edge-to-edge while still showing the complete frame with no
    /// cropping (a fixed height only matches width coincidentally at one specific panel size).</summary>
    private const double AspectRatio = 16.0 / 9.0;

    private void ThumbnailContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var container = (FrameworkElement)sender;
        if (e.NewSize.Width <= 0)
        {
            return;
        }

        var targetHeight = e.NewSize.Width / AspectRatio;
        if (double.IsNaN(container.Height) || Math.Abs(container.Height - targetHeight) > 0.5)
        {
            container.Height = targetHeight;
        }
    }

    private void LibraryScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not VideoLibraryViewModel viewModel)
        {
            return;
        }

        const double nearBottomThreshold = 100;
        if (LibraryScrollViewer.VerticalOffset >= LibraryScrollViewer.ScrollableHeight - nearBottomThreshold)
        {
            viewModel.LoadNextPage();
        }
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e) => WithPlayer(sender, p => p.Play());

    private void PauseButton_Click(object sender, RoutedEventArgs e) => WithPlayer(sender, p => p.Pause());

    private void StopButton_Click(object sender, RoutedEventArgs e) => WithPlayer(sender, p => p.Stop());

    private void FastForwardButton_Click(object sender, RoutedEventArgs e) => WithPlayer(sender, p => Seek(p, SeekStep));

    private void RewindButton_Click(object sender, RoutedEventArgs e) => WithPlayer(sender, p => Seek(p, -SeekStep));

    private static void WithPlayer(object sender, Action<MediaElement> action)
    {
        if (((Button)sender).Tag is MediaElement player)
        {
            action(player);
        }
    }

    private static void Seek(MediaElement player, TimeSpan delta)
    {
        var target = player.Position + delta;
        if (target < TimeSpan.Zero)
        {
            target = TimeSpan.Zero;
        }
        else if (player.NaturalDuration.HasTimeSpan && target > player.NaturalDuration.TimeSpan)
        {
            target = player.NaturalDuration.TimeSpan;
        }

        player.Position = target;
    }
}
