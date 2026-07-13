using VideoAnalysis.App.Infrastructure;
using VideoAnalysis.Core.Domain;

namespace VideoAnalysis.App.ViewModels;

/// <summary>A selectable station/installation/beacon in the resolved system - just an identity for
/// the dropdown; the video comes from the shared library selection instead of a per-row action.</summary>
public sealed class StationRowViewModel
{
    public StationInfo Station { get; }

    public StationRowViewModel(StationInfo station)
    {
        Station = station;
    }

    public string SystemName => Station.SystemName;
    public string StationName => Station.StationName;
    public string Kind => Station.DisplayKind;
    public string BodyNameDisplay => Station.BodyName ?? "N/A";
    public string BodyRadiusDisplay => Station.BodyRadiusKm is double km ? $"{km:N0} km" : "N/A";
    public string BodyRotationalPeriodDisplay => Station.BodyRotationalPeriodDays is double d ? $"{d:N2} days" : "N/A";
    public string BodyInclinationDisplay => Station.BodyInclinationDegrees is double deg ? $"{deg:N1}°" : "N/A";
    public string EstimatedRotationDisplay => DurationFormat.Seconds(Station.EstimatedRotationSeconds);
    public string SuggestedDurationDisplay => DurationFormat.Minutes(Station.SuggestedVideoDurationMinutes);

    /// <summary>The dropdown's item label.</summary>
    public string Display => $"{StationName} ({Kind})";

    /// <summary>A one-line summary of everything the removed grid columns used to show, for the
    /// selected row only.</summary>
    public string DetailsSummary =>
        $"Body {BodyNameDisplay} · Body Radius {BodyRadiusDisplay} · Rotational Period {BodyRotationalPeriodDisplay} · Inclination {BodyInclinationDisplay} · Est. Rotation {EstimatedRotationDisplay} · Suggested {SuggestedDurationDisplay}";
}
