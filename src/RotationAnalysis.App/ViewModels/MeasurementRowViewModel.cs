using RotationAnalysis.App.Infrastructure;
using RotationAnalysis.Core.Storage;

namespace RotationAnalysis.App.ViewModels;

/// <summary>Formats one logged <see cref="MeasurementRecord"/> for display, mirroring how
/// <see cref="RingRowViewModel"/> formats a ring row rather than showing raw field values.</summary>
public sealed class MeasurementRowViewModel : ObservableObject
{
    private bool _isSubmitted;
    private bool _isSubmitting;
    private string? _submitError;

    public MeasurementRowViewModel(MeasurementRecord record, Func<MeasurementRowViewModel, CancellationToken, Task> onSendToCanonn)
    {
        Record = record;
        _isSubmitted = record.Submitted;
        SendToCanonnCommand = new RelayCommand(async () => await onSendToCanonn(this, CancellationToken.None));
    }

    public MeasurementRecord Record { get; }

    public bool IsSubmitted
    {
        get => _isSubmitted;
        set
        {
            if (SetField(ref _isSubmitted, value))
            {
                Record.Submitted = value;
                OnPropertyChanged(nameof(CanSend));
            }
        }
    }

    public bool IsSubmitting
    {
        get => _isSubmitting;
        set
        {
            if (SetField(ref _isSubmitting, value))
            {
                OnPropertyChanged(nameof(CanSend));
            }
        }
    }

    public string? SubmitError
    {
        get => _submitError;
        set
        {
            if (SetField(ref _submitError, value))
            {
                OnPropertyChanged(nameof(SendTooltip));
            }
        }
    }

    public bool CanSend => !IsSubmitted && !IsSubmitting;

    public string SendTooltip => SubmitError ?? "Send to Canonn";

    public string TimestampDisplay => Record.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string SystemName => Record.SystemName;
    public string RingName => Record.RingName;
    public string InnerRadiusDisplay => (Record.InnerRadius / 1000.0).ToString();
    public string OuterRadiusDisplay => (Record.OuterRadius / 1000.0).ToString();
    public string WidthDisplay => (Record.Width / 1000.0).ToString();
    public string EstimatedRotationDisplay => FormatSeconds(Record.EstimatedRotationSeconds);
    public string ObservedRotationDisplay => FormatSeconds(Record.ObservedRotationSeconds);
    public string VideoFilename => Record.VideoFilename;

    public RelayCommand SendToCanonnCommand { get; }

    private static string FormatSeconds(double seconds) =>
        double.IsNaN(seconds) || double.IsInfinity(seconds) ? "N/A" : seconds.ToString();
}
