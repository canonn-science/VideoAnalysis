using RotationAnalysis.App.Infrastructure;
using RotationAnalysis.Core.Domain;

namespace RotationAnalysis.App.ViewModels;

public sealed class RingRowViewModel
{
    public RingInfo Ring { get; }

    public RingRowViewModel(RingInfo ring, Action<RingRowViewModel> onSelectVideo)
    {
        Ring = ring;
        SelectVideoCommand = new RelayCommand(() => onSelectVideo(this));
    }

    public string BodyName => Ring.BodyName;
    public string RingName => Ring.RingName;
    public string Kind => Ring.DisplayKind;
    public string InnerRadiusDisplay => $"{Ring.InnerRadiusMeters / 1000.0:N0} km";
    public string OuterRadiusDisplay => $"{Ring.OuterRadiusMeters / 1000.0:N0} km";
    public string WidthDisplay => $"{Ring.WidthMeters / 1000.0:N0} km";
    public string EstimatedPeriodDisplay => DurationFormat.Seconds(Ring.EstimatedPeriodSeconds);
    public string SuggestedDurationDisplay => DurationFormat.Minutes(Ring.SuggestedVideoDurationMinutes);

    public RelayCommand SelectVideoCommand { get; }
}
