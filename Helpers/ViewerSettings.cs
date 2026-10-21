namespace Palace.Helpers;

/// <summary>
/// Viewer + Input prefs (LocalSettings). Off WinUI for Palace.Tests.
/// Default zoom opens the overlay; Details / loop share <see cref="GalleryChrome"/> keys.
/// Touch / swipe-down gates are ready for the touch milestone.
/// </summary>
public static class ViewerSettings
{
    public const string DefaultZoomKey = "ViewerDefaultZoom";
    public const string TouchGesturesKey = "ViewerTouchGestures";
    public const string SwipeDownDismissKey = "ViewerSwipeDownDismiss";

    public const ImageScaling DefaultZoom = ImageScaling.Fit;
    public const bool DefaultTouchGestures = true;
    public const bool DefaultSwipeDownDismiss = true;

    public static readonly string[] ZoomOptions =
    [
        GalleryScale.Label(ImageScaling.Fit),
        GalleryScale.Label(ImageScaling.Fill),
        GalleryScale.Label(ImageScaling.Actual)
    ];

    public static ImageScaling DefaultZoomMode { get; private set; } = DefaultZoom;

    public static bool TouchGesturesEnabled { get; private set; } = DefaultTouchGestures;

    public static bool SwipeDownDismissEnabled { get; private set; } = DefaultSwipeDownDismiss;

    public static event EventHandler? Changed;

    public static void Apply(
        ImageScaling defaultZoom,
        bool touchGestures,
        bool swipeDownDismiss)
    {
        if (DefaultZoomMode == defaultZoom
            && TouchGesturesEnabled == touchGestures
            && SwipeDownDismissEnabled == swipeDownDismiss)
        {
            return;
        }

        DefaultZoomMode = defaultZoom;
        TouchGesturesEnabled = touchGestures;
        SwipeDownDismissEnabled = swipeDownDismiss;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static string ZoomLabel(ImageScaling scaling) => GalleryScale.Label(scaling);

    public static ImageScaling ParseZoom(object? stored) => stored switch
    {
        ImageScaling scaling => NormalizeZoom(scaling),
        int value when Enum.IsDefined(typeof(ImageScaling), value) =>
            NormalizeZoom((ImageScaling)value),
        string text => ParseZoomLabel(text),
        _ => DefaultZoom
    };

    public static ImageScaling ParseZoomLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return DefaultZoom;
        }

        var text = label.Trim();
        if (text.Equals("1:1", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Actual", StringComparison.OrdinalIgnoreCase)
            || text.Equals(nameof(ImageScaling.Actual), StringComparison.OrdinalIgnoreCase))
        {
            return ImageScaling.Actual;
        }

        if (text.Equals("Fill", StringComparison.OrdinalIgnoreCase)
            || text.Equals(nameof(ImageScaling.Fill), StringComparison.OrdinalIgnoreCase))
        {
            return ImageScaling.Fill;
        }

        if (text.Equals("Fit", StringComparison.OrdinalIgnoreCase)
            || text.Equals(nameof(ImageScaling.Fit), StringComparison.OrdinalIgnoreCase))
        {
            return ImageScaling.Fit;
        }

        return DefaultZoom;
    }

    public static ImageScaling NormalizeZoom(ImageScaling scaling) => scaling switch
    {
        ImageScaling.Actual => ImageScaling.Actual,
        ImageScaling.Fill => ImageScaling.Fill,
        _ => ImageScaling.Fit
    };

    public static string PersistZoom(ImageScaling scaling) => ZoomLabel(NormalizeZoom(scaling));

    public static bool ParseBool(object? stored, bool defaultValue) => stored switch
    {
        bool value => value,
        string text when bool.TryParse(text, out var parsed) => parsed,
        _ => defaultValue
    };

    /// <summary>
    /// Whether touch / pen / trackpad Manipulation gestures may pan and pinch.
    /// Mouse left-drag pan stays available when touch gestures are off.
    /// </summary>
    public static bool AllowsTouchGestures(bool touchGesturesEnabled) => touchGesturesEnabled;

    /// <summary>
    /// Swipe-down-to-dismiss overlay. Honored when the touch milestone wires the gesture.
    /// </summary>
    public static bool AllowsSwipeDownDismiss(bool swipeDownDismissEnabled) => swipeDownDismissEnabled;

    public static string SdrTonemapCaption { get; } =
        "On SDR displays, HDR stills tonemap to 80 nits (scRGB 1.0). Peak override clips on HDR displays only.";

    public static string SlideshowPlaceholderCaption { get; } =
        "Timed slideshow advances land in a later update.";
}
