using VideoAnalysis.Core.Recording;
using Xunit;

namespace VideoAnalysis.Core.Tests;

public class DefaultWatchFoldersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DefaultWatchFoldersTests_" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string UserProfile => Path.Combine(_root, "user");

    private string ProgramFilesX86 => Path.Combine(_root, "Program Files (x86)");

    [Fact]
    public void Discover_OnEmptyFakeFilesystem_ReturnsNoFolders()
    {
        Directory.CreateDirectory(UserProfile);
        Directory.CreateDirectory(ProgramFilesX86);

        var result = DefaultWatchFolders.Discover(UserProfile, ProgramFilesX86);

        Assert.Empty(result);
    }

    [Fact]
    public void Discover_OnlyReturnsPathsThatActuallyExist()
    {
        var gameBar = Path.Combine(UserProfile, "Videos", "Captures");
        Directory.CreateDirectory(gameBar);

        var steamUser1Recordings = Path.Combine(ProgramFilesX86, "Steam", "userdata", "1000", "gamerecordings");
        Directory.CreateDirectory(steamUser1Recordings);

        // A second Steam userdata folder with no gamerecordings subfolder shouldn't be picked up.
        Directory.CreateDirectory(Path.Combine(ProgramFilesX86, "Steam", "userdata", "2000"));

        var geForceNowGame = Path.Combine(UserProfile, "Videos", "NVIDIA", "GeForce NOW", "Elite Dangerous");
        Directory.CreateDirectory(geForceNowGame);

        var result = DefaultWatchFolders.Discover(UserProfile, ProgramFilesX86);

        Assert.Contains(gameBar, result);
        Assert.Contains(steamUser1Recordings, result);
        Assert.Contains(geForceNowGame, result);
        Assert.Equal(3, result.Count);
    }
}
