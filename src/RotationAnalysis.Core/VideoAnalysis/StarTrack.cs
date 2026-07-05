namespace RotationAnalysis.Core.VideoAnalysis;

/// <summary>The pixel trajectory of one tracked star across however many frames it survived in.</summary>
public sealed class StarTrack
{
    public required int Id { get; init; }
    public List<int> FrameIndices { get; } = new();
    public List<float> Xs { get; } = new();
    public List<float> Ys { get; } = new();
}
