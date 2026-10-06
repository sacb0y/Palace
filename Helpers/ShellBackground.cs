using System.Globalization;

namespace Palace.Helpers;

/// <summary>
/// App-wide glass shell wallpaper. Persist via LocalSettings; the window
/// paints a layer behind ThemeResource chrome. Off WinUI.
/// </summary>
public static class ShellBackground
{
    public const string WallpaperPathKey = "ShellWallpaperPath";
    public const string DarknessKey = "ShellDarkness";
    public const string BlurKey = "ShellBlur";
    public const string TintEnabledKey = "ShellTintEnabled";
    public const string TintKey = "ShellTintColor";

    public const string WallpaperFilePrefix = "shell-wallpaper-";
    public const double MinAmount = 0;
    public const double MaxAmount = 100;
    public const double DefaultDarkness = 40;
    public const double DefaultBlur = 45;
    public const double LightDimScale = 0.35;
    public const string DefaultTintHex = "#502C3A6B";
    public const string DefaultWallpaperLabel = "Default gradient";
    public const string DarkGradientStartHex = "#FF0B0E14";
    public const string DarkGradientMidHex = "#FF161022";
    public const string DarkGradientEndHex = "#FF0A1628";
    public const string LightGradientStartHex = "#FFF7F8FB";
    public const string LightGradientMidHex = "#FFE8EDF5";
    public const string LightGradientEndHex = "#FFD9E3F0";
    public const string DarkGlassTintHex = "#FF0C0E14";
    public const string LightGlassTintHex = "#FFF2F4F8";

    public static string? WallpaperPath { get; private set; }

    public static double Darkness { get; private set; } = DefaultDarkness;

    public static double Blur { get; private set; } = DefaultBlur;

    public static bool TintEnabled { get; private set; }

    public static string TintHex { get; private set; } = DefaultTintHex;

    public static int WallpaperEpoch { get; private set; }

    public static bool HasWallpaper => !string.IsNullOrWhiteSpace(WallpaperPath);

    public static string WallpaperLabel
    {
        get
        {
            if (!HasWallpaper)
            {
                return DefaultWallpaperLabel;
            }

            var path = WallpaperPath!;
            var slash = path.LastIndexOfAny(['/', '\\']);
            return slash >= 0 && slash < path.Length - 1
                ? path[(slash + 1)..]
                : path;
        }
    }

    public static event EventHandler? Changed;

    /// <summary>
    /// Drop the displayed <c>BitmapImage</c> before overwriting or deleting the
    /// store file so the decode does not keep it locked.
    /// </summary>
    public static event EventHandler? DisplayReleasing;

    public static void Apply(
        string? wallpaperPath,
        double darkness,
        double blur,
        bool tintEnabled,
        string? tintHex,
        bool reloadWallpaper = false)
    {
        wallpaperPath = NormalizePath(wallpaperPath);
        darkness = ClampAmount(darkness);
        blur = ClampAmount(blur);
        tintHex = ParseTint(tintHex);
        var unchanged = WallpaperPath == wallpaperPath
            && Math.Abs(Darkness - darkness) < 0.01
            && Math.Abs(Blur - blur) < 0.01
            && TintEnabled == tintEnabled
            && TintHex == tintHex;
        var pathChanged = WallpaperPath != wallpaperPath;
        if (unchanged && !reloadWallpaper)
        {
            return;
        }

        WallpaperPath = wallpaperPath;
        Darkness = darkness;
        Blur = blur;
        TintEnabled = tintEnabled;
        TintHex = tintHex;
        if (pathChanged || reloadWallpaper)
        {
            WallpaperEpoch++;
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void ReleaseDisplay() =>
        DisplayReleasing?.Invoke(null, EventArgs.Empty);

    /// <summary>
    /// Reload the current wallpaper after <see cref="ReleaseDisplay"/> when a
    /// replace did not commit a new path (copy failed).
    /// </summary>
    public static void RestoreDisplay() =>
        Changed?.Invoke(null, EventArgs.Empty);

    public static void Refresh() =>
        RestoreDisplay();

    public static bool IsLightChrome(string? requestedTheme, bool systemIsLight) =>
        requestedTheme switch
        {
            "Light" => true,
            "Dark" => false,
            _ => systemIsLight
        };

    public static string GradientStartHex(bool lightChrome) =>
        lightChrome ? LightGradientStartHex : DarkGradientStartHex;

    public static string GradientMidHex(bool lightChrome) =>
        lightChrome ? LightGradientMidHex : DarkGradientMidHex;

    public static string GradientEndHex(bool lightChrome) =>
        lightChrome ? LightGradientEndHex : DarkGradientEndHex;

    public static string GlassTintHex(bool lightChrome) =>
        lightChrome ? LightGlassTintHex : DarkGlassTintHex;

    public static double DimOpacity(bool lightChrome, double darkness)
    {
        var t = ClampAmount(darkness) / 100.0;
        return lightChrome ? t * LightDimScale : t;
    }

    public static string NewWallpaperFileName(string? extension)
    {
        var ext = extension ?? "";
        if (ext.Length > 0 && ext[0] != '.')
        {
            ext = "." + ext;
        }

        return WallpaperFilePrefix + Guid.NewGuid().ToString("N") + ext;
    }

    public static bool IsStoredWallpaperName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var name = fileName;
        var slash = name.LastIndexOfAny(['/', '\\']);
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        return name.StartsWith(WallpaperFilePrefix, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("shell-wallpaper.", StringComparison.OrdinalIgnoreCase);
    }

    public static string? NormalizePath(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        var path = stored.Trim();
        return path.Length == 0 ? null : path;
    }

    public static string? ParsePath(object? stored) =>
        stored is string text ? NormalizePath(text) : null;

    public static bool ParseEnabled(object? stored) => stored switch
    {
        bool value => value,
        string text when bool.TryParse(text, out var parsed) => parsed,
        _ => false
    };

    public static double ParseDarkness(object? stored) =>
        ParseAmount(stored, DefaultDarkness);

    public static double ParseBlur(object? stored) =>
        ParseAmount(stored, DefaultBlur);

    public static string ParseTint(object? stored)
    {
        var text = stored as string;
        return TryParseTint(text, out _, out _, out _, out _)
            ? NormalizeTint(text)!
            : DefaultTintHex;
    }

    public static string? NormalizeTint(string? hex) =>
        TryParseTint(hex, out var a, out var r, out var g, out var b)
            ? $"#{a:X2}{r:X2}{g:X2}{b:X2}"
            : null;

    public static bool TryParseTint(string? hex, out byte a, out byte r, out byte g, out byte b)
    {
        a = r = g = b = 0;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var text = hex.Trim();
        if (text.StartsWith('#') || text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[0] == '#' ? text[1..] : text[2..];
        }

        if (text.Length == 6)
        {
            text = "FF" + text;
        }

        if (text.Length != 8
            || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        a = (byte)(value >> 24);
        r = (byte)(value >> 16);
        g = (byte)(value >> 8);
        b = (byte)value;
        return true;
    }

    public static double ClampAmount(double value) =>
        Math.Clamp(value, MinAmount, MaxAmount);

    public static string AmountLabel(double value) =>
        $"{ClampAmount(value):0}%";

    public static double ParseAmount(object? stored, double fallback)
    {
        var value = stored switch
        {
            double number => number,
            float number => number,
            int number => number,
            long number => number,
            string text when double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => fallback
        };
        return ClampAmount(value);
    }
}
