using OpenCvSharp;

namespace RotationAnalysis.Core.VideoAnalysis;

/// <summary>Saves the running max-blend timelapse image (built up during star tracking) to disk.</summary>
public static class TimelapseRenderer
{
    public static void Save(Mat timelapse, string outputPngPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPngPath)!);
        timelapse.SaveImage(outputPngPath);
    }
}
