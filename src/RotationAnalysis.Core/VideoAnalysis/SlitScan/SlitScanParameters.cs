namespace RotationAnalysis.Core.VideoAnalysis.SlitScan;

public enum SlitScanDirection
{
    Forward,
    Reverse,
}

/// <summary>How overlapping slit placements combine, when <see cref="SlitScanParameters.ScanSpeedPixelsPerFrame"/>
/// is smaller than <see cref="SlitScanParameters.SlitWidthPixels"/> (each slit then overlaps the
/// next). With no overlap (speed >= width), the blend mode has no visible effect.</summary>
public enum SlitScanBlendMode
{
    /// <summary>Later slits overwrite earlier ones in the overlap region.</summary>
    Normal,

    /// <summary>Per-pixel brightest-of-the-overlap wins.</summary>
    Lighten,

    /// <summary>Per-pixel mean of every slit that covers that column.</summary>
    Average,
}

/// <summary>A quick preset that pre-fills <see cref="SlitScanParameters.SlitAngleDegrees"/> and
/// <see cref="SlitScanParameters.ScanDirection"/> for a guessed subject-motion direction - a
/// convenience default, not a separate algorithm input (per spec, "if applicable").</summary>
public enum MotionDirectionHint
{
    None,
    LeftToRight,
    RightToLeft,
    TopToBottom,
    BottomToTop,
}

public sealed class SlitScanParameters
{
    /// <summary>Degrees. 90 = a vertical slit (the classic left-right scan); 0 = a horizontal
    /// slit (top-bottom scan). Any angle in between rotates the sampled line accordingly.</summary>
    public double SlitAngleDegrees { get; init; } = 90.0;

    public int SlitWidthPixels { get; init; } = 2;

    /// <summary>0.0-1.0, the slit's position across the frame (after rotating to the slit's own
    /// axis) - 0.5 samples the center.</summary>
    public double SlitPositionFraction { get; init; } = 0.5;

    public SlitScanDirection ScanDirection { get; init; } = SlitScanDirection.Forward;

    /// <summary>Output pixels advanced per sampled frame. Equal to <see cref="SlitWidthPixels"/>
    /// for edge-to-edge coverage with no overlap or gaps; smaller creates overlap (see
    /// <see cref="SlitScanBlendMode"/>), larger leaves gaps.</summary>
    public int ScanSpeedPixelsPerFrame { get; init; } = 2;

    /// <summary>Only every Nth input frame is sampled - 1 samples every frame.</summary>
    public int FrameSamplingInterval { get; init; } = 1;

    /// <summary>Caps the output image's width, downscaling proportionally if the raw composited
    /// width exceeds it. Null leaves the output at its native (frame-count-driven) width.</summary>
    public int? MaxOutputWidth { get; init; }

    public SlitScanBlendMode BlendMode { get; init; } = SlitScanBlendMode.Normal;
}

public sealed class SlitScanResult
{
    public required byte[] ImagePng { get; init; }
    public required int FramesSampled { get; init; }
}
