namespace Palace.Helpers;

/// <summary>
/// Policy for setting a Library mosaic poster from a video playback frame.
/// Off WinUI so <c>Palace.Tests</c> can cover path / enable rules.
/// </summary>
public static class VideoMosaicThumb
{
    public const string OverlayAutomationId = "BtnGallerySetThumb";
    public const string WindowAutomationId = "BtnGalleryWindowSetThumb";

    public const string OverlayReasonAutomationId = "TxtGallerySetThumbReason";
    public const string WindowReasonAutomationId = "TxtGalleryWindowSetThumbReason";

    /// <summary>
    /// Visible when the overlay item is a playable local video (MediaPlayer
    /// path). Online-only / API / missing files stay hidden — do not hydrate
    /// just to pick a poster.
    /// </summary>
    public static bool IsActionVisible(bool isPlayableLocalVideo) => isPlayableLocalVideo;

    public static bool CanSet(bool isPlayableLocalVideo, string? contentHash, string? localPath) =>
        isPlayableLocalVideo
        && !string.IsNullOrEmpty(contentHash)
        && !string.IsNullOrEmpty(localPath);

    /// <summary>
    /// Tooltip / status when the button is hidden or disabled.
    /// </summary>
    public static string? DisabledReason(
        bool isPlayableLocalVideo,
        string? contentHash,
        string? localPath,
        bool isOnlineOnly,
        bool apiOnly)
    {
        if (apiOnly || isOnlineOnly || !isPlayableLocalVideo)
        {
            return "Needs a playable local video — online-only files stay in the cloud.";
        }

        if (string.IsNullOrEmpty(localPath))
        {
            return "Needs a playable local video path.";
        }

        if (string.IsNullOrEmpty(contentHash))
        {
            return "Needs a content hash before a mosaic thumb can be cached.";
        }

        return null;
    }

    /// <summary>
    /// Mosaic JPEG long edge — same budget as <c>ThumbnailService.MaxSide</c>.
    /// </summary>
    public static (int Width, int Height) ScaledSize(int width, int height, int maxSide)
    {
        if (maxSide < 1)
        {
            maxSide = 1;
        }

        if (width < 1 || height < 1)
        {
            return (maxSide, maxSide);
        }

        var scale = Math.Min(maxSide / (double)width, maxSide / (double)height);
        scale = Math.Min(scale, 1.0);
        return (
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    public static TimeSpan ClampPosition(TimeSpan position, TimeSpan duration)
    {
        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        if (duration <= TimeSpan.Zero)
        {
            return position;
        }

        // Nearest-frame at Exact duration can miss; stay just inside the clip.
        var last = duration > TimeSpan.FromMilliseconds(1)
            ? duration - TimeSpan.FromMilliseconds(1)
            : TimeSpan.Zero;
        return position > last ? last : position;
    }

    /// <summary>
    /// Mosaic cache uses the bare hash JPEG (no <c>_lg</c>). Large-preview
    /// suffix stays for cloud preview only.
    /// </summary>
    public static string? MosaicSuffix => null;

    public static bool UsesMosaicCacheName(string? suffix) => string.IsNullOrEmpty(suffix);
}
