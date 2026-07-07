using RotationAnalysis.Core.Storage;

namespace RotationAnalysis.Core.Canonn;

/// <summary>Decides whether a local measurement already exists in Canonn's published dataset.
/// There's no shared submission id to key off, so this matches on the fields that identify a
/// specific reading: who took it, which ring, and the observed period - with tolerances loose
/// enough to absorb km/seconds rounding on either side of the round trip.</summary>
public static class CanonnMatcher
{
    private const double RadiusToleranceKm = 1.0;
    private const double PeriodToleranceSeconds = 5.0;

    public static bool IsSubmitted(MeasurementRecord record, string commanderName, IEnumerable<CanonnSubmittedMeasurement> submitted)
    {
        var innerKm = record.InnerRadius / 1000.0;
        var outerKm = record.OuterRadius / 1000.0;

        foreach (var candidate in submitted)
        {
            if (!string.Equals(candidate.CommanderName, commanderName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!string.Equals(candidate.SystemName, record.SystemName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!string.Equals(candidate.RingName, record.RingName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (Math.Abs(candidate.InnerRadiusKm - innerKm) > RadiusToleranceKm)
            {
                continue;
            }
            if (Math.Abs(candidate.OuterRadiusKm - outerKm) > RadiusToleranceKm)
            {
                continue;
            }
            if (Math.Abs(candidate.ObservedPeriodSeconds - record.ObservedRotationSeconds) > PeriodToleranceSeconds)
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
