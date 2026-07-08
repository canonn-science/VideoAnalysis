using RotationAnalysis.Core.Storage;
using Xunit;

namespace RotationAnalysis.Core.Tests;

public class VideoFileNamerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VideoFileNamerTests_" + Guid.NewGuid());

    public VideoFileNamerTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData("Aurorae Rings.mp4", "Aurorae Rings", true)]
    [InlineData("aurorae rings.MP4", "Aurorae Rings", true)] // case-insensitive on name and extension
    [InlineData("recording.mp4", "Aurorae Rings", false)]
    public void MatchesRingName_ComparesBaseNameCaseInsensitively(string fileName, string ringName, bool expected)
    {
        var path = Path.Combine(_directory, fileName);
        Assert.Equal(expected, VideoFileNamer.MatchesRingName(path, ringName));
    }

    [Fact]
    public void GetNextAvailableFileName_UsesPlainRingNameWhenFree()
    {
        var videoPath = Path.Combine(_directory, "recording.mp4");
        var suggested = VideoFileNamer.GetNextAvailableFileName(videoPath, "Aurorae Rings");

        Assert.Equal(Path.Combine(_directory, "Aurorae Rings.mp4"), suggested);
    }

    [Fact]
    public void GetNextAvailableFileName_PreservesSourceExtension()
    {
        var videoPath = Path.Combine(_directory, "recording.mkv");
        var suggested = VideoFileNamer.GetNextAvailableFileName(videoPath, "Aurorae Rings");

        Assert.Equal(Path.Combine(_directory, "Aurorae Rings.mkv"), suggested);
    }

    [Fact]
    public void GetNextAvailableFileName_SkipsToNextFreeVersionSuffix()
    {
        File.WriteAllText(Path.Combine(_directory, "Aurorae Rings.mp4"), "");
        File.WriteAllText(Path.Combine(_directory, "Aurorae Rings_v2.mp4"), "");

        var videoPath = Path.Combine(_directory, "recording.mp4");
        var suggested = VideoFileNamer.GetNextAvailableFileName(videoPath, "Aurorae Rings");

        Assert.Equal(Path.Combine(_directory, "Aurorae Rings_v3.mp4"), suggested);
    }

    [Fact]
    public void GetNextAvailableFileName_SanitizesInvalidFileNameCharacters()
    {
        var videoPath = Path.Combine(_directory, "recording.mp4");
        var suggested = VideoFileNamer.GetNextAvailableFileName(videoPath, "A/B: Ring?");

        Assert.Equal(Path.Combine(_directory, "A_B_ Ring_.mp4"), suggested);
    }
}
