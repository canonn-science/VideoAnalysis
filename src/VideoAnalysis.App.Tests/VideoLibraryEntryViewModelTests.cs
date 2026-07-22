using VideoAnalysis.App.ViewModels;
using VideoAnalysis.Core.Storage;
using Xunit;

namespace VideoAnalysis.App.Tests;

/// <summary>Covers <see cref="VideoLibraryEntryViewModel.VideoMetadataText"/> (GitHub issue #55):
/// the thumbnail's resolution/duration corner badge.</summary>
public class VideoLibraryEntryViewModelTests
{
    private static VideoLibraryEntryViewModel CreateEntry(int? width, int? height, double? durationSeconds) =>
        new(new VideoLibraryEntry
        {
            FilePath = @"C:\videos\a.mp4",
            VideoWidth = width,
            VideoHeight = height,
            VideoDurationSeconds = durationSeconds,
        });

    [Fact]
    public void VideoMetadataText_Null_WhenNeitherResolutionNorDurationKnown()
    {
        var entry = CreateEntry(null, null, null);

        Assert.Null(entry.VideoMetadataText);
        Assert.False(entry.HasVideoMetadata);
    }

    [Fact]
    public void VideoMetadataText_ResolutionOnly_WhenDurationUnknown()
    {
        var entry = CreateEntry(1920, 1080, null);

        Assert.Equal("1920×1080", entry.VideoMetadataText);
        Assert.True(entry.HasVideoMetadata);
    }

    [Fact]
    public void VideoMetadataText_DurationOnly_WhenResolutionUnknown()
    {
        var entry = CreateEntry(null, null, 65);

        Assert.Equal("1:05", entry.VideoMetadataText);
    }

    [Fact]
    public void VideoMetadataText_JoinsResolutionAndDuration_WhenBothKnown()
    {
        var entry = CreateEntry(1920, 1080, 225);

        Assert.Equal("1920×1080 • 3:45", entry.VideoMetadataText);
    }

    [Fact]
    public void VideoMetadataText_UsesHourFormat_ForDurationsAtOrOverAnHour()
    {
        var entry = CreateEntry(null, null, 3661);

        Assert.Equal("1:01:01", entry.VideoMetadataText);
    }
}
