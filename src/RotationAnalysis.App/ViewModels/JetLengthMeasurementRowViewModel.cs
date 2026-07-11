using RotationAnalysis.Core.Storage;

namespace RotationAnalysis.App.ViewModels;

/// <summary>Formats one logged <see cref="JetLengthRecord"/> for display, mirroring how
/// <see cref="StationMeasurementRowViewModel"/> formats a station history row.</summary>
public sealed class JetLengthMeasurementRowViewModel
{
    public JetLengthMeasurementRowViewModel(JetLengthRecord record)
    {
        Record = record;
    }

    public JetLengthRecord Record { get; }

    public string SystemName => Record.SystemName;
    public string BodyName => Record.BodyName;
    public string DistanceDisplay => $"{Record.Distance:0.##} Ls";
    public string RotationalPeriodDisplay => Record.RotationalPeriod is double days ? $"{days * 86_400.0:N1} s" : "N/A";
    public string SpectralClassDisplay => Record.SpectralClass ?? "N/A";
}
