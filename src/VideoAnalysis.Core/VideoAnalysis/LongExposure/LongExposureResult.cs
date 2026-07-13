namespace VideoAnalysis.Core.VideoAnalysis.LongExposure;

public enum LongExposureVariant
{
    Average,
    Maximum,
    Minimum,
    MaxMinusMin,
    MotionVariance,
    MotionBlur,
}

/// <summary>Which variants <see cref="LongExposureProcessor.GenerateAsync"/> should actually
/// compute and emit - unchecked ones are skipped entirely (not just hidden), so deselecting
/// variants you don't want speeds up generation instead of just tidying up the result.</summary>
[Flags]
public enum LongExposureVariants
{
    None = 0,
    Average = 1 << 0,
    Maximum = 1 << 1,
    Minimum = 1 << 2,
    MaxMinusMin = 1 << 3,
    MotionVariance = 1 << 4,
    MotionBlur = 1 << 5,
    All = Average | Maximum | Minimum | MaxMinusMin | MotionVariance | MotionBlur,
}

public sealed class LongExposureResult
{
    /// <summary>Null for any variant that wasn't in the <see cref="LongExposureVariants"/> passed
    /// to <see cref="LongExposureProcessor.GenerateAsync"/> - see <see cref="AllVariants"/> for the
    /// filtered, display-ready list.</summary>
    public byte[]? AveragePng { get; init; }
    public byte[]? MaximumPng { get; init; }
    public byte[]? MinimumPng { get; init; }
    public byte[]? MaxMinusMinPng { get; init; }
    public byte[]? MotionVariancePng { get; init; }
    public byte[]? MotionBlurPng { get; init; }
    public required int FrameCount { get; init; }

    /// <summary>Every variant that was actually generated, paired with a display name, in the
    /// order the results view should show them.</summary>
    public IReadOnlyList<(LongExposureVariant Variant, string DisplayName, byte[] Png)> AllVariants
    {
        get
        {
            var all = new (LongExposureVariant, string, byte[]?)[]
            {
                (LongExposureVariant.Average, "Average", AveragePng),
                (LongExposureVariant.Maximum, "Maximum", MaximumPng),
                (LongExposureVariant.Minimum, "Minimum", MinimumPng),
                (LongExposureVariant.MaxMinusMin, "Max Minus Min", MaxMinusMinPng),
                (LongExposureVariant.MotionVariance, "Motion Variance", MotionVariancePng),
                (LongExposureVariant.MotionBlur, "Motion Blur", MotionBlurPng),
            };

            var results = new List<(LongExposureVariant, string, byte[])>();
            foreach (var (variant, displayName, png) in all)
            {
                if (png is not null)
                {
                    results.Add((variant, displayName, png));
                }
            }

            return results;
        }
    }
}
