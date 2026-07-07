using System.Windows;
using RotationAnalysis.App.Infrastructure;
using RotationAnalysis.App.ViewModels;
using RotationAnalysis.Core.Diagnostics;
using RotationAnalysis.Core.VideoAnalysis;

namespace RotationAnalysis.App.Views;

/// <summary>
/// Shows the analysis result for review. Nothing is saved to the CSV until the user explicitly
/// clicks "Save to History" - DialogResult reflects that choice so the caller knows whether to
/// persist the measurement. Sending to Canonn is independent of that and can happen either way.
/// </summary>
public partial class ResultsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly RingRowViewModel _row;
    private readonly HorizontalVideoAnalysisResult _result;

    /// <summary>Whether the user successfully sent this measurement to Canonn from this dialog.</summary>
    public bool SubmittedToCanonn { get; private set; }

    public ResultsWindow(MainViewModel viewModel, RingRowViewModel row, HorizontalVideoAnalysisResult result, string videoPath)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _row = row;
        _result = result;

        SystemNameText.Text = row.Ring.SystemName;
        BodyNameText.Text = row.Ring.BodyName;
        RingNameText.Text = row.Ring.RingName;

        EstimatedText.Text = DurationFormat.SecondsWithRaw(row.Ring.EstimatedPeriodSeconds);
        ObservedText.Text = DurationFormat.SecondsWithRaw(result.ObservedPeriodSeconds);

        if (row.Ring.EstimatedPeriodSeconds is double estimated && estimated > 0)
        {
            double diffPercent = (result.ObservedPeriodSeconds - estimated) / estimated * 100.0;
            DifferenceText.Text = $"{diffPercent:+0.0;-0.0}%";
        }
        else
        {
            DifferenceText.Text = "N/A";
        }

        ConfidenceText.Text = $"{result.ConfidencePercent:F0}%";
        RollText.Text = $"{result.MedianRollDegrees:+0.0;-0.0}° from level";
        TrackingText.Text = $"{result.ChunksUsed} of {result.ChunksAvailable} recording segments";
    }

    private async void SendToCanonnButton_Click(object sender, RoutedEventArgs e)
    {
        SendToCanonnButton.IsEnabled = false;
        SubmitErrorText.Text = null;
        try
        {
            await _viewModel.SubmitMeasurementToCanonnAsync(_row, _result);
            SubmittedToCanonn = true;
            SendToCanonnButton.Content = "Sent to Canonn ✓";
        }
        catch (Exception ex)
        {
            AppLog.LogError("SubmitToCanonn", ex);
            SubmitErrorText.Text = $"Send failed: {ex.Message}";
            SendToCanonnButton.IsEnabled = true;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
