using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Microsoft.Win32;
using ModernWpf.Controls;
using VideoAnalysis.App.Infrastructure;
using VideoAnalysis.App.ViewModels;
using VideoAnalysis.Core.Diagnostics;
using VideoAnalysis.Core.Domain;
using VideoAnalysis.Core.Storage;
using VideoAnalysis.Core.Updates;
using VideoAnalysis.Core.VideoAnalysis;
using VideoAnalysis.Core.VideoAnalysis.LongExposure;

namespace VideoAnalysis.App.Views;

public partial class MainWindow : Window
{
    private const string UpdateRepoOwner = "canonn-science";
    private const string UpdateRepoName = "VideoAnalysis";

    private static readonly string DefaultLongExposureOutputRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "RotationAnalysisLab", "LongExposure");

    /// <summary>Defaults under the user's Pictures folder, but once a save goes somewhere else
    /// (see <see cref="UpdateLongExposureOutputDirectoryIfChanged"/>), that folder is remembered
    /// and used as the root for subsequent saves instead.</summary>
    private string LongExposureOutputRoot => _viewModel.LongExposureOutputDirectory ?? DefaultLongExposureOutputRoot;

    private readonly MainViewModel _viewModel = new();
    private readonly UpdateChecker _updateChecker = new();
    private CancellationTokenSource? _searchDebounceCts;
    private CancellationTokenSource? _stationSearchDebounceCts;
    private CancellationTokenSource? _jetConeSearchDebounceCts;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.VideoLibrary.UploadRequested += OnLibraryUploadRequested;
        _viewModel.Measurements.SubmissionFailed += OnCanonnSubmissionFailed;
        _viewModel.VideoLibrary.EntrySelected += OnLibraryEntrySelectedForRingRotation;
        _viewModel.VideoLibrary.EntrySelected += OnLibraryEntrySelectedForStationRotation;
        _viewModel.VideoLibrary.EntrySelected += OnLibraryEntrySelectedForSlitScan;
        _viewModel.VideoLibrary.EntrySelected += OnLibraryEntrySelectedForLongExposure;
        _viewModel.VideoLibrary.EntrySelected += OnLibraryEntrySelectedForJetCone;
        _viewModel.VideoLibrary.SelectFirstEntryIfAny();
        Closed += (_, _) =>
        {
            _viewModel.Dispose();
            _updateChecker.Dispose();
        };
        Loaded += async (_, _) => await CheckForUpdatesAsync();
        VersionText.Text = $"Version v{GetCurrentVersion().ToString(3)}";
        UpdateClaudeApiKeyStatusText();
    }

    private void UpdateClaudeApiKeyStatusText()
    {
        ClaudeApiKeyStatusText.Text = _viewModel.HasClaudeApiKey ? "A key is configured." : "No key configured.";
    }

    private static Version GetCurrentVersion() => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private async Task CheckForUpdatesAsync()
    {
        UpdateInfo? update;
        try
        {
            update = await _updateChecker.CheckForUpdateAsync(UpdateRepoOwner, UpdateRepoName, GetCurrentVersion());
        }
        catch (Exception ex)
        {
            AppLog.LogError("CheckForUpdates", ex);
            return;
        }

        if (update is null)
        {
            return;
        }

        var promptResult = await new ContentDialog
        {
            Title = "Update available",
            Content = $"Video Analysis Lab {update.Version} is available. Download and install it now?",
            PrimaryButtonText = "Update Now",
            CloseButtonText = "Later",
        }.ShowAsync();

        if (promptResult != ContentDialogResult.Primary)
        {
            return;
        }

        var downloadWindow = new UpdateDownloadWindow(_updateChecker, update) { Owner = this };
        if (downloadWindow.ShowDialog() == true && downloadWindow.InstallerPath is string installerPath)
        {
            Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        else if (downloadWindow.FailureMessage is not null)
        {
            await new ContentDialog
            {
                Title = "Update failed",
                Content = downloadWindow.FailureMessage,
                CloseButtonText = "OK",
            }.ShowAsync();
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
        if (args.SelectedItem is Core.Spansh.Models.SpanshSearchSystem system)
        {
            await _viewModel.SubmitAsync(system);
        }
    }

    private async void SystemSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var chosen = args.ChosenSuggestion as Core.Spansh.Models.SpanshSearchSystem;
        await _viewModel.SubmitAsync(chosen);
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SubmitAsync(null);
    }

    private async void OnCanonnSubmissionFailed(string message)
    {
        await new ContentDialog
        {
            Title = "Send to Canonn failed",
            Content = message,
            CloseButtonText = "OK",
        }.ShowAsync();
    }

    /// <summary>Only reachable once <see cref="MainViewModel.CanAnalyzeRing"/> gates the button
    /// enabled, so a non-missing <see cref="MainViewModel.ActiveLibraryVideo"/> and a
    /// <see cref="MainViewModel.SelectedRing"/> are already guaranteed.</summary>
    private async void RingRotationAnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedRing is not { } row || _viewModel.ActiveLibraryVideo is not { IsFileMissing: false } libraryEntry)
        {
            return;
        }

        var videoPath = libraryEntry.FilePath;

        // The filename is usually the strongest signal that this is the right video for the
        // selected system - if it doesn't mention it at all, double-check before tagging/
        // analyzing, since that combination could easily be an accidental mismatch.
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(videoPath);
        if (!FilenameSystemMatcher.IsNameInFilename(row.Ring.SystemName, fileNameWithoutExtension))
        {
            var mismatchResult = await new ContentDialog
            {
                Title = "System doesn't match filename",
                Content = $"The selected system \"{row.Ring.SystemName}\" doesn't appear in this video's filename (\"{Path.GetFileName(videoPath)}\"). Continue anyway?",
                PrimaryButtonText = "Continue",
                CloseButtonText = "Cancel",
            }.ShowAsync();

            if (mismatchResult != ContentDialogResult.Primary)
            {
                return;
            }
        }

        // Tag the video with whatever system/body/ring is currently selected, so future
        // selections of it auto-populate correctly even if it wasn't (fully) tagged before.
        _viewModel.VideoLibrary.UpdateSystemBodyRing(
            libraryEntry,
            row.Ring.SystemName, row.Ring.SystemId64, row.Ring.SystemX, row.Ring.SystemY, row.Ring.SystemZ,
            row.Ring.BodyName, row.Ring.RingName);

        // Kick this off now rather than waiting for VideoProcessingWindow's Loaded event, so the
        // shell round-trip overlaps window construction/layout instead of starting after it.
        var quickMetadataTask = Task.Run(() => QuickVideoMetadataReader.Read(videoPath));

        var processingWindow = new VideoProcessingWindow(_viewModel.AnalyzeVideoAsync, videoPath, row.Ring.EstimatedPeriodSeconds, quickMetadataTask, row.Ring.RingName) { Owner = this };
        var completed = processingWindow.ShowDialog();

        if (completed == true && processingWindow.Result is not null)
        {
            var result = processingWindow.Result;
            var finalVideoPath = processingWindow.FinalVideoPath;

            if (!string.Equals(finalVideoPath, videoPath, StringComparison.OrdinalIgnoreCase))
            {
                // The in-place ring-rename flow (VideoRenamePromptWindow/VideoFileNamer) may have
                // renamed the source file - keep the library entry pointing at the real file
                // instead of letting it go "missing".
                _viewModel.VideoLibrary.UpdatePath(libraryEntry, finalVideoPath);
            }

            var resultsWindow = new ResultsWindow(
                row.Ring.SystemName, row.Ring.BodyName, "Ring:", row.Ring.RingName,
                row.Ring.EstimatedPeriodSeconds, result, finalVideoPath,
                ct => _viewModel.SubmitMeasurementToCanonnAsync(row, result, ct))
            { Owner = this };
            if (resultsWindow.ShowDialog() == true)
            {
                _viewModel.SaveMeasurement(row, result, finalVideoPath, resultsWindow.SubmittedToCanonn);
                _viewModel.VideoLibrary.MarkAnalyzed(libraryEntry, "RingRotation");
            }
        }
        else if (processingWindow.FailureMessage is not null)
        {
            await new ContentDialog
            {
                Title = "Video analysis failed",
                Content = processingWindow.FailureMessage,
                CloseButtonText = "OK",
            }.ShowAsync();
        }
    }

    private void OnLibraryUploadRequested()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a video",
            Filter = "Video files (*.mp4;*.mkv;*.avi;*.mov;*.wmv)|*.mp4;*.mkv;*.avi;*.mov;*.wmv|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        PromptAddVideoToLibrary(dialog.FileName);
    }

    /// <summary>Shows the metadata modal for a freshly picked video and, if the user confirms,
    /// adds it to the library - the modal auto-detects the system from the filename (falling back
    /// to journal history) once it's shown, see <see cref="VideoUploadMetadataWindow"/>'s
    /// constructor doc.</summary>
    private VideoLibraryEntryViewModel? PromptAddVideoToLibrary(string videoPath)
    {
        var metadataViewModel = new VideoUploadMetadataViewModel(_viewModel.SpanshClient, _viewModel.JournalMonitor);
        var metadataWindow = new VideoUploadMetadataWindow(
            metadataViewModel, videoPath,
            autoDetectFromFilename: true,
            organizeBySystemFolder: _viewModel.OrganizeRenamedVideosBySystem)
        { Owner = this };
        if (metadataWindow.ShowDialog() != true || metadataWindow.ResultEntry is not { } entry)
        {
            return null;
        }

        return _viewModel.VideoLibrary.AddFromUpload(entry);
    }

    private async void StationSystemSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        _stationSearchDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _stationSearchDebounceCts = cts;

        try
        {
            await Task.Delay(300, cts.Token);
            await _viewModel.Stations.RefreshSuggestionsAsync(sender.Text, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer keystroke
        }
    }

    private async void StationSystemSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is Core.Spansh.Models.SpanshSearchSystem system)
        {
            await _viewModel.Stations.SubmitAsync(system);
        }
    }

    private async void StationSystemSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var chosen = args.ChosenSuggestion as Core.Spansh.Models.SpanshSearchSystem;
        await _viewModel.Stations.SubmitAsync(chosen);
    }

    private async void StationSubmitButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.Stations.SubmitAsync(null);
    }

    /// <summary>Only reachable once <see cref="StationViewModel.CanAnalyze"/> gates the button
    /// enabled, so a video and a <see cref="StationViewModel.SelectedStation"/> are already
    /// guaranteed.</summary>
    private async void StationAnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Stations.SelectedStation is not { } row || _viewModel.Stations.VideoFilePath is not { } videoPath)
        {
            return;
        }

        var quickMetadataTask = Task.Run(() => QuickVideoMetadataReader.Read(videoPath));

        var processingWindow = new VideoProcessingWindow(_viewModel.Stations.AnalyzeVideoAsync, videoPath, row.Station.EstimatedRotationSeconds, quickMetadataTask, row.Station.StationName) { Owner = this };
        var completed = processingWindow.ShowDialog();

        if (completed == true && processingWindow.Result is not null)
        {
            var result = processingWindow.Result;
            var finalVideoPath = processingWindow.FinalVideoPath;
            var resultsWindow = new ResultsWindow(
                row.Station.SystemName, row.Station.BodyName ?? "N/A", "Station:", row.Station.StationName,
                row.Station.EstimatedRotationSeconds, result, finalVideoPath,
                submitToCanonn: null)
            { Owner = this };
            if (resultsWindow.ShowDialog() == true)
            {
                _viewModel.Stations.SaveMeasurement(row, result, finalVideoPath);
            }
        }
        else if (processingWindow.FailureMessage is not null)
        {
            await new ContentDialog
            {
                Title = "Video analysis failed",
                Content = processingWindow.FailureMessage,
                CloseButtonText = "OK",
            }.ShowAsync();
        }
    }

    private async void AddReplaceClaudeApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var input = new PasswordBox { MinWidth = 320 };
        var dialog = new ContentDialog
        {
            Title = "Claude API key",
            Content = input,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        input.Loaded += (_, _) => input.Focus();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var key = input.Password.Trim();
        if (key.Length > 0)
        {
            _viewModel.SetClaudeApiKey(key);
            UpdateClaudeApiKeyStatusText();
        }
    }

    private async void DeleteClaudeApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmResult = await new ContentDialog
        {
            Title = "Delete Claude API key?",
            Content = "You'll need to enter a new key to use Claude's vision model again.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
        }.ShowAsync();

        if (confirmResult != ContentDialogResult.Primary)
        {
            return;
        }

        _viewModel.DeleteClaudeApiKey();
        UpdateClaudeApiKeyStatusText();
    }

    private async void JetConeSystemSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        _jetConeSearchDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _jetConeSearchDebounceCts = cts;

        try
        {
            await Task.Delay(300, cts.Token);
            await _viewModel.JetCone.RefreshSuggestionsAsync(sender.Text, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer keystroke
        }
    }

    private async void JetConeSystemSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is Core.Spansh.Models.SpanshSearchSystem system)
        {
            await _viewModel.JetCone.SubmitAsync(system);
        }
    }

    private async void JetConeSystemSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var chosen = args.ChosenSuggestion as Core.Spansh.Models.SpanshSearchSystem;
        await _viewModel.JetCone.SubmitAsync(chosen);
    }

    private async void JetConeSubmitButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.JetCone.SubmitAsync(null);
    }

    /// <summary>Local-OCR confidence (0-1) below which a configured Claude key is used
    /// automatically instead of the local guess. HudDistanceReader's heuristic classifier
    /// reliably scores well below this on real footage - see its class doc comment - so in
    /// practice this means Claude is tried whenever a key is present, and the local guess is
    /// mostly a fallback for when no key is configured yet.</summary>
    private const double TrustedLocalConfidenceThreshold = 0.55;

    /// <summary>Jet Cone still needs a specific neutron star/white dwarf's full physical data for
    /// the CSV (age, mass, orbital elements, ...), which only Spansh has - so selecting a library
    /// video auto-resolves its tagged system into the compact target picker (preferring the
    /// tagged body, if any) rather than requiring the user to search and pick it every time.</summary>
    private async void OnLibraryEntrySelectedForJetCone(VideoLibraryEntryViewModel entry)
    {
        if (entry.IsFileMissing)
        {
            return;
        }

        _viewModel.JetCone.ErrorMessage = null;
        _viewModel.JetCone.ResetReview();
        _viewModel.JetCone.VideoFilePath = entry.FilePath;
        JetConePreviewImage.Source = null;

        if (entry.Entry.SystemId64 is long id64)
        {
            var syntheticSystem = new Core.Spansh.Models.SpanshSearchSystem
            {
                Id64 = id64,
                Name = entry.Entry.SystemName ?? string.Empty,
                X = entry.Entry.SystemX ?? 0,
                Y = entry.Entry.SystemY ?? 0,
                Z = entry.Entry.SystemZ ?? 0,
            };
            await _viewModel.JetCone.SubmitAsync(syntheticSystem, preferredBodyName: entry.Entry.BodyName);
        }
else
{
    _viewModel.JetCone.Targets.Clear();
    _viewModel.JetCone.SelectedTarget = null;
    _viewModel.JetCone.ResolvedSystemName = null;

    _viewModel.JetCone.ErrorMessage =
        "This video isn't tagged with a system - search for one above, then pick its neutron star or white dwarf below.";
}

        byte[]? previewFrame;
        try
        {
            previewFrame = await _viewModel.JetCone.LoadPreviewFrameAsync(CancellationToken.None);
        }
        catch
        {
            previewFrame = null;
        }

        // The user may have selected a different video while this was loading.
        if (_viewModel.JetCone.VideoFilePath == entry.FilePath && previewFrame is not null)
        {
            JetConePreviewImage.Source = ToBitmapImage(previewFrame);
        }
    }

    private async void JetConeAnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        var videoPath = _viewModel.JetCone.VideoFilePath;
        if (videoPath is null)
        {
            _viewModel.JetCone.ErrorMessage = "Select a video from the library first.";
            return;
        }

        if (_viewModel.JetCone.SelectedTarget is null)
        {
            _viewModel.JetCone.ErrorMessage = "Pick a neutron star or white dwarf target first.";
            return;
        }

        _viewModel.JetCone.ErrorMessage = null;
        _viewModel.JetCone.ResetReview();

        var processingWindow = new JetConeProcessingWindow(_viewModel.JetCone.AnalyzeVideoAsync, videoPath) { Owner = this };
        if (processingWindow.ShowDialog() != true || processingWindow.Result is not { } result)
        {
            if (processingWindow.FailureMessage is not null)
            {
                await new ContentDialog
                {
                    Title = "Jet cone analysis failed",
                    Content = processingWindow.FailureMessage,
                    CloseButtonText = "OK",
                }.ShowAsync();
            }
            return;
        }

        if (!result.OnsetDetected)
        {
            _viewModel.JetCone.ErrorMessage = "Couldn't find the \"FSD OPERATING / BEYOND SAFETY LIMITS\" warning in this recording. Make sure it shows the approach all the way through the warning appearing.";
            return;
        }

        double? prefill = result.LocalDistanceLs;
        string sourceLabel = result.LocalConfidence >= TrustedLocalConfidenceThreshold
            ? $"Local reading (confidence {result.LocalConfidence:P0})"
            : $"Local reading, low confidence ({result.LocalConfidence:P0}) - please verify";

        if (result.LocalConfidence < TrustedLocalConfidenceThreshold && _viewModel.JetCone.HasClaudeApiKey)
        {
            try
            {
                var claudeReading = await _viewModel.JetCone.ReadDistanceWithClaudeAsync(result.BottomLeftCropPng);
                prefill = claudeReading.DistanceLs;
                sourceLabel = $"Claude vision (confidence {claudeReading.Confidence}%)";
            }
            catch (Exception ex)
            {
                AppLog.LogError("ClaudeVisionFallback", ex);
                // Fall through with the local guess already assigned above.
            }
        }

        _viewModel.JetCone.BeginReview(result, prefill, sourceLabel);
        JetConeReticleImage.Source = ToBitmapImage(result.ReticleCropPng);
        JetConeBottomLeftImage.Source = ToBitmapImage(result.BottomLeftCropPng);
        JetConeDistanceTextBox.Text = prefill is double d ? d.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
    }

    private async void JetConeSaveMeasurementButton_Click(object sender, RoutedEventArgs e)
    {
        var text = JetConeDistanceTextBox.Text.Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            _viewModel.JetCone.ErrorMessage = "Enter a valid, non-negative distance in light seconds.";
            return;
        }

        if (_viewModel.JetCone.SelectedTarget is null)
        {
            _viewModel.JetCone.ErrorMessage = "Pick a neutron star or white dwarf target first.";
            return;
        }

        _viewModel.JetCone.ErrorMessage = null;
        _viewModel.JetCone.DistanceLs = value;

        if (_viewModel.JetCone.DistanceWasCorrected && !_viewModel.JetCone.HasClaudeApiKey)
        {
            await OfferClaudeApiKeySetupAsync();
        }

        _viewModel.JetCone.SaveMeasurement();
        _viewModel.JetCone.ResetReview();
        JetConeDistanceTextBox.Text = string.Empty;
    }

    private void JetConeDiscardReviewButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.JetCone.ResetReview();
        JetConeDistanceTextBox.Text = string.Empty;
    }

    private async Task OfferClaudeApiKeySetupAsync()
    {
        var offer = await new ContentDialog
        {
            Title = "Improve future readings?",
            Content = "Local text recognition struggled with this frame. Want to provide a Claude API key so future readings like this can use Claude's vision model instead? You can remove it any time from the About tab.",
            PrimaryButtonText = "Add API Key",
            CloseButtonText = "Not now",
        }.ShowAsync();

        if (offer != ContentDialogResult.Primary)
        {
            return;
        }

        var input = new PasswordBox { MinWidth = 320 };
        var keyDialog = new ContentDialog
        {
            Title = "Claude API key",
            Content = input,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        input.Loaded += (_, _) => input.Focus();

        if (await keyDialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var key = input.Password.Trim();
            if (key.Length > 0)
            {
                _viewModel.SetClaudeApiKey(key);
                UpdateClaudeApiKeyStatusText();
            }
        }
    }

    /// <summary>Long Exposure only ever needed a system/body/station identity to name its output
    /// files - the selected library entry already carries that, so (like Slit Scan) selecting any
    /// non-missing library video just loads it here directly.</summary>
    private void OnLibraryEntrySelectedForLongExposure(VideoLibraryEntryViewModel entry)
    {
        if (entry.IsFileMissing)
        {
            return;
        }

        _viewModel.LongExposure.ErrorMessage = null;
        _viewModel.LongExposure.Result = null;
        _viewModel.LongExposure.VideoFilePath = entry.FilePath;
        _viewModel.LongExposure.SystemName = entry.Entry.SystemName;
        _viewModel.LongExposure.BodyOrStationName = entry.Entry.BodyName ?? entry.Entry.StationName;
        _viewModel.LongExposure.RingName = entry.Entry.RingName;
    }

    private void MotionBlurAlphaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        MotionBlurAlphaValueText.Text = e.NewValue.ToString("0.00");
        _viewModel.LongExposure.MotionBlurAlpha = e.NewValue;
    }

    private async void LongExposureGenerateButton_Click(object sender, RoutedEventArgs e)
    {
        var videoPath = _viewModel.LongExposure.VideoFilePath;
        if (videoPath is null)
        {
            _viewModel.LongExposure.ErrorMessage = "Select a video from the library first.";
            return;
        }

        if (_viewModel.LongExposure.SelectedVariants == LongExposureVariants.None)
        {
            _viewModel.LongExposure.ErrorMessage = "Check at least one mode to generate.";
            return;
        }

        _viewModel.LongExposure.ErrorMessage = null;
        _viewModel.LongExposure.Result = null;

        var processingWindow = new LongExposureProcessingWindow(_viewModel.LongExposure.GenerateAsync, videoPath) { Owner = this };
        if (processingWindow.ShowDialog() != true || processingWindow.Result is not { } result)
        {
            if (processingWindow.FailureMessage is not null)
            {
                await new ContentDialog
                {
                    Title = "Long exposure generation failed",
                    Content = processingWindow.FailureMessage,
                    CloseButtonText = "OK",
                }.ShowAsync();
            }
            return;
        }

        _viewModel.LongExposure.Result = result;
        LongExposureVariantList.Items.Clear();
        foreach (var (_, displayName, png) in result.AllVariants)
        {
            LongExposureVariantList.Items.Add(new LongExposureVariantThumbnail(displayName, png, ToBitmapImage(png)));
        }

        if (LongExposureVariantList.Items.Count > 0)
        {
            LongExposureVariantList.SelectedIndex = 0;
        }
    }

    private void LongExposureVariantList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LongExposurePreviewImage.Source = LongExposureVariantList.SelectedItem is LongExposureVariantThumbnail selected
            ? selected.Thumbnail
            : null;
        LongExposureSaveSelectedButton.IsEnabled = LongExposureVariantList.SelectedItem is not null;
    }

    private void LongExposureSaveSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (LongExposureVariantList.SelectedItem is not LongExposureVariantThumbnail selected)
        {
            return;
        }

        var directory = LongExposureFileNamer.SuggestDirectory(LongExposureOutputRoot, ResolveLongExposureSystemName());
        Directory.CreateDirectory(directory);
        var suggestedPath = BuildLongExposureFilePath(selected.DisplayName, directory);

        var dialog = new SaveFileDialog
        {
            Title = "Save Long Exposure Image",
            InitialDirectory = directory,
            FileName = Path.GetFileName(suggestedPath),
            Filter = "PNG Image (*.png)|*.png",
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            File.WriteAllBytes(dialog.FileName, selected.Png);
            UpdateLongExposureOutputDirectoryIfChanged(Path.GetDirectoryName(dialog.FileName), directory);
        }
    }

    private void LongExposureSaveAllButton_Click(object sender, RoutedEventArgs e)
    {
        var directory = LongExposureFileNamer.SuggestDirectory(LongExposureOutputRoot, ResolveLongExposureSystemName());

        var folderDialog = new OpenFolderDialog
        {
            Title = "Choose a folder to save all variations",
            InitialDirectory = Directory.Exists(directory) ? directory : LongExposureOutputRoot,
        };

        if (folderDialog.ShowDialog(this) != true)
        {
            return;
        }

        var targetDirectory = folderDialog.FolderName;
        Directory.CreateDirectory(targetDirectory);

        // Same auto-incrementing scheme as video renaming - each variant just claims the next
        // free "_vN" suffix if its name is already taken, rather than prompting to overwrite.
        var items = LongExposureVariantList.Items.Cast<LongExposureVariantThumbnail>().ToList();
        foreach (var item in items)
        {
            var path = BuildLongExposureFilePath(item.DisplayName, targetDirectory);
            File.WriteAllBytes(path, item.Png);
        }

        UpdateLongExposureOutputDirectoryIfChanged(targetDirectory, directory);
        MessageBox.Show(this, $"Saved {items.Count} images to {targetDirectory}.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Builds a save path using the same naming convention as renaming the source video
    /// (see <see cref="VideoFileNamer"/>: sanitized Ring &gt; Body &gt; System name, with an
    /// auto-incrementing "_vN" suffix if that exact name is already taken) - just with the variant
    /// appended so multiple saved images stay distinguishable, and a .png extension instead of the
    /// video's own.</summary>
    private string BuildLongExposureFilePath(string variantDisplayName, string targetDirectory)
    {
        var videoPath = _viewModel.LongExposure.VideoFilePath ?? string.Empty;
        var suggestedName = $"{ResolveLongExposureBaseName()} ({variantDisplayName})";
        return VideoFileNamer.GetNextAvailableFileName(Path.ChangeExtension(videoPath, ".png"), suggestedName, targetDirectory);
    }

    /// <summary>Remembers <paramref name="chosenDirectory"/> as the new default for future Long
    /// Exposure saves, but only if it actually differs from what was already suggested -
    /// accepting the suggested folder as-is shouldn't "lock in" a nested system subfolder as the
    /// new root the next time a (possibly different) system's images are saved.</summary>
    private void UpdateLongExposureOutputDirectoryIfChanged(string? chosenDirectory, string suggestedDirectory)
    {
        if (chosenDirectory is not null && !string.Equals(chosenDirectory, suggestedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _viewModel.LongExposureOutputDirectory = chosenDirectory;
        }
    }

    private string LongExposureVideoFileNameFallback =>
        _viewModel.LongExposure.VideoFilePath is { } path ? Path.GetFileNameWithoutExtension(path) : "Unknown";

    private string ResolveLongExposureSystemName() => _viewModel.LongExposure.SystemName ?? LongExposureVideoFileNameFallback;

    private string ResolveLongExposureBaseName() => _viewModel.LongExposure.SuggestedFileBaseName ?? LongExposureVideoFileNameFallback;

    private static BitmapImage ToBitmapImage(byte[] pngBytes)
    {
        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(pngBytes);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private sealed class LongExposureVariantThumbnail
    {
        public LongExposureVariantThumbnail(string displayName, byte[] png, BitmapImage thumbnail)
        {
            DisplayName = displayName;
            Png = png;
            Thumbnail = thumbnail;
        }

        public string DisplayName { get; }
        public byte[] Png { get; }
        public BitmapImage Thumbnail { get; }
    }

    /// <summary>Ring Rotation's system/ring auto-population from a selected library video is
    /// already handled by <see cref="MainViewModel.OnLibraryEntrySelected"/> (private, wired
    /// internally) - this only needs to refresh the preview frame shown alongside it.</summary>
    private async void OnLibraryEntrySelectedForRingRotation(VideoLibraryEntryViewModel entry)
    {
        RingRotationPreviewImage.Source = null;
        if (entry.IsFileMissing)
        {
            return;
        }

        byte[]? previewFrame;
        try
        {
            previewFrame = await VideoFrameReader.ReadRepresentativeFrameAsync(entry.FilePath);
        }
        catch
        {
            previewFrame = null;
        }

        // The user may have selected a different video while this was loading.
        if (_viewModel.ActiveLibraryVideo == entry && previewFrame is not null)
        {
            RingRotationPreviewImage.Source = ToBitmapImage(previewFrame);
        }
    }

    /// <summary>Station Rotation still needs a specific station's full physical data (body
    /// radius/rotation/inclination, ...) for the CSV, which only Spansh has - so selecting a
    /// library video auto-resolves its tagged system into the grid (preferring the tagged
    /// station, if any) rather than requiring the user to search and pick it every time.</summary>
    private async void OnLibraryEntrySelectedForStationRotation(VideoLibraryEntryViewModel entry)
    {
        if (entry.IsFileMissing)
        {
            return;
        }

        _viewModel.Stations.ErrorMessage = null;
        _viewModel.Stations.VideoFilePath = entry.FilePath;
        StationPreviewImage.Source = null;

        if (entry.Entry.SystemId64 is long id64)
        {
            var syntheticSystem = new Core.Spansh.Models.SpanshSearchSystem
            {
                Id64 = id64,
                Name = entry.Entry.SystemName ?? string.Empty,
                X = entry.Entry.SystemX ?? 0,
                Y = entry.Entry.SystemY ?? 0,
                Z = entry.Entry.SystemZ ?? 0,
            };
            await _viewModel.Stations.SubmitAsync(syntheticSystem, preferredStationName: entry.Entry.StationName);
        }
else
{
    _viewModel.Stations.Stations.Clear();
    _viewModel.Stations.SelectedStation = null;
    _viewModel.Stations.ResolvedSystemName = null;

    _viewModel.Stations.ErrorMessage =
        "This video isn't tagged with a system - search for one above, then pick its station below.";
}

        byte[]? previewFrame;
        try
        {
            previewFrame = await VideoFrameReader.ReadRepresentativeFrameAsync(entry.FilePath);
        }
        catch
        {
            previewFrame = null;
        }

        // The user may have selected a different video while this was loading.
        if (_viewModel.Stations.VideoFilePath == entry.FilePath && previewFrame is not null)
        {
            StationPreviewImage.Source = ToBitmapImage(previewFrame);
        }
    }

    /// <summary>Slit Scan is a general effect with no system/body context of its own, so unlike
    /// Ring Rotation's library hookup (which also resolves and searches the system), selecting any
    /// library video just loads it here directly - the same way manually uploading one does.</summary>
    private async void OnLibraryEntrySelectedForSlitScan(VideoLibraryEntryViewModel entry)
    {
        if (entry.IsFileMissing)
        {
            return;
        }

        await SetSlitScanVideoAsync(entry.FilePath);
    }

    private async Task SetSlitScanVideoAsync(string videoPath)
    {
        _viewModel.SlitScan.ErrorMessage = null;
        _viewModel.SlitScan.VideoFilePath = videoPath;
        SlitScanControls.SetPreviewFrame(null);

        byte[]? previewFrame;
        try
        {
            previewFrame = await _viewModel.SlitScan.LoadPreviewFrameAsync(CancellationToken.None);
        }
        catch
        {
            previewFrame = null;
        }

        // The user may have uploaded a different video (or left the tab) while this was loading.
        if (_viewModel.SlitScan.VideoFilePath == videoPath)
        {
            SlitScanControls.SetPreviewFrame(previewFrame);
        }
    }

    private async void SlitScanGenerateButton_Click(object sender, RoutedEventArgs e)
    {
        var videoPath = _viewModel.SlitScan.VideoFilePath;
        if (videoPath is null)
        {
            _viewModel.SlitScan.ErrorMessage = "Select a video from the library first.";
            return;
        }

        var parameters = SlitScanControls.BuildParameters();
        if (parameters is null)
        {
            _viewModel.SlitScan.ErrorMessage = SlitScanControls.LastValidationError;
            return;
        }

        _viewModel.SlitScan.ErrorMessage = null;

        var processingWindow = new SlitScanProcessingWindow(
            (path, progress, ct) => _viewModel.SlitScan.GenerateAsync(path, parameters, progress, ct),
            videoPath)
        { Owner = this };
        if (processingWindow.ShowDialog() != true || processingWindow.Result is not { } result)
        {
            if (processingWindow.FailureMessage is not null)
            {
                await new ContentDialog
                {
                    Title = "Slit scan generation failed",
                    Content = processingWindow.FailureMessage,
                    CloseButtonText = "OK",
                }.ShowAsync();
            }
            return;
        }

        var resultsWindow = new LongExposureResultsWindow(
            new[] { ("Slit Scan", result.ImagePng) },
            Path.GetFileNameWithoutExtension(videoPath),
            null)
        { Owner = this };
        resultsWindow.ShowDialog();
    }

    private async void SlitScanResetButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmResult = await new ContentDialog
        {
            Title = "Reset Slit Scan controls?",
            Content = "All Geometry, Motion, Sampling, Compositing, and Output settings will be restored to their defaults. This can't be undone.",
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
        }.ShowAsync();

        if (confirmResult != ContentDialogResult.Primary)
        {
            return;
        }

        SlitScanControls.ResetToDefaults();
    }
}
