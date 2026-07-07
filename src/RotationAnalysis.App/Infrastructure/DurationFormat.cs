namespace RotationAnalysis.App.Infrastructure;

public static class DurationFormat
{
    public static string Seconds(double? totalSeconds)
    {
        if (totalSeconds is not double s || double.IsNaN(s) || double.IsInfinity(s))
        {
            return "N/A";
        }

        var absSeconds = Math.Abs(s);
        if (absSeconds > TimeSpan.MaxValue.TotalSeconds)
        {
            // A handful of dump entries produce an absurdly large Kepler estimate (bad parent
            // mass data); TimeSpan.FromSeconds would overflow and crash the whole app.
            return "N/A";
        }

        var span = TimeSpan.FromSeconds(absSeconds);
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m {span.Seconds}s";
        }
        if (span.TotalMinutes >= 1)
        {
            return $"{span.Minutes}m {span.Seconds}s";
        }
        return $"{span.Seconds}s";
    }

    public static string Minutes(int? minutes) => minutes is int m ? $"{m} min" : "N/A";

    /// <summary>Same as <see cref="Seconds"/>, with the raw value appended in brackets, e.g. "1m 2s (62 seconds)".</summary>
    public static string SecondsWithRaw(double? totalSeconds)
    {
        string formatted = Seconds(totalSeconds);
        if (totalSeconds is not double s || double.IsNaN(s) || double.IsInfinity(s))
        {
            return formatted;
        }
        return $"{formatted} ({s:0} seconds)";
    }
}
