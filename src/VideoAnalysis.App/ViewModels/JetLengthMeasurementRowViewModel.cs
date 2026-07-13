using VideoAnalysis.App.Infrastructure;
using VideoAnalysis.Core.Storage;

namespace VideoAnalysis.App.ViewModels;

/// <summary>Formats one logged <see cref="JetLengthRecord"/> for display, mirroring how
/// <see cref="MeasurementRowViewModel"/> formats a ring history row - including the same
/// submitted/submitting/error/selectable-for-batch state, now that Jet Cone has a Canonn
/// submission path.</summary>
public sealed class JetLengthMeasurementRowViewModel : ObservableObject
{
    private bool _isSubmitted;
    private bool _isSubmitting;
    private string? _submitError;
    private bool _isSelected;

    public JetLengthMeasurementRowViewModel(JetLengthRecord record, Func<JetLengthMeasurementRowViewModel, CancellationToken, Task> onSendToCanonn)
    {
        Record = record;
        _isSubmitted = record.Submitted;
        SendToCanonnCommand = new RelayCommand(async () => await onSendToCanonn(this, CancellationToken.None));
    }

    public JetLengthRecord Record { get; }

    public string SystemName => Record.SystemName;
    public string BodyName => Record.BodyName;
    public string DistanceDisplay => $"{Record.Distance:0.##} Ls";
    public string RotationalPeriodDisplay => Record.RotationalPeriod is double days ? $"{days * 86_400.0:N1} s" : "N/A";
    public string SpectralClassDisplay => Record.SpectralClass ?? "N/A";

    /// <summary>Bound to the history grid's selection checkbox, for batching multiple rows into
    /// one "Send Selected to Canonn" request.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

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

    public RelayCommand SendToCanonnCommand { get; }
}
