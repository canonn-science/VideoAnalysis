namespace RotationAnalysis.Core.Diagnostics;

/// <summary>Minimal append-only error log. Without this, a failure only ever surfaces as a
/// one-line message in the UI, with no way to see what actually went wrong afterward.</summary>
public static class AppLog
{
    public static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RotationAnalysisLab", "app.log");

    private static readonly object WriteLock = new();

    public static void LogError(string context, Exception ex)
    {
        try
        {
            lock (WriteLock)
            {
                var directory = Path.GetDirectoryName(LogPath)!;
                Directory.CreateDirectory(directory);
                File.AppendAllText(LogPath, $"{DateTime.UtcNow:O} [{context}] {ex}{Environment.NewLine}");
            }
        }
        catch
        {
            // logging must never be the reason the app crashes
        }
    }
}
