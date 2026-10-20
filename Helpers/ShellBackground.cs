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
    /// <summary>Gooey Appearance: Darkness slider 0–90%.</summary>
    public const double MaxDarkness = 90;
    /// <summary>Gooey Appearance: Blur slider 0–40 px (Composition BlurAmount).</summary>
    public const double MaxBlurPx = 40;
    public const double DefaultDarkness = 40;
    /// <summary>~old 45% gamma-mapped radius, clamped into the 0–40 px model.</summary>
    public const double DefaultBlur = 18;
    /// <summary>Composition GaussianBlur <c>BlurAmount</c> ceiling (matches MaxBlurPx).</summary>
    public const float MaxBlurRadius = 40f;
    /// <summary>Legacy percent→px ease-in (pre-0.2.8). Used only when migrating stored values &gt; MaxBlurPx.</summary>
    public const double LegacyBlurAmountGamma = 1.35;
    /// <summary>Legacy Composition radius at 100% blur (pre-0.2.8).</summary>
    public const float LegacyMaxBlurRadius = 64f;
    /// <summary>Deprecated alias — prefer <see cref="MaxDarkness"/> / <see cref="MaxBlurPx"/>.</summary>
    public const double MaxAmount = MaxDarkness;
    /// <summary>Quantize radius for cache keys / cheap 4K re-apply (2 DIP steps).</summary>
    public const int BlurRadiusStep = 2;
    /// <summary>
    /// Soft-mode kernel half-width multiplier (std deviation × 3). Used to
    /// size the blur sprite margin in physical pixels (DPI-aware).
    /// </summary>
    public const float BlurEdgeKernel = 3f;
    /// <summary>Minimum physical-pixel coverage margin on each blur-sprite edge.</summary>
    public const float BlurCoverageMinPhysicalPx = 2f;
    public const double LightDimScale = 0.35;
    public const string DefaultTintHex = "#502C3A6B";
    public const string DefaultWallpaperLabel = "Default gradient";
    /// <summary>Gooey Appearance preview: 16:9 frame ~280 DIP tall.</summary>
    public const double PreviewHeightDip = 280;
    /// <summary>Preview width for <see cref="PreviewHeightDip"/> at 16:9.</summary>
    public const double PreviewWidthDip = PreviewHeightDip * 16.0 / 9.0;
    /// <summary>LocalSettings flush after last blur/darkness slider tick.</summary>
    public const int PersistDebounceMs = ShellBackgroundPersist.DebounceMs;
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
    /// Blur/darkness only — hosts should <c>InsertScalar</c> animatable Composition
    /// props (no surface reload). Fired by <see cref="ApplyLiveAmounts"/>.
    /// </summary>
    public static event EventHandler? LiveAmountsChanged;

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

        darkness = ClampDarkness(darkness);
        blur = NormalizeBlur(blur);
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

    /// <summary>
    /// Update darkness/blur without bumping <see cref="WallpaperEpoch"/> or
    /// rebinding the wallpaper. Composition hosts listen via
    /// <see cref="LiveAmountsChanged"/> and only <c>InsertScalar</c>.
    /// </summary>
    public static void ApplyLiveAmounts(double darkness, double blur)
    {
        darkness = ClampDarkness(darkness);
        blur = NormalizeBlur(blur);
        if (Math.Abs(Darkness - darkness) < 0.01 && Math.Abs(Blur - blur) < 0.01)
        {
            return;
        }

        Darkness = darkness;
        Blur = blur;
        LiveAmountsChanged?.Invoke(null, EventArgs.Empty);
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
            // Rows are (DarkBlurPx, DarkDarkness%, LightBlurPx, LightDarkness%).
            ShellPresetKind.Ribbons => new(18, 45, 18, 20),
            ShellPresetKind.Capsules => new(18, 45, 20, 25),
            ShellPresetKind.Bokeh => new(14, 35, 18, 20),
            ShellPresetKind.Nebula => new(14, 35, 18, 20),
            ShellPresetKind.Aero => new(18, 40, 18, 35),
            ShellPresetKind.Petal => new(18, 45, 18, 20),
            ShellPresetKind.Facets => new(18, 45, 18, 20),
            ShellPresetKind.Aurora => new(16, 40, 18, 20),
            ShellPresetKind.Viewport => new(16, 40, 18, 20),
            ShellPresetKind.Blueprint => new(16, 35, 18, 15),
            ShellPresetKind.Sketchbook => new(16, 35, 16, 15),
            ShellPresetKind.Pixel => new(12, 45, 12, 20),
            ShellPresetKind.Nodes => new(14, 30, 16, 15),
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
        var t = ClampDarkness(darkness) / 100.0;
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
        ParseDarknessAmount(stored, DefaultDarkness);

    public static double ParseBlur(object? stored) =>
        ParseBlurAmount(stored, DefaultBlur);

    public static double ParseDarknessFor(ShellPresetKind preset, object? stored, bool lightChrome = false) =>
        stored is null
            ? DefaultDarknessFor(preset, lightChrome)
            : ParseDarknessAmount(stored, DefaultDarknessFor(preset, lightChrome));

    public static double ParseBlurFor(ShellPresetKind preset, object? stored, bool lightChrome = false) =>
        stored is null
            ? DefaultBlurFor(preset, lightChrome)
            : ParseBlurAmount(stored, DefaultBlurFor(preset, lightChrome));

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

    public static double ClampDarkness(double value) =>
        Math.Clamp(value, MinAmount, MaxDarkness);

    public static double ClampBlur(double value) =>
        Math.Clamp(value, MinAmount, MaxBlurPx);

    /// <summary>Deprecated: clamps as darkness (0–90). Prefer <see cref="ClampDarkness"/> / <see cref="ClampBlur"/>.</summary>
    public static double ClampAmount(double value) => ClampDarkness(value);

    /// <summary>
    /// Composition GaussianBlur radius in DIPs. Blur is stored as px (0–<see cref="MaxBlurPx"/>).
    /// Values above MaxBlurPx are treated as legacy percent (0–100) and migrated.
    /// </summary>
    public static float BlurRadius(double blurPxOrLegacyPercent) =>
        (float)NormalizeBlur(blurPxOrLegacyPercent);

    /// <summary>Map a legacy 0–100% blur slider through the pre-0.2.8 gamma curve into px.</summary>
    public static double MigrateBlurFromLegacyPercent(double percent)
    {
        var t = Math.Clamp(percent, MinAmount, 100) / 100.0;
        if (t <= 0)
        {
            return 0;
        }

        return ClampBlur(LegacyMaxBlurRadius * Math.Pow(t, LegacyBlurAmountGamma));
    }

    /// <summary>
    /// Normalize stored blur: ≤ MaxBlurPx stays px; &gt; MaxBlurPx is legacy percent → px.
    /// </summary>
    public static double NormalizeBlur(double stored) =>
        stored > MaxBlurPx ? MigrateBlurFromLegacyPercent(stored) : ClampBlur(stored);

    /// <summary>
    /// Soft-kernel half-width pad in DIPs (stdDev × <see cref="BlurEdgeKernel"/>).
    /// </summary>
    public static float BlurEdgePad(float radiusDip) =>
        radiusDip <= 0.01f ? 0f : MathF.Ceiling(radiusDip * BlurEdgeKernel);

    /// <summary>
    /// Blur-sprite edge pad in DIPs after ceiling in <b>physical</b> pixels.
    /// At 150% DPI, a DIP-only pad can leave a 1px gap on the top/right; this
    /// converts through <paramref name="rasterizationScale"/> so coverage is exact.
    /// </summary>
    public static float BlurVisualPadDip(float radiusDip, double rasterizationScale)
    {
        var scale = rasterizationScale <= 0 ? 1.0 : rasterizationScale;
        var padDip = BlurEdgePad(radiusDip);
        var physical = Math.Max(
            BlurCoverageMinPhysicalPx,
            Math.Ceiling(Math.Max(padDip, 1f) * scale));
        return (float)(physical / scale);
    }

    /// <summary>
    /// Host width/height in DIPs snapped up to whole physical pixels, then
    /// converted back — avoids a sub-pixel shortfall at non-100% DPI.
    /// </summary>
    public static float BlurHostSizeDip(double logicalDip, double rasterizationScale)
    {
        if (logicalDip <= 0)
        {
            return 0f;
        }

        var scale = rasterizationScale <= 0 ? 1.0 : rasterizationScale;
        return (float)(Math.Ceiling(logicalDip * scale) / scale);
    }

    /// <summary>True when the blur slider should hide the sharp wallpaper layer.</summary>
    public static bool BlurHidesWallpaper(double blurPx) =>
        BlurApplies && BlurRadius(blurPx) > 0.01f;

    /// <summary>
    /// Quantized radius for cache keys. Step keeps nearby slider ticks on one
    /// GPU/brush entry without mid-value opacity blending.
    /// </summary>
    public static int QuantizedBlurRadius(double blurPx) =>
        QuantizeRadiusDip(BlurRadius(blurPx));

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
    public static string BlurCacheKey(ShellPresetKind preset, bool lightChrome, double blurPx) =>
        $"{PresetId(NormalizePreset(preset))}:{(lightChrome ? "L" : "D")}:{QuantizedBlurRadius(blurPx)}";

    public static string AmountLabel(double value) => DarknessLabel(value);

    public static string DarknessLabel(double value) =>
        $"{ClampDarkness(value):0}%";

    public static string BlurLabel(double value) =>
        $"{NormalizeBlur(value):0} px";

    public static double ParseAmount(object? stored, double fallback) =>
        ParseDarknessAmount(stored, fallback);

    public static double ParseDarknessAmount(object? stored, double fallback)
    {
        var value = ReadNumber(stored, fallback);
        return ClampDarkness(value);
    }

    public static double ParseBlurAmount(object? stored, double fallback)
    {
        var value = ReadNumber(stored, fallback);
        return NormalizeBlur(value);
    }

    private static double ReadNumber(object? stored, double fallback) => stored switch
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
}
