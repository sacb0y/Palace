using System.Globalization;
using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// App-wide glass shell wallpaper. Persist via LocalSettings; the window
/// paints a layer behind ThemeResource chrome. Off WinUI.
/// </summary>
public static class ShellBackground
{
    public const string WallpaperPathKey = "ShellWallpaperPath";
    public const string WallpaperNameKey = "ShellWallpaperName";
    public const string DarknessKey = "ShellDarkness";
    public const string BlurKey = "ShellBlur";
    public const string TintEnabledKey = "ShellTintEnabled";
    public const string TintKey = "ShellTintColor";
    public const string PresetKey = "ShellPreset";

    public const string WallpaperFilePrefix = "shell-wallpaper-";
    /// <summary>
    /// Physical pixel cap for the always-resident wallpaper <c>BitmapImage</c>.
    /// Set <c>DecodePixelWidth</c> before <c>UriSource</c> — the Uri constructor
    /// starts a native decode and ignores a later cap.
    /// </summary>
    public const int WallpaperDecodeWidth = 2560;

    public const double MinAmount = 0;
    public const double MaxAmount = 100;
    public const double DefaultDarkness = 40;
    public const double DefaultBlur = 45;
    /// <summary>
    /// Composition GaussianBlur <c>BlurAmount</c> at slider 100%. Percent maps
    /// through <see cref="BlurAmountGamma"/> — never an opacity cross-fade.
    /// </summary>
    public const float MaxBlurRadius = 64f;
    /// <summary>
    /// Ease-in exponent for percent → radius. &gt;1 softens the 0→10 jump so
    /// low slider ticks stay subtle; 100% still reaches <see cref="MaxBlurRadius"/>.
    /// </summary>
    public const double BlurAmountGamma = 1.35;
    /// <summary>Quantize radius for cache keys / cheap 4K re-apply (2 DIP steps).</summary>
    public const int BlurRadiusStep = 2;
    /// <summary>
    /// Soft-mode kernel half-width multiplier (std deviation × 3). Used to
    /// oversize the blur sprite so transparent edges crop outside the host.
    /// </summary>
    public const float BlurEdgeKernel = 3f;
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
    public const string DarkDimHex = "#FF000000";
    public const string LightDimHex = "#FFFFFFFF";

    /// <summary>
    /// Settings picker: chrome, then Vivid, then Creator, Custom last.
    /// Header is empty for the unlabeled chrome and Custom rows.
    /// </summary>
    public static readonly (string Header, ShellPresetKind[] Kinds)[] PickerSections =
    [
        ("", [ShellPresetKind.Default, ShellPresetKind.Mica, ShellPresetKind.MicaAlt, ShellPresetKind.Neutral]),
        ("Vivid",
        [
            ShellPresetKind.Ribbons, ShellPresetKind.Capsules, ShellPresetKind.Bokeh, ShellPresetKind.Nebula,
            ShellPresetKind.Aero, ShellPresetKind.Petal, ShellPresetKind.Facets, ShellPresetKind.Aurora
        ]),
        ("Creator",
        [
            ShellPresetKind.Viewport, ShellPresetKind.Blueprint, ShellPresetKind.Sketchbook,
            ShellPresetKind.Pixel, ShellPresetKind.Nodes
        ]),
        ("", [ShellPresetKind.Custom])
    ];

    public static readonly ShellPresetKind[] PickerOrder = FlattenPicker();

    private static ShellPresetKind[] FlattenPicker()
    {
        var list = new List<ShellPresetKind>();
        foreach (var section in PickerSections)
        {
            list.AddRange(section.Kinds);
        }

        return list.ToArray();
    }

    public static ShellPresetKind Preset { get; private set; } = ShellPresetKind.Default;

    public static string? WallpaperPath { get; private set; }

    /// <summary>The user's chosen file name; the store copy has a generated name.</summary>
    public static string? WallpaperName { get; private set; }

    public static double Darkness { get; private set; } = DefaultDarkness;

    public static double Blur { get; private set; } = DefaultBlur;

    public static bool TintEnabled { get; private set; }

    public static string TintHex { get; private set; } = DefaultTintHex;

    public static int WallpaperEpoch { get; private set; }

    public static bool HasWallpaper => !string.IsNullOrWhiteSpace(WallpaperPath);

    /// <summary>True when the painted layer uses a packaged preset bitmap (not Custom file).</summary>
    public static bool IsPackagedImagePreset(ShellPresetKind preset) =>
        PresetGroup(preset) is ShellPresetGroup.Vivid or ShellPresetGroup.Creator;

