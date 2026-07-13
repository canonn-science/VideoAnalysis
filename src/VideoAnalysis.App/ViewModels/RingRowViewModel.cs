using VideoAnalysis.App.Infrastructure;
using VideoAnalysis.Core.Domain;

namespace VideoAnalysis.App.ViewModels;

/// <summary>A selectable ring/belt in the resolved system - just an identity for the dropdown; the
/// video comes from the shared library selection instead of a per-row action.</summary>
public sealed class RingRowViewModel
{
    public RingInfo Ring { get; }

    public RingRowViewModel(RingInfo ring)
    {
        Ring = ring;
    }

    public string BodyName => Ring.BodyName;
    public string? BodyType => Ring.BodyType;
    public string RingName => Ring.RingName;
    public string Kind => Ring.DisplayKind;
    public string InnerRadiusDisplay => $"{Ring.InnerRadiusMeters / 1000.0:N0} km";
    public string OuterRadiusDisplay => $"{Ring.OuterRadiusMeters / 1000.0:N0} km";
    public string WidthDisplay => $"{Ring.WidthMeters / 1000.0:N0} km";
    public string EstimatedPeriodDisplay => DurationFormat.Seconds(Ring.EstimatedPeriodSeconds);
    public string SuggestedDurationDisplay => DurationFormat.Minutes(Ring.SuggestedVideoDurationMinutes);

    /// <summary>The Body dropdown's item label/key, e.g. "Merope A (K (Yellow-Orange) Star)" -
    /// used as the actual identity for <see cref="MainViewModel.SelectedBodyName"/> so bodies of
    /// the same name but different types (rare, but possible across a multi-star system) stay
    /// distinguishable.</summary>
    public string BodyDisplay => string.IsNullOrWhiteSpace(BodyType) ? BodyName : $"{BodyName} ({BodyType})";

    /// <summary>The dropdown's item label.</summary>
    public string Display => $"{RingName} ({Ring.MaterialType})";

    /// <summary>A one-line summary of everything the removed grid columns used to show, for the
    /// selected row only.</summary>
    public string DetailsSummary =>
        $"{Kind} · Body {BodyName} · Inner {InnerRadiusDisplay} · Outer {OuterRadiusDisplay} · Width {WidthDisplay} · Est. Rotation {EstimatedPeriodDisplay} · Suggested {SuggestedDurationDisplay}";
}
