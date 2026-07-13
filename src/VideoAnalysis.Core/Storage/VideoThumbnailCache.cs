using VideoAnalysis.Core.VideoAnalysis;

namespace VideoAnalysis.Core.Storage;

/// <summary>Caches a decoded representative frame per video-library entry, under
/// <see cref="Directory"/>. Keyed by entry Id (not a hash of the file path), so an in-place
/// rename of the source video never invalidates the cached image.</summary>
public static class VideoThumbnailCache
{
    public static string Directory { get; } = Path.Combine(StoragePaths.Root, "thumbnails");

    /// <summary>Decodes a frame from <paramref name="videoPath"/> and writes it to
    /// "{entryId}.png", returning the filename (not full path) to store on the entry - or null
    /// if the frame couldn't be read.</summary>
    public static async Task<string?> GenerateAsync(Guid entryId, string videoPath, CancellationToken ct = default)
    {
        var bytes = await VideoFrameReader.ReadRepresentativeFrameAsync(videoPath, ct).ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }

        System.IO.Directory.CreateDirectory(Directory);
        var fileName = $"{entryId}.png";
        await File.WriteAllBytesAsync(Path.Combine(Directory, fileName), bytes, ct).ConfigureAwait(false);
        return fileName;
    }
}
