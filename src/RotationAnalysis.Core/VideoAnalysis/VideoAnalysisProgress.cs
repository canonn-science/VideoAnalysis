namespace RotationAnalysis.Core.VideoAnalysis;

public enum VideoAnalysisStage
{
    Opening,
    Tracking,
    FittingCenter,
    SolvingRotation,
    DetectingOnset,
    Done,
}

public sealed record VideoAnalysisProgress(
    VideoAnalysisStage Stage,
    int PercentComplete,
    string Message,
    int FramesProcessed = 0,
    int TotalFrames = 0,
    byte[]? PreviewImageBytes = null);
