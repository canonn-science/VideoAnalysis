using System.Windows;
using ModernWpf.Controls;
using RotationAnalysis.App.ViewModels;

namespace RotationAnalysis.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private CancellationTokenSource? _searchDebounceCts;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.VideoSelectionRequested += OnVideoSelectionRequested;
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

    private void OnVideoSelectionRequested(RingRowViewModel row)
    {
        var promptWindow = new VideoUploadPromptWindow { Owner = this };
        if (promptWindow.ShowDialog() != true || promptWindow.SelectedFilePath is not string videoPath)
        {
            return;
        }

        var processingWindow = new VideoProcessingWindow(_viewModel, videoPath, row.Ring.EstimatedPeriodSeconds) { Owner = this };
        var completed = processingWindow.ShowDialog();

        if (completed == true && processingWindow.Result is not null)
        {
            var resultsWindow = new ResultsWindow(row, processingWindow.Result, videoPath) { Owner = this };
            if (resultsWindow.ShowDialog() == true)
            {
                _viewModel.SaveMeasurement(row, processingWindow.Result, videoPath);
            }
        }
        else if (processingWindow.FailureMessage is not null)
        {
            MessageBox.Show(this, processingWindow.FailureMessage, "Video analysis failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
