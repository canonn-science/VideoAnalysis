using VideoAnalysis.Core.Storage;
using Xunit;

namespace VideoAnalysis.Core.Tests;

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

    [Fact]
    public void GetNextAvailableFileName_UsesExplicitTargetDirectory_WhenProvided()
    {
        var subDirectory = Path.Combine(_directory, "Aurorae");
        var videoPath = Path.Combine(_directory, "recording.mp4");
        var suggested = VideoFileNamer.GetNextAvailableFileName(videoPath, "Aurorae Rings", subDirectory);

        Assert.Equal(Path.Combine(subDirectory, "Aurorae Rings.mp4"), suggested);
    }

    [Fact]
    public void GetNextAvailableFileName_VersionsIndependently_PerTargetDirectory()
    {
        var subDirectory = Path.Combine(_directory, "Aurorae");
        Directory.CreateDirectory(subDirectory);
        File.WriteAllText(Path.Combine(_directory, "Aurorae Rings.mp4"), "");

        var videoPath = Path.Combine(_directory, "recording.mp4");
        var suggested = VideoFileNamer.GetNextAvailableFileName(videoPath, "Aurorae Rings", subDirectory);

        // The plain name is only taken in _directory, not in the (empty) subfolder - so the
        // subfolder placement shouldn't be forced to a _v2 suffix just because a same-named file
        // happens to exist elsewhere.
        Assert.Equal(Path.Combine(subDirectory, "Aurorae Rings.mp4"), suggested);
    }

    [Theory]
    [InlineData("A/B", "A_B")]
    [InlineData("Col 285 Sector", "Col 285 Sector")]
    public void Sanitize_ReplacesInvalidFileNameCharacters(string input, string expected)
    {
        Assert.Equal(expected, VideoFileNamer.Sanitize(input));
    }
}
