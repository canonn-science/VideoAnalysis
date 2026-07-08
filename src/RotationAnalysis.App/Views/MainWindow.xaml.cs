using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using ModernWpf.Controls;
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

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.VideoSelectionRequested += OnVideoSelectionRequested;
        _viewModel.Measurements.SubmissionFailed += OnCanonnSubmissionFailed;
        Closed += (_, _) =>
        {
            _viewModel.Dispose();
            _updateChecker.Dispose();
        };
        Loaded += async (_, _) => await CheckForUpdatesAsync();
        VersionText.Text = $"Version v{GetCurrentVersion().ToString(3)}";
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

        var processingWindow = new VideoProcessingWindow(_viewModel, videoPath, row.Ring.EstimatedPeriodSeconds, row.Ring.RingName) { Owner = this };
        var completed = processingWindow.ShowDialog();

        if (completed == true && processingWindow.Result is not null)
        {
            var finalVideoPath = processingWindow.FinalVideoPath;
            var resultsWindow = new ResultsWindow(_viewModel, row, processingWindow.Result, finalVideoPath) { Owner = this };
            if (resultsWindow.ShowDialog() == true)
            {
                _viewModel.SaveMeasurement(row, processingWindow.Result, finalVideoPath, resultsWindow.SubmittedToCanonn);
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
}
