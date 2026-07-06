using RotationAnalysis.Core.Storage;

namespace RotationAnalysis.App.ViewModels;

/// <summary>Formats one logged <see cref="MeasurementRecord"/> for display, mirroring how
/// <see cref="RingRowViewModel"/> formats a ring row rather than showing raw field values.</summary>
public sealed class MeasurementRowViewModel
{
    public MeasurementRowViewModel(MeasurementRecord record)
    {
        Record = record;
    }

    public MeasurementRecord Record { get; }

    public string TimestampDisplay => Record.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string SystemName => Record.SystemName;
    public string RingName => Record.RingName;
    public string InnerRadiusDisplay => (Record.InnerRadius / 1000.0).ToString();
    public string OuterRadiusDisplay => (Record.OuterRadius / 1000.0).ToString();
    public string WidthDisplay => (Record.Width / 1000.0).ToString();
    public string EstimatedRotationDisplay => FormatSeconds(Record.EstimatedRotationSeconds);
    public string ObservedRotationDisplay => FormatSeconds(Record.ObservedRotationSeconds);
    public string VideoFilename => Record.VideoFilename;

    private static string FormatSeconds(double seconds) =>
        double.IsNaN(seconds) || double.IsInfinity(seconds) ? "N/A" : seconds.ToString();
}
