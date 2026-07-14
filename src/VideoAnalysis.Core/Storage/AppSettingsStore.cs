using System.Text.Json;

namespace VideoAnalysis.Core.Storage;

public sealed class AppSettingsStore
{
    public string SettingsPath { get; }

    public AppSettingsStore(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? Path.Combine(StoragePaths.Root, "settings.json");
    }

    /// <summary>True when a settings file already exists on disk - used to detect a first run.
    /// Callers should check this before any operation that might write settings (for example, <see cref="Save"/>).</summary>
    public bool SettingsFileExists => File.Exists(SettingsPath);

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            // A corrupted settings file shouldn't prevent the app from starting.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings));
    }
}
