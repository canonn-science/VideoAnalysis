using RotationAnalysis.App.Infrastructure;
using RotationAnalysis.Core.Domain;

namespace RotationAnalysis.App.ViewModels;

public sealed class SlitScanRowViewModel
{
    public LongExposureTargetInfo Target { get; }

    public SlitScanRowViewModel(LongExposureTargetInfo target, Action<SlitScanRowViewModel> onSelectVideo)
    {
        Target = target;
        SelectVideoCommand = new RelayCommand(() => onSelectVideo(this));
    }

    public string SystemName => Target.SystemName;
    public string ObjectName => Target.ObjectName;
    public string Kind => Target.DisplayKind;
    public string ObjectType => Target.ObjectType ?? "N/A";

    public RelayCommand SelectVideoCommand { get; }
}