    public static ShellPresetGroup PresetGroup(ShellPresetKind preset) =>
        NormalizePreset(preset) switch
        {
            ShellPresetKind.Ribbons or ShellPresetKind.Capsules or ShellPresetKind.Bokeh
                or ShellPresetKind.Nebula or ShellPresetKind.Aero or ShellPresetKind.Petal
                or ShellPresetKind.Facets or ShellPresetKind.Aurora => ShellPresetGroup.Vivid,
            ShellPresetKind.Viewport or ShellPresetKind.Blueprint or ShellPresetKind.Sketchbook
                or ShellPresetKind.Pixel or ShellPresetKind.Nodes => ShellPresetGroup.Creator,
            _ => ShellPresetGroup.None
        };

    /// <summary>True when the painted layer uses a bitmap (packaged preset or Custom file).</summary>
    public static bool UsesImageLayer =>
        IsPackagedImagePreset(Preset) || Preset == ShellPresetKind.Custom;

    /// <summary>Mica / MicaAlt — host paints via <c>Window.SystemBackdrop</c>.</summary>
    public static bool UsesSystemBackdrop =>
        Preset is ShellPresetKind.Mica or ShellPresetKind.MicaAlt;

    /// <summary>
    /// Blur slider is meaningful over an image. Default keeps glass for continuity
    /// (acrylic also tints the gradient). Other gradients / Neutral / Mica skip glass.
    /// </summary>
    public static bool BlurApplies =>
        Preset is ShellPresetKind.Default || UsesImageLayer;

    public static bool DarknessApplies => !UsesSystemBackdrop;

