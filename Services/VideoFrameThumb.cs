using Palace.Helpers;
using Windows.Media.Editing;
using Windows.Media.Playback;
using Windows.Storage;

namespace Palace.Services;

/// <summary>
/// Grab a mosaic-sized JPEG from a local video at the current MediaPlayer
/// seek position via <see cref="MediaComposition.GetThumbnailAsync"/> (WIC /
/// inbox codecs — no Magick / Win2D). Writes only into
/// <see cref="ThumbnailService"/> thumb cache.
/// </summary>
public static class VideoFrameThumb
{
    public static async Task<ThumbnailInfo?> CaptureMosaicAsync(
        MediaPlayer? player,
        string filePath,
        string contentHash,
        ThumbnailService thumbs,
        int? catalogWidth = null,
        int? catalogHeight = null)
    {
        if (player is null
            || string.IsNullOrEmpty(filePath)
            || string.IsNullOrEmpty(contentHash)
            || !CloudFile.TryGetAttributes(filePath, out var attrs)
            || CloudFile.IsOnlineOnly(attrs))
        {
            return null;
        }

        var session = player.PlaybackSession;
        var position = session?.Position ?? TimeSpan.Zero;
        var naturalW = (int)(session?.NaturalVideoWidth ?? 0);
        var naturalH = (int)(session?.NaturalVideoHeight ?? 0);
        if (naturalW < 1 || naturalH < 1)
        {
            naturalW = catalogWidth ?? 0;
            naturalH = catalogHeight ?? 0;
        }

        var size = VideoMosaicThumb.ScaledSize(naturalW, naturalH, ThumbnailService.MaxSide);

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath).AsTask().ConfigureAwait(false);
            var clip = await MediaClip.CreateFromFileAsync(file).AsTask().ConfigureAwait(false);
            var composition = new MediaComposition();
            composition.Clips.Add(clip);
            var at = VideoMosaicThumb.ClampPosition(position, clip.OriginalDuration);
            using var stream = await composition.GetThumbnailAsync(
                    at,
                    size.Width,
                    size.Height,
                    VideoFramePrecision.NearestFrame)
                .AsTask()
                .ConfigureAwait(false);
            if (stream is null)
            {
                return null;
            }

            return await thumbs.CacheScaledJpegAsync(contentHash, stream, VideoMosaicThumb.MosaicSuffix)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }
}
