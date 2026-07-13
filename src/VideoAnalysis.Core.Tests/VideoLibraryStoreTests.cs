using VideoAnalysis.Core.Storage;
using Xunit;

namespace VideoAnalysis.Core.Tests;

public class VideoLibraryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VideoLibraryStoreTests_" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string LibraryPath => Path.Combine(_directory, "video_library.json");

    [Fact]
    public void GetAll_OnMissingFile_ReturnsEmpty()
    {
        var store = new VideoLibraryStore(LibraryPath);
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void GetAll_OnCorruptFile_ReturnsEmptyWithoutThrowing()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(LibraryPath, "{ not valid json ");

        var store = new VideoLibraryStore(LibraryPath);
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void Add_StampsIdAndTimestamps()
    {
        var store = new VideoLibraryStore(LibraryPath);
        var entry = store.Add(new VideoLibraryEntry { FilePath = @"C:\videos\a.mp4" });

        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.True(entry.AddedUtc > DateTime.MinValue);
        Assert.Equal(entry.AddedUtc, entry.LastSelectedUtc);

        var reloaded = Assert.Single(store.GetAll());
        Assert.Equal(entry.Id, reloaded.Id);
        Assert.Equal(@"C:\videos\a.mp4", reloaded.FilePath);
    }

    [Fact]
    public void FindByPath_MatchesCaseInsensitively_AndMissesUnknownPath()
    {
        var store = new VideoLibraryStore(LibraryPath);
        store.Add(new VideoLibraryEntry { FilePath = @"C:\videos\A.mp4" });

        Assert.NotNull(store.FindByPath(@"c:\videos\a.mp4"));
        Assert.Null(store.FindByPath(@"C:\videos\missing.mp4"));
    }

    [Fact]
    public void GetPage_SlicesInMruOrder()
    {
        var store = new VideoLibraryStore(LibraryPath);
        for (var i = 0; i < 15; i++)
        {
            store.Add(new VideoLibraryEntry { FilePath = $@"C:\videos\{i}.mp4" });
            Thread.Sleep(2); // ensure distinct LastSelectedUtc ordering
        }

        var firstPage = store.GetPage(0, 10);
        var secondPage = store.GetPage(10, 10);

        Assert.Equal(10, firstPage.Count);
        Assert.Equal(5, secondPage.Count);
        Assert.Equal(@"C:\videos\14.mp4", firstPage[0].FilePath); // most recently added is first
    }

    [Fact]
    public void MarkSelected_MovesEntryToTopOfMruOrder()
    {
        var store = new VideoLibraryStore(LibraryPath);
        var first = store.Add(new VideoLibraryEntry { FilePath = @"C:\videos\first.mp4" });
        Thread.Sleep(2);
        store.Add(new VideoLibraryEntry { FilePath = @"C:\videos\second.mp4" });

        store.MarkSelected(first.Id);

        Assert.Equal(first.Id, store.GetAll()[0].Id);
    }

    [Fact]
    public void MarkAnalyzed_AddsTabIdAndSetsStatus()
    {
        var store = new VideoLibraryStore(LibraryPath);
        var entry = store.Add(new VideoLibraryEntry { FilePath = @"C:\videos\a.mp4" });

        store.MarkAnalyzed(entry.Id, "RingRotation");
        store.MarkAnalyzed(entry.Id, "RingRotation"); // idempotent

        var reloaded = Assert.Single(store.GetAll());
        Assert.Equal(VideoLibraryEntryStatus.Analyzed, reloaded.Status);
        Assert.Equal(new[] { "RingRotation" }, reloaded.AnalyzedByTabs);
    }

    [Fact]
    public void UpdatePath_PersistsNewPath()
    {
        var store = new VideoLibraryStore(LibraryPath);
        var entry = store.Add(new VideoLibraryEntry { FilePath = @"C:\videos\old.mp4" });

        store.UpdatePath(entry.Id, @"C:\videos\new.mp4");

        Assert.Equal(@"C:\videos\new.mp4", Assert.Single(store.GetAll()).FilePath);
    }

    [Fact]
    public void UpdateThumbnail_PersistsFileName()
    {
        var store = new VideoLibraryStore(LibraryPath);
        var entry = store.Add(new VideoLibraryEntry { FilePath = @"C:\videos\a.mp4" });

        store.UpdateThumbnail(entry.Id, $"{entry.Id}.png");

        Assert.Equal($"{entry.Id}.png", Assert.Single(store.GetAll()).ThumbnailFileName);
    }

    [Fact]
    public void Remove_DeletesEntry()
    {
        var store = new VideoLibraryStore(LibraryPath);
        var entry = store.Add(new VideoLibraryEntry { FilePath = @"C:\videos\a.mp4" });

        store.Remove(entry.Id);

        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void Mutate_OnUnknownId_DoesNothing()
    {
        var store = new VideoLibraryStore(LibraryPath);
        store.Add(new VideoLibraryEntry { FilePath = @"C:\videos\a.mp4" });

        store.MarkSelected(Guid.NewGuid());
        store.UpdatePath(Guid.NewGuid(), @"C:\videos\other.mp4");

        Assert.Single(store.GetAll());
    }
}