    public static string WallpaperLabel
    {
        get
        {
            if (Preset != ShellPresetKind.Custom || !HasWallpaper)
            {
                return PresetDisplayName(Preset);
            }

            if (WallpaperName is { } name)
            {
                return name;
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

    /// <summary>
    /// True between <see cref="ReleaseDisplay"/> and the next restore or
    /// committed apply. <see cref="Changed"/> (theme, sliders) must not re-bind
    /// the previous file while a copy or delete is in flight.
    /// </summary>
    public static bool DisplayHeld { get; private set; }

    /// <summary>Show the wallpaper image — not while a replace holds the file.</summary>
    public static bool ShouldBindWallpaper => HasWallpaper && !DisplayHeld;

    public static void Apply(
        string? wallpaperPath,
        double darkness,
        double blur,
        bool tintEnabled,
        string? tintHex,
        bool reloadWallpaper = false) =>
        Apply(Preset, wallpaperPath, darkness, blur, tintEnabled, tintHex, reloadWallpaper);

    public static void Apply(
        ShellPresetKind preset,
        string? wallpaperPath,
        double darkness,
        double blur,
        bool tintEnabled,
        string? tintHex,
        bool reloadWallpaper = false)
    {
        preset = NormalizePreset(preset);
        wallpaperPath = NormalizePath(wallpaperPath);
        // A concrete user file always means Custom (picker + legacy Apply(path, …) tests).
        if (wallpaperPath is not null)
        {
            preset = ShellPresetKind.Custom;
        }
        else if (preset == ShellPresetKind.Custom)
        {
            preset = ShellPresetKind.Default;
        }

        // Built-in presets never keep a user file bound (packaged images use BoundImagePath).
        if (preset != ShellPresetKind.Custom)
        {
            wallpaperPath = null;
        }

        darkness = ClampAmount(darkness);
        blur = ClampAmount(blur);
        tintHex = ParseTint(tintHex);
        var unchanged = Preset == preset
            && WallpaperPath == wallpaperPath
            && Math.Abs(Darkness - darkness) < 0.01
            && Math.Abs(Blur - blur) < 0.01
            && TintEnabled == tintEnabled
            && TintHex == tintHex;
        var pathChanged = WallpaperPath != wallpaperPath;
        var presetChanged = Preset != preset;
        if (unchanged && !reloadWallpaper)
        {
            return;
        }

        Preset = preset;
        WallpaperPath = wallpaperPath;
        Darkness = darkness;
        Blur = blur;
        TintEnabled = tintEnabled;
        TintHex = tintHex;
        // Theme swap / packaged image / custom replace need a fresh BitmapImage.
        if (pathChanged || reloadWallpaper || (presetChanged && UsesImageLayer))
        {
            WallpaperEpoch++;
            DisplayHeld = false;
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static string PresetDisplayName(ShellPresetKind preset) => preset switch
    {
        ShellPresetKind.Default => DefaultWallpaperLabel,
        ShellPresetKind.Mica => "Mica",
        ShellPresetKind.MicaAlt => "Mica Alt",
        ShellPresetKind.Neutral => "Neutral",
        ShellPresetKind.Ribbons => "Ribbons",
        ShellPresetKind.Capsules => "Capsules",
        ShellPresetKind.Bokeh => "Bokeh",
        ShellPresetKind.Nebula => "Nebula",
        ShellPresetKind.Aero => "Aero",
        ShellPresetKind.Petal => "Petal",
        ShellPresetKind.Facets => "Facets",
        ShellPresetKind.Aurora => "Aurora",
        ShellPresetKind.Viewport => "Viewport",
        ShellPresetKind.Blueprint => "Blueprint",
        ShellPresetKind.Sketchbook => "Sketchbook",
        ShellPresetKind.Pixel => "Pixel",
        ShellPresetKind.Nodes => "Node Graph",
        ShellPresetKind.Custom => "Custom…",
        _ => DefaultWallpaperLabel
    };

    public static string PresetId(ShellPresetKind preset) => preset.ToString();

    public static string PresetAssetStem(ShellPresetKind preset) =>
        PresetId(NormalizePreset(preset)).ToLowerInvariant();

    public static ShellPresetKind NormalizePreset(ShellPresetKind preset) =>
        Enum.IsDefined(preset) ? preset : ShellPresetKind.Default;

    public static ShellPresetKind ParsePreset(object? stored)
    {
        if (stored is string text && !string.IsNullOrWhiteSpace(text))
        {
            var key = text.Trim();
            // Legacy #98 names removed from the picker.
            if (key.Equals("Dusk", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Slate", StringComparison.OrdinalIgnoreCase))
            {
                return key.Equals("Slate", StringComparison.OrdinalIgnoreCase)
                    ? ShellPresetKind.Neutral
                    : ShellPresetKind.Default;
            }

            if (key.Equals("Grain", StringComparison.OrdinalIgnoreCase))
            {
                return ShellPresetKind.Aero;
            }

            if (key.Equals("Node Graph", StringComparison.OrdinalIgnoreCase)
                || key.Equals("NodeGraph", StringComparison.OrdinalIgnoreCase))
            {
                return ShellPresetKind.Nodes;
            }

            if (Enum.TryParse<ShellPresetKind>(key, ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed))
            {
                return parsed;
            }
        }

        return ShellPresetKind.Default;
    }

    /// <summary>
    /// Resolve stored preset. Non-empty wallpaper path migrates to Custom when
    /// no preset key was saved yet (or the key is empty).
    /// </summary>
    public static ShellPresetKind ResolvePreset(object? storedPreset, object? wallpaperPath)
    {
        var path = ParsePath(wallpaperPath);
        if (storedPreset is string text && !string.IsNullOrWhiteSpace(text))
        {
            var preset = ParsePreset(storedPreset);
            if (preset == ShellPresetKind.Custom && path is null)
            {
                return ShellPresetKind.Default;
            }

            return preset;
        }

        return path is null ? ShellPresetKind.Default : ShellPresetKind.Custom;
    }

    public static string DarknessKeyFor(ShellPresetKind preset) =>
        DarknessKey + "_" + PresetId(NormalizePreset(preset));

    public static string BlurKeyFor(ShellPresetKind preset) =>
        BlurKey + "_" + PresetId(NormalizePreset(preset));

    /// <summary>
    /// Snapshot resolved from LocalSettings (or defaults). Off WinUI so tests can cover
    /// migration / corrupt-value fallback without a packaged host.
    /// </summary>
    public readonly record struct SettingsSnapshot(
        ShellPresetKind Preset,
        string? WallpaperPath,
        string? WallpaperName,
        double Darkness,
        double Blur,
        bool TintEnabled,
        string TintHex,
        bool FellBackToDefault);

    /// <summary>
    /// Parse stored shell background keys. On any failure, returns Default and
    /// <see cref="SettingsSnapshot.FellBackToDefault"/> so startup never crash-loops.
    /// </summary>
    public static SettingsSnapshot ResolveFromStorage(
        object? storedPreset,
        object? wallpaperPath,
        object? wallpaperName,
        Func<ShellPresetKind, object?> darknessFor,
        Func<ShellPresetKind, object?> blurFor,
        object? globalDarkness,
        object? globalBlur,
        object? tintEnabled,
        object? tintHex,
        bool micaSupported,
        bool lightChrome = false,
        bool forceFailure = false)
    {
        try
        {
            if (forceFailure)
            {
                throw new InvalidOperationException("Forced settings failure.");
            }

            var path = ParsePath(wallpaperPath);
            var preset = ResolvePreset(storedPreset, path);
            if (preset is ShellPresetKind.Mica or ShellPresetKind.MicaAlt && !micaSupported)
            {
                preset = ShellPresetKind.Neutral;
            }

            var darknessStored = darknessFor(preset)
                ?? (preset == ShellPresetKind.Default ? globalDarkness : null);
            var blurStored = blurFor(preset)
                ?? (preset == ShellPresetKind.Default ? globalBlur : null);
            return new SettingsSnapshot(
                preset,
                path,
                ParsePath(wallpaperName),
                ParseDarknessFor(preset, darknessStored, lightChrome),
                ParseBlurFor(preset, blurStored, lightChrome),
                ParseEnabled(tintEnabled),
                ParseTint(tintHex),
                FellBackToDefault: false);
        }
        catch
        {
            return DefaultSettingsSnapshot();
        }
    }

    public static SettingsSnapshot DefaultSettingsSnapshot() =>
        new(
            ShellPresetKind.Default,
            null,
            null,
            DefaultDarkness,
            DefaultBlur,
            false,
            DefaultTintHex,
            FellBackToDefault: true);

    /// <summary>
    /// Recreating <c>Window.SystemBackdrop</c> during <c>ActualThemeChanged</c> AVs in
    /// WinUI (<c>Microsoft.UI.Xaml.dll</c>). Keep the existing Mica instance when still wanted.
    /// </summary>
    public static bool ShouldRecreateSystemBackdrop(bool wantMica, bool currentlyHasMica) =>
        wantMica != currentlyHasMica;

    public static bool WantsSystemBackdrop(ShellPresetKind preset, bool micaSupported) =>
        micaSupported && preset is ShellPresetKind.Mica or ShellPresetKind.MicaAlt;

    /// <summary>
    /// First-pick Darkness when no per-preset LocalSettings value exists.
    /// <paramref name="lightChrome"/> selects the manifest Light row; otherwise Dark.
    /// </summary>
    public static double DefaultDarknessFor(ShellPresetKind preset, bool lightChrome = false) =>
        ImageDefaults(preset) is { } row
            ? (lightChrome ? row.LightDarkness : row.DarkDarkness)
            : DefaultDarkness;

    /// <summary>
    /// First-pick Blur when no per-preset LocalSettings value exists.
    /// <paramref name="lightChrome"/> selects the manifest Light row; otherwise Dark.
    /// </summary>
    public static double DefaultBlurFor(ShellPresetKind preset, bool lightChrome = false) =>
        ImageDefaults(preset) is { } row
            ? (lightChrome ? row.LightBlur : row.DarkBlur)
            : DefaultBlur;

    /// <summary>Manifest first-pick blur/darkness for one designed preset, dark then light.</summary>
    private readonly record struct ImageDefaultRow(
        double DarkBlur,
        double DarkDarkness,
        double LightBlur,
        double LightDarkness);

    private static ImageDefaultRow? ImageDefaults(ShellPresetKind preset) =>
        NormalizePreset(preset) switch
        {
            ShellPresetKind.Ribbons => new(45, 45, 45, 20),
            ShellPresetKind.Capsules => new(45, 45, 50, 25),
            ShellPresetKind.Bokeh => new(35, 35, 45, 20),
            ShellPresetKind.Nebula => new(35, 35, 45, 20),
            ShellPresetKind.Aero => new(45, 40, 45, 35),
            ShellPresetKind.Petal => new(45, 45, 45, 20),
            ShellPresetKind.Facets => new(45, 45, 45, 20),
            ShellPresetKind.Aurora => new(40, 40, 45, 20),
            ShellPresetKind.Viewport => new(40, 40, 45, 20),
            ShellPresetKind.Blueprint => new(40, 35, 45, 15),
            ShellPresetKind.Sketchbook => new(40, 35, 40, 15),
            ShellPresetKind.Pixel => new(30, 45, 30, 20),
            ShellPresetKind.Nodes => new(35, 30, 40, 15),
            _ => null
        };

    public static string PackagedImageUri(ShellPresetKind preset, bool lightChrome) =>
        $"ms-appx:///Assets/Backgrounds/{PresetAssetStem(preset)}-{(lightChrome ? "light" : "dark")}.jpg";

    public static string PackagedThumbUri(ShellPresetKind preset, bool lightChrome) =>
        $"ms-appx:///Assets/Backgrounds/{PresetAssetStem(preset)}-{(lightChrome ? "light" : "dark")}-thumb.jpg";

    /// <summary>
    /// Image path the backdrop should bind: Custom store file, or packaged preset URI.
    /// </summary>
    public static string? BoundImagePath(bool lightChrome) =>
        Preset switch
        {
            ShellPresetKind.Custom when ShouldBindWallpaper => WallpaperPath,
            _ when IsPackagedImagePreset(Preset) => PackagedImageUri(Preset, lightChrome),
            _ => null
        };

    public static void SetWallpaperName(string? name) =>
        WallpaperName = NormalizePath(name);

    public static void ReleaseDisplay()
    {
        DisplayHeld = true;
        DisplayReleasing?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Reload the current wallpaper after <see cref="ReleaseDisplay"/> when a
    /// replace did not commit a new path (copy failed).
    /// </summary>
    public static void RestoreDisplay()
    {
        DisplayHeld = false;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Re-paint glass / dim / theme without clearing a replace hold.
    /// </summary>
    public static void Refresh() =>
        Changed?.Invoke(null, EventArgs.Empty);

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

    /// <summary>
    /// Dim overlay color: white wash in Light (fade toward white), black in Dark.
    /// High Contrast collapses the dim layer entirely in <c>ShellBackdrop</c>.
    /// </summary>
    public static string DimHex(bool lightChrome) =>
        lightChrome ? LightDimHex : DarkDimHex;

    public static (byte R, byte G, byte B) DimRgb(bool lightChrome) =>
        lightChrome ? ((byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0);

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

    /// <summary>
    /// Width to decode the resident wallpaper. Native files at or under the cap
    /// keep their width; larger camera photos clamp to <see cref="WallpaperDecodeWidth"/>.
    /// </summary>
    public static int DecodePixelWidth(int sourceWidth) =>
        sourceWidth <= 0 ? WallpaperDecodeWidth : Math.Min(sourceWidth, WallpaperDecodeWidth);

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

    public static double ParseDarknessFor(ShellPresetKind preset, object? stored, bool lightChrome = false) =>
        stored is null
            ? DefaultDarknessFor(preset, lightChrome)
            : ParseAmount(stored, DefaultDarknessFor(preset, lightChrome));

    public static double ParseBlurFor(ShellPresetKind preset, object? stored, bool lightChrome = false) =>
        stored is null
            ? DefaultBlurFor(preset, lightChrome)
            : ParseAmount(stored, DefaultBlurFor(preset, lightChrome));

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

    /// <summary>
    /// Map blur slider percent (0–100) to Composition GaussianBlur radius in DIPs.
    /// Uses an ease-in power curve so the low end is perceptually smoother than linear.
    /// </summary>
    public static float BlurRadius(double blurPercent)
    {
        var t = ClampAmount(blurPercent) / MaxAmount;
        if (t <= 0)
        {
            return 0f;
        }

        return (float)(MaxBlurRadius * Math.Pow(t, BlurAmountGamma));
    }

    /// <summary>
    /// Extra DIP padding so Soft-mode blur edges fall outside the clipped host.
    /// Pass <see cref="MaxBlurRadius"/> for a stable overscan while dragging.
    /// </summary>
    public static float BlurEdgePad(float radiusDip) =>
        radiusDip <= 0.01f ? 0f : MathF.Ceiling(radiusDip * BlurEdgeKernel);

    /// <summary>
    /// Quantized radius for cache keys. Step keeps nearby slider ticks on one
    /// GPU/brush entry without mid-value opacity blending.
    /// </summary>
    public static int QuantizedBlurRadius(double blurPercent) =>
        QuantizeRadiusDip(BlurRadius(blurPercent));

    public static int QuantizeRadiusDip(float radiusDip)
    {
        if (radiusDip <= 0.01f)
        {
            return 0;
        }

        var stepped = (int)Math.Round(radiusDip / BlurRadiusStep) * BlurRadiusStep;
        return Math.Clamp(stepped, BlurRadiusStep, (int)MaxBlurRadius);
    }

    /// <summary>Cache key: preset id + theme + quantized radius.</summary>
    public static string BlurCacheKey(ShellPresetKind preset, bool lightChrome, double blurPercent) =>
        $"{PresetId(NormalizePreset(preset))}:{(lightChrome ? "L" : "D")}:{QuantizedBlurRadius(blurPercent)}";

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
