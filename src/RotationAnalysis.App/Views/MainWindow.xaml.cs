using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using ModernWpf.Controls;
using RotationAnalysis.App.Infrastructure;
using RotationAnalysis.App.ViewModels;
using RotationAnalysis.Core.Diagnostics;
using RotationAnalysis.Core.Updates;

namespace RotationAnalysis.App.Views;

public partial class MainWindow : Window
{
    private const string UpdateRepoOwner = "canonn-science";
    private const string UpdateRepoName = "RotationAnalysis";

    private readonly MainViewModel _viewModel = new();
    private readonly UpdateChecker _updateChecker = new();
    private CancellationTokenSource? _searchDebounceCts;
    private CancellationTokenSource? _stationSearchDebounceCts;
    private CancellationTokenSource? _jetConeSearchDebounceCts;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.VideoSelectionRequested += OnVideoSelectionRequested;
        _viewModel.Measurements.SubmissionFailed += OnCanonnSubmissionFailed;
        _viewModel.Stations.VideoSelectionRequested += OnStationVideoSelectionRequested;
        _viewModel.JetCone.VideoSelectionRequested += OnJetConeVideoSelectionRequested;
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
            Content = $"Rotation Analysis Lab {update.Version} is available. Download and install it now?",
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

    private async void CommanderNameButton_Click(object sender, RoutedEventArgs e)
    {
        var input = new TextBox { Text = _viewModel.CommanderName, MinWidth = 260 };
        var dialog = new ContentDialog
        {
            Title = "Commander name",
            Content = input,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        input.Loaded += (_, _) => input.SelectAll();

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var name = input.Text.Trim();
            if (name.Length > 0)
            {
                _viewModel.CommanderName = name;
            }
        }
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

    private async void OnVideoSelectionRequested(RingRowViewModel row)
    {
        var promptWindow = new VideoUploadPromptWindow { Owner = this };
        if (promptWindow.ShowDialog() != true || promptWindow.SelectedFilePath is not string videoPath)
        {
            return;
        }

        // Kick this off now rather than waiting for VideoProcessingWindow's Loaded event, so the
        // shell round-trip overlaps window construction/layout instead of starting after it.
        var quickMetadataTask = Task.Run(() => QuickVideoMetadataReader.Read(videoPath));

        var processingWindow = new VideoProcessingWindow(_viewModel.AnalyzeVideoAsync, videoPath, row.Ring.EstimatedPeriodSeconds, quickMetadataTask, row.Ring.RingName) { Owner = this };
        var completed = processingWindow.ShowDialog();

        if (completed == true && processingWindow.Result is not null)
        {
            var result = processingWindow.Result;
            var finalVideoPath = processingWindow.FinalVideoPath;
            var resultsWindow = new ResultsWindow(
                row.Ring.SystemName, row.Ring.BodyName, "Ring:", row.Ring.RingName,
                row.Ring.EstimatedPeriodSeconds, result, finalVideoPath,
                ct => _viewModel.SubmitMeasurementToCanonnAsync(row, result, ct))
            { Owner = this };
            if (resultsWindow.ShowDialog() == true)
            {
                _viewModel.SaveMeasurement(row, result, finalVideoPath, resultsWindow.SubmittedToCanonn);
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

    private async void OnStationVideoSelectionRequested(StationRowViewModel row)
    {
        var promptWindow = new VideoUploadPromptWindow { Owner = this };
        if (promptWindow.ShowDialog() != true || promptWindow.SelectedFilePath is not string videoPath)
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

    private void DeleteClaudeApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
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

    private async void OnJetConeVideoSelectionRequested(JetConeRowViewModel row)
    {
        var promptWindow = new VideoUploadPromptWindow { Owner = this };
        if (promptWindow.ShowDialog() != true || promptWindow.SelectedFilePath is not string videoPath)
        {
            return;
        }

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
            await new ContentDialog
            {
                Title = "Warning overlay not found",
                Content = "Couldn't find the \"FSD OPERATING / BEYOND SAFETY LIMITS\" warning in this recording. Make sure it shows the approach all the way through the warning appearing.",
                CloseButtonText = "OK",
            }.ShowAsync();
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

        var reviewWindow = new JetConeReviewWindow(result.ReticleCropPng, result.BottomLeftCropPng, prefill, sourceLabel) { Owner = this };
        if (reviewWindow.ShowDialog() != true)
        {
            return;
        }

        if (reviewWindow.UserCorrectedValue && !_viewModel.JetCone.HasClaudeApiKey)
        {
            await OfferClaudeApiKeySetupAsync();
        }

        _viewModel.JetCone.SaveMeasurement(row, reviewWindow.DistanceLs);
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
                _viewModel.JetCone.SetClaudeApiKey(key);
                UpdateClaudeApiKeyStatusText();
            }
        }
    }
}
