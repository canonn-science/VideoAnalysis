using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ModernWpf.Controls;
using VideoAnalysis.App.ViewModels;
using VideoAnalysis.Core.Diagnostics;
using VideoAnalysis.Core.Spansh.Models;
using VideoAnalysis.Core.Storage;

namespace VideoAnalysis.App.Views;

public partial class VideoUploadMetadataWindow : Window
{
    private readonly VideoUploadMetadataViewModel _viewModel;
    private readonly bool _autoDetectFromFilename;
    private readonly bool _organizeBySystemFolder;
    private CancellationTokenSource? _searchDebounceCts;
    private string _filePath;

    public VideoLibraryEntry? ResultEntry { get; private set; }

    /// <summary><paramref name="autoDetectFromFilename"/> triggers the filename/journal-history
    /// system detection once this window is actually visible - rather than before it's shown -
    /// so the modal's busy spinner (bound to <see cref="VideoUploadMetadataViewModel.IsBusy"/>)
    /// is there to give feedback while the lookup runs, instead of the user staring at a delayed,
    /// unexplained pause before the dialog even appears. <paramref name="organizeBySystemFolder"/>
    /// is the current Configuration-tab setting for whether an accepted rename also moves the file
    /// into a per-system subfolder.</summary>
    public VideoUploadMetadataWindow(
        VideoUploadMetadataViewModel viewModel, string filePath, bool autoDetectFromFilename = false, bool organizeBySystemFolder = false)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _filePath = filePath;
        _autoDetectFromFilename = autoDetectFromFilename;
        _organizeBySystemFolder = organizeBySystemFolder;
        DataContext = _viewModel;
        Loaded += VideoUploadMetadataWindow_Loaded;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private async void VideoUploadMetadataWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= VideoUploadMetadataWindow_Loaded;
        UpdateRenameSuggestion();
        if (_autoDetectFromFilename)
        {
            await _viewModel.TryDetectSystemFromFilenameAsync(_filePath);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VideoUploadMetadataViewModel.SuggestedFileBaseName)
            or nameof(VideoUploadMetadataViewModel.ResolvedSystemName))
        {
            UpdateRenameSuggestion();
        }
    }

    /// <summary>Shows/hides <see cref="RenameCheckBox"/> and refreshes its label with the name
    /// (and, if enabled, per-system subfolder) the file would be renamed to - hidden entirely once
    /// the current filename already matches, mirroring <see cref="VideoProcessingWindow"/>'s own
    /// rename-to-match-object prompt but as an inline opt-out checkbox instead of a separate modal,
    /// since this dialog is already the point where the user is entering that metadata.</summary>
    private void UpdateRenameSuggestion()
    {
        var suggested = _viewModel.SuggestedFileBaseName;
        if (string.IsNullOrWhiteSpace(suggested) || VideoFileNamer.MatchesRingName(_filePath, suggested))
        {
            RenameCheckBox.Visibility = Visibility.Collapsed;
            RenameCheckBox.IsChecked = false;
            return;
        }

        var systemName = _viewModel.ResolvedSystemName;
        string? targetDirectory = null;
        if (_organizeBySystemFolder && !string.IsNullOrWhiteSpace(systemName))
        {
            targetDirectory = Path.Combine(Path.GetDirectoryName(_filePath) ?? string.Empty, VideoFileNamer.Sanitize(systemName));
        }

        var suggestedPath = VideoFileNamer.GetNextAvailableFileName(_filePath, suggested, targetDirectory);
        var suggestedFileName = Path.GetFileName(suggestedPath);
        var label = targetDirectory is not null
            ? $"Rename file to \"{suggestedFileName}\" in a \"{systemName}\" folder"
            : $"Rename file to \"{suggestedFileName}\"";
        RenameCheckBox.Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap };
        RenameCheckBox.Visibility = Visibility.Visible;
        RenameCheckBox.IsChecked = true;
    }

    /// <summary>Applies the pending rename (and, if enabled, moves the file into a per-system
    /// subfolder, creating it as needed) before the entry is built, so the video library ends up
    /// with the final path from the moment the entry is created - not a stale one that needs a
    /// separate update afterward.</summary>
    private void ApplyPendingRename()
    {
        var suggested = _viewModel.SuggestedFileBaseName;
        if (string.IsNullOrWhiteSpace(suggested))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_filePath) ?? string.Empty;
            string? targetDirectory = null;
            var systemName = _viewModel.ResolvedSystemName;
            if (_organizeBySystemFolder && !string.IsNullOrWhiteSpace(systemName))
            {
                targetDirectory = Path.Combine(directory, VideoFileNamer.Sanitize(systemName));
                Directory.CreateDirectory(targetDirectory);
            }

            var newPath = VideoFileNamer.GetNextAvailableFileName(_filePath, suggested, targetDirectory);
            File.Move(_filePath, newPath);
            _filePath = newPath;
        }
        catch (Exception ex)
        {
            AppLog.LogError("VideoUploadMetadataRename", ex);
            _viewModel.ErrorMessage = $"Rename failed: {ex.Message}";
        }
    }

    private async void SystemSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        _searchDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;

        try
        {
            await Task.Delay(300, cts.Token);
            await _viewModel.RefreshSuggestionsAsync(sender.Text, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer keystroke
        }
    }

    private async void SystemSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is SpanshSearchSystem system)
        {
            await _viewModel.SelectSystemAsync(system);
        }
    }

    private async void SystemSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var chosen = args.ChosenSuggestion as SpanshSearchSystem;
        await _viewModel.SelectSystemAsync(chosen);
    }

    private async void LookUpButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SelectSystemAsync(null);
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SystemQuery.Trim().Length == 0)
        {
            _viewModel.ErrorMessage = "Enter a system name.";
            return;
        }

        if (RenameCheckBox.Visibility == Visibility.Visible && RenameCheckBox.IsChecked == true)
        {
            ApplyPendingRename();
        }

        ResultEntry = _viewModel.BuildEntry(_filePath);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
