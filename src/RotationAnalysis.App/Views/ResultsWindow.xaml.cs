using System.Windows;
using RotationAnalysis.App.Infrastructure;
using RotationAnalysis.App.ViewModels;
using RotationAnalysis.Core.VideoAnalysis;

namespace RotationAnalysis.App.Views;

/// <summary>
/// Shows the analysis result for review. Nothing is saved to the CSV until the user explicitly
/// clicks "Save to History" - DialogResult reflects that choice so the caller knows whether to
/// persist the measurement.
/// </summary>
public partial class ResultsWindow : Window
{
    public ResultsWindow(RingRowViewModel row, HorizontalVideoAnalysisResult result, string videoPath)
    {
        InitializeComponent();

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

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
