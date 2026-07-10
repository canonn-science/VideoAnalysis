using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
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

        if (result.MeasuredPeriodSeconds is double measured)
        {
            MeasuredText.Text = $"{DurationFormat.SecondsWithRaw(measured)} ± {DurationFormat.Seconds(result.MeasuredPeriodErrSeconds)} ({result.NReferenceSamples} samples)";
            RateVsMeasuredText.Text = $"{result.RateVsMeasuredPctDiff:+0.0;-0.0}%";
            ConsistencyWarningText.Visibility = result.ConsistencyWarning ? Visibility.Visible : Visibility.Collapsed;
            AlignmentStatusText.Visibility = Visibility.Collapsed;
        }
        else
        {
            MeasuredLabelText.Visibility = Visibility.Collapsed;
            MeasuredText.Visibility = Visibility.Collapsed;
            RateVsMeasuredLabelText.Visibility = Visibility.Collapsed;
            RateVsMeasuredText.Visibility = Visibility.Collapsed;
            ConsistencyWarningText.Visibility = Visibility.Collapsed;

            AlignmentStatusText.Text = result.AlignmentAttempted
                ? $"Full-rotation measurement attempted but didn't converge: {result.AlignmentFailureReason}"
                : "Full-rotation measurement not attempted (video too short to contain a full rotation).";
            AlignmentStatusText.Visibility = Visibility.Visible;
        }

        PopulateEvidencePanel(result);
    }

    /// <summary>Shows the 2 most confident accepted reference/matched-frame pairs (by combined
    /// correlation peak strength + fit R²) as visual proof of the full-rotation measurement.
    /// Collapses the whole panel if there's nothing to show, and gracefully handles having only
    /// one accepted pair available.</summary>
    private void PopulateEvidencePanel(HorizontalVideoAnalysisResult result)
    {
        var best = result.AlignmentPreviews
            .OrderByDescending(p => p.MeanPeakStrength + p.FitRSquared)
            .Take(2)
            .OrderBy(p => p.ReferenceIndex)
            .ToList();

        if (best.Count == 0)
        {
            EvidencePanel.Visibility = Visibility.Collapsed;
            return;
        }

        EvidencePanel.Visibility = Visibility.Visible;
        PopulateSlot(best[0], Preview1RefCaption, Preview1RefImage, Preview1MatchCaption, Preview1MatchImage);

        bool hasSecond = best.Count > 1;
        Preview2RefCaption.Visibility = hasSecond ? Visibility.Visible : Visibility.Collapsed;
        Preview2RefImage.Visibility = hasSecond ? Visibility.Visible : Visibility.Collapsed;
        Preview2MatchCaption.Visibility = hasSecond ? Visibility.Visible : Visibility.Collapsed;
        Preview2MatchImage.Visibility = hasSecond ? Visibility.Visible : Visibility.Collapsed;
        if (hasSecond)
        {
            PopulateSlot(best[1], Preview2RefCaption, Preview2RefImage, Preview2MatchCaption, Preview2MatchImage);
        }
    }

    private static void PopulateSlot(
        ReferenceMatchPreview preview, System.Windows.Controls.TextBlock refCaption, System.Windows.Controls.Image refImage,
        System.Windows.Controls.TextBlock matchCaption, System.Windows.Controls.Image matchImage)
    {
        refCaption.Text = $"Reference #{preview.ReferenceIndex + 1} — peak {preview.MeanPeakStrength:F2}, R² {preview.FitRSquared:F2} (t={preview.TRefSeconds:F1}s)";
        refImage.Source = DecodeJpeg(preview.ReferenceFrameJpeg);

        matchCaption.Text = $"Matched frame (t={preview.TMatchSeconds:F1}s, one period ≈ {preview.PeriodSampleSeconds:F1}s later)";
        matchImage.Source = DecodeJpeg(preview.MatchedFrameJpeg);
    }

    private static BitmapImage DecodeJpeg(byte[] jpegBytes)
    {
        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(jpegBytes);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
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
