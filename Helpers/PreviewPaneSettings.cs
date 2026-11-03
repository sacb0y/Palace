namespace Palace.Helpers;

/// <summary>
/// Library Preview pane prefs (LocalSettings, app-wide — not catalog).
/// Off WinUI for Palace.Tests.
/// </summary>
public static class PreviewPaneSettings
{
    public const string ModeKey = "LibraryPreviewPane";
    public const string WidthKey = "LibraryPreviewPaneWidth";
    public const string UserShowKey = "LibraryPreviewPaneUserShow";

    public const string ModeAutomatic = "Automatic";
    public const string ModeShow = "Show";
    public const string ModeHide = "Hide";
    public const string DefaultMode = ModeAutomatic;

    public static readonly string[] ModeOptions =
    [
        ModeAutomatic,
        ModeShow,
        ModeHide
    ];

    public static string Mode { get; private set; } = DefaultMode;

    public static bool UserShow { get; private set; } = true;

    public static double UserWidth { get; private set; } = LibraryColumnLayout.PreviewDefaultWidth;

    public static event EventHandler? Changed;

    public static bool AutoHideEnabled =>
        string.Equals(Mode, ModeAutomatic, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeMode(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return DefaultMode;
        }

        var text = stored.Trim();
        return ModeOptions.Any(o => o.Equals(text, StringComparison.OrdinalIgnoreCase))
            ? ModeOptions.First(o => o.Equals(text, StringComparison.OrdinalIgnoreCase))
            : DefaultMode;
    }

    public static string ParseMode(object? stored) => stored switch
    {
        string text => NormalizeMode(text),
        _ => DefaultMode
    };

    public static bool ParseUserShow(object? stored) => stored switch
    {
        bool value => value,
        string text when bool.TryParse(text, out var parsed) => parsed,
        _ => true
    };

    public static double ParseWidth(object? stored) => stored switch
    {
        double value => PreviewPane.ClampUserWidth(value),
        float value => PreviewPane.ClampUserWidth(value),
        int value => PreviewPane.ClampUserWidth(value),
        long value => PreviewPane.ClampUserWidth(value),
        string text when double.TryParse(
            text,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed) => PreviewPane.ClampUserWidth(parsed),
        _ => LibraryColumnLayout.PreviewDefaultWidth
    };

    public static void Apply(string mode, bool userShow, double userWidth)
    {
        var normalized = NormalizeMode(mode);
        var width = PreviewPane.ClampUserWidth(userWidth);
        var show = normalized switch
        {
            ModeHide => false,
            ModeShow => true,
            _ => userShow
        };

        if (Mode == normalized && UserShow == show && Math.Abs(UserWidth - width) < 0.01)
        {
            return;
        }

        Mode = normalized;
        UserShow = show;
        UserWidth = width;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Seed a <see cref="PreviewPaneState"/> from persisted prefs (session flags clear).</summary>
    public static PreviewPaneState ToState() =>
        new(
            UserShow: UserShow,
            AutoHidden: false,
            OverlayOpen: false,
            Pinned: false,
            UserWidth: UserWidth);
}
