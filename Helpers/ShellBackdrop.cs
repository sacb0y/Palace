using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Palace.Models;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Palace.Helpers;

/// <summary>
/// Paints the glass wallpaper layer (theme gradient / preset / user image +
/// Composition GaussianBlur then darkness) behind ThemeResource chrome.
/// Preview and the real shell share one <see cref="LoadedImageSurface"/>; slider
/// ticks only <c>InsertScalar</c> animatable props (no surface reload).
/// WinUI only.
/// </summary>
public static partial class ShellBackdrop
{
    private const string BlurAmountProperty = "Blur.BlurAmount";
    private const string DimOpacityProperty = "Dim.Opacity";
    private const string DimColorProperty = "DimColor.Color";

    private static string? _loadedPath;
    private static int _loadedEpoch = -1;
    private static AccessibilitySettings? _accessibility;

    private static Compositor? _compositor;
    private static CompositionEffectFactory? _effectFactory;
    private static LoadedImageSurface? _sharedSurface;
    private static string? _sharedSurfacePath;
    private static bool _sharedSurfaceIsLight;
    private static float _liveBlurAmount = -1f;
    private static float _liveDimOpacity = -1f;
    private static bool _liveDimIsLight;

    private static readonly GlassSlot MainSlot = new();
    private static readonly GlassSlot PreviewSlot = new();

    private sealed class GlassSlot
    {
        public Border? Host;
        public Image? Wallpaper;
        public Border? Gradient;
        public Border? Dim;
        public Border? Tint;
        public SpriteVisual? Visual;
        public CompositionEffectBrush? Brush;
        public CompositionBrush? SourceBrush;
        /// <summary>
        /// Path the current <see cref="SourceBrush"/> was built for. Shared-surface
        /// path can update while this slot still holds a brush for the previous
        /// preset — without this, Petal→Aurora early-returns and paints stale/empty.
        /// </summary>
        public string? BoundSourcePath;
        public bool BoundSourceIsLight;
        public bool IsPreview;
    }

    public static void Release(Image wallpaper)
    {
        wallpaper.Source = null;
        _loadedPath = null;
        _loadedEpoch = -1;
        ReleaseSlot(MainSlot);
        // Keep shared surface for preview until DetachPreview / full release.
        if (PreviewSlot.Host is null)
        {
            ReleaseSharedSurface();
            ReleaseFactory();
        }
    }

    public static bool IsHighContrast()
    {
        try
        {
            _accessibility ??= new AccessibilitySettings();
            return _accessibility.HighContrast;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Apply painted layers. Returns whether the host should use a Mica system backdrop
    /// (<see cref="ShellPresetKind.Mica"/> / <see cref="ShellPresetKind.MicaAlt"/>).
    /// </summary>
    public static bool Apply(
        Border gradient,
        Image wallpaper,
        Border glass,
        Border dim,
        Border tint)
    {
        MainSlot.Gradient = gradient;
        MainSlot.Wallpaper = wallpaper;
        MainSlot.Dim = dim;
        MainSlot.Tint = tint;
        return ApplyToSlot(MainSlot, glass, dim, tint, isPreview: false);
    }

    /// <summary>
    /// Settings 16:9 preview — same LoadedImageSurface + effect recipe as the shell.
    /// Mock nav/card are XAML siblings above <paramref name="glass"/> (never blurred).
    /// </summary>
    public static void AttachPreview(
        Border gradient,
        Image wallpaper,
        Border glass,
        Border dim)
    {
        PreviewSlot.Gradient = gradient;
        PreviewSlot.Wallpaper = wallpaper;
        PreviewSlot.Dim = dim;
        PreviewSlot.Tint = null;
        PreviewSlot.IsPreview = true;
        ApplyToSlot(PreviewSlot, glass, dim, tint: null, isPreview: true);
    }

    public static void DetachPreview()
    {
        ReleaseSlot(PreviewSlot);
        PreviewSlot.Gradient = null;
        PreviewSlot.Wallpaper = null;
        PreviewSlot.Dim = null;
        if (MainSlot.Host is null)
        {
            ReleaseSharedSurface();
            ReleaseFactory();
        }
    }

    /// <summary>
    /// Slider path: only <c>InsertScalar</c> blur + darkness on attached hosts.
    /// Crossing the glass on/off boundary rebuilds the SpriteVisual once (no surface reload
    /// when the image path is unchanged).
    /// </summary>
    public static void SyncLiveAmounts()
    {
        if (IsHighContrast())
        {
            return;
        }

        SyncLiveSlot(MainSlot);
        SyncLiveSlot(PreviewSlot);
    }

    private static void SyncLiveSlot(GlassSlot slot)
    {
        if (slot.Host is null)
        {
            return;
        }

        var light = ResolveLightChrome(slot.Gradient);
        var blurAmount = ShellBackground.BlurApplies
            ? ShellBackground.BlurRadius(ShellBackground.Blur)
            : 0f;
        var dimOpacity = ShellBackground.DarknessApplies
            ? (float)ShellBackground.DimOpacity(light, ShellBackground.Darkness)
            : 0f;
        var useGlass = UsesCompositionGlass(blurAmount, dimOpacity);

        if (ShellBackground.UsesSystemBackdrop && !slot.IsPreview)
        {
            return;
        }

        if (useGlass)
        {
            EnsureGlassVisual(slot, slot.Host, light);
            InsertScalars(slot, blurAmount, dimOpacity, light);
            UpdateGlassVisibility(slot, blurAmount, dimOpacity);
            if (slot.Dim is not null)
            {
                slot.Dim.Visibility = Visibility.Collapsed;
                slot.Dim.Background = null;
                slot.Dim.Opacity = 0;
            }
        }
        else
        {
            ClearGlassHost(slot, slot.Host);
            ApplyXamlDim(slot.Dim, light, dimOpacity);
        }

        UpdateWallpaperVisibility(slot);
    }

    private static bool ApplyToSlot(
        GlassSlot slot,
        Border glass,
        Border? dim,
        Border? tint,
        bool isPreview)
    {
        slot.IsPreview = isPreview;
        // Keep the Border reference even when glass is collapsed so SyncLiveAmounts
        // can reattach without a full page re-Apply.
        slot.Host = glass;
        slot.Dim = dim;
        if (IsHighContrast())
        {
            CollapseSlot(slot, glass, dim, tint);
            return false;
        }

        var light = ResolveLightChrome(slot.Gradient);

        if (ShellBackground.UsesSystemBackdrop && !isPreview)
        {
            CollapseSlot(slot, glass, dim, tint);
            return true;
        }

        if (ShellBackground.UsesSystemBackdrop && isPreview)
        {
            // Preview shows a quiet mica stand-in (no system backdrop in-page).
            PaintGradient(slot, light, forceDefault: true);
            ClearGlassHost(slot, glass);
            if (dim is not null)
            {
                dim.Visibility = Visibility.Collapsed;
                dim.Background = null;
                dim.Opacity = 0;
            }

            if (slot.Wallpaper is not null)
            {
                slot.Wallpaper.Source = null;
                slot.Wallpaper.Visibility = Visibility.Collapsed;
            }

            return false;
        }

        PaintGradient(slot, light, forceDefault: false);
        BindImage(slot, light);

        var blurAmount = ShellBackground.BlurApplies
            ? ShellBackground.BlurRadius(ShellBackground.Blur)
            : 0f;
        var dimOpacity = ShellBackground.DarknessApplies
            ? (float)ShellBackground.DimOpacity(light, ShellBackground.Darkness)
            : 0f;
        var useGlass = UsesCompositionGlass(blurAmount, dimOpacity);

        if (useGlass)
        {
            EnsureGlassVisual(slot, glass, light);
            InsertScalars(slot, blurAmount, dimOpacity, light);
            UpdateGlassVisibility(slot, blurAmount, dimOpacity);
            if (dim is not null)
            {
                // Darkness is in the effect brush — avoid double dim.
                dim.Visibility = Visibility.Collapsed;
                dim.Background = null;
                dim.Opacity = 0;
            }
        }
        else
        {
            ClearGlassHost(slot, glass);
            ApplyXamlDim(dim, light, dimOpacity);
        }

        UpdateWallpaperVisibility(slot);

        if (tint is not null)
        {
            if (ShellBackground.TintEnabled
                && ShellBackground.TryParseTint(ShellBackground.TintHex, out var a, out var r, out var g, out var b)
                && a > 0)
            {
                tint.Visibility = Visibility.Visible;
                tint.Background = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            }
            else
            {
                tint.Visibility = Visibility.Collapsed;
                tint.Background = null;
            }
        }

        return false;
    }

    private static bool UsesCompositionGlass(float blurAmount, float dimOpacity) =>
        (ShellBackground.BlurApplies && blurAmount > 0.01f)
        || (ShellBackground.DarknessApplies && dimOpacity > 0.001f && ShellBackground.BlurApplies);

    private static void PaintGradient(GlassSlot slot, bool light, bool forceDefault)
    {
        if (slot.Gradient is null)
        {
            return;
        }

        var preset = forceDefault ? ShellPresetKind.Default : ShellBackground.Preset;
        var stops = ShellPresetGradients.TryStops(preset, light)
            ?? ShellPresetGradients.TryStops(ShellPresetKind.Default, light);
        if (stops is { } gradientStops)
        {
            slot.Gradient.Visibility = Visibility.Visible;
            slot.Gradient.Background = GradientBrush(gradientStops.Start, gradientStops.Mid, gradientStops.End);
        }
        else
        {
            slot.Gradient.Visibility = Visibility.Collapsed;
            slot.Gradient.Background = null;
        }
    }

    private static void EnsureGlassVisual(GlassSlot slot, Border glass, bool light)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(glass).Compositor;
        EnsureFactory(compositor);

        var imagePath = ShellBackground.BoundImagePath(light);
        EnsureSharedSurface(imagePath, light);

        var wantSurface = !string.IsNullOrWhiteSpace(imagePath) && _sharedSurface is not null;
        // After EnsureSharedSurface succeeds, _sharedSurfacePath already matches imagePath —
        // checking it again is redundant (BoundSourcePath + theme are the slot contract).
        System.Diagnostics.Debug.Assert(
            !wantSurface
            || string.Equals(_sharedSurfacePath, imagePath, StringComparison.OrdinalIgnoreCase),
            "EnsureSharedSurface should leave _sharedSurfacePath matching imagePath when loaded.");

        if (IsSlotReady(
                hasVisual: slot.Visual is not null,
                hostMatches: ReferenceEquals(slot.Host, glass),
                compositorMatches: ReferenceEquals(_compositor, compositor),
                hasEffectBrush: slot.Brush is not null,
                wantSurface: wantSurface,
                hasSurfaceSourceBrush: slot.SourceBrush is CompositionSurfaceBrush,
                hasBackdropSourceBrush: slot.SourceBrush is CompositionBackdropBrush,
                boundSourcePath: slot.BoundSourcePath,
                imagePath: imagePath,
                boundSourceIsLight: slot.BoundSourceIsLight,
                light: light))
        {
            SyncBlurVisualSize(slot, glass);
            return;
        }

        if (!ReferenceEquals(slot.Host, glass))
        {
            if (slot.Host is not null)
            {
                DetachHost(slot);
            }

            slot.Host = glass;
        }

        ReleaseSlotResources(slot);
        _compositor = compositor;

        slot.Brush = _effectFactory!.CreateBrush();
        slot.SourceBrush = CreateSourceBrush(compositor, imagePath);
        slot.BoundSourcePath = wantSurface ? imagePath : null;
        slot.BoundSourceIsLight = light;
        slot.Brush.SetSourceParameter("source", slot.SourceBrush);
        TryInsertColor(slot.Brush, light);

        slot.Visual = compositor.CreateSpriteVisual();
        slot.Visual.Brush = slot.Brush;
        slot.Visual.RelativeSizeAdjustment = Vector2.Zero;
        ElementCompositionPreview.SetElementChildVisual(glass, slot.Visual);
        glass.SizeChanged -= Glass_SizeChanged;
        glass.SizeChanged += Glass_SizeChanged;
        glass.Tag = slot;
        SyncBlurVisualSize(slot, glass);

        // Soft tint under the blur sprite (fixed alpha — not slider opacity).
        var glassTint = ToColor(ShellBackground.GlassTintHex(light), Color.FromArgb(255, 12, 14, 20));
        glass.Background = new SolidColorBrush(Color.FromArgb(28, glassTint.R, glassTint.G, glassTint.B));
        glass.Opacity = 1;
        glass.Visibility = Visibility.Visible;
    }

    private static void EnsureFactory(Compositor compositor)
    {
        if (_effectFactory is not null && ReferenceEquals(_compositor, compositor))
        {
            return;
        }

        ReleaseFactory();
        _compositor = compositor;
        var sourceParam = new CompositionEffectSourceParameter("source");
        var clamped = new BorderEffect
        {
            ExtendX = CanvasEdgeBehavior.Clamp,
            ExtendY = CanvasEdgeBehavior.Clamp,
            Source = sourceParam
        };
        var blur = new GaussianBlurEffect
        {
            Name = "Blur",
            BlurAmount = 0f,
            BorderMode = EffectBorderMode.Hard,
            Optimization = EffectOptimization.Balanced,
            Source = clamped
        };
        var dimColor = new ColorSourceEffect
        {
            Name = "DimColor",
            Color = Color.FromArgb(255, 0, 0, 0)
        };
        var dim = new OpacityEffect
        {
            Name = "Dim",
            Opacity = 0f,
            Source = dimColor
        };
        var composite = new CompositeEffect
        {
            Mode = CanvasComposite.SourceOver,
            Sources = { blur, dim }
        };
        _effectFactory = compositor.CreateEffectFactory(
            composite,
            [BlurAmountProperty, DimOpacityProperty, DimColorProperty]);
    }

    private static void EnsureSharedSurface(string? imagePath, bool light)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            ReleaseSharedSurface();
            return;
        }

        if (_sharedSurface is not null
            && _sharedSurfacePath == imagePath
            && _sharedSurfaceIsLight == light)
        {
            return;
        }

        ReleaseSharedSurface();
        try
        {
            _sharedSurfacePath = imagePath;
            _sharedSurfaceIsLight = light;
            _sharedSurface = LoadedImageSurface.StartLoadFromUri(new Uri(imagePath, UriKind.Absolute));
        }
        catch
        {
            ReleaseSharedSurface();
        }
    }

    private static CompositionBrush CreateSourceBrush(Compositor compositor, string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || _sharedSurface is null)
        {
            return compositor.CreateBackdropBrush();
        }

        var surfaceBrush = compositor.CreateSurfaceBrush(_sharedSurface);
        surfaceBrush.Stretch = CompositionStretch.UniformToFill;
        return surfaceBrush;
    }

    private static void InsertScalars(GlassSlot slot, float blurAmount, float dimOpacity, bool light)
    {
        if (slot.Brush is null)
        {
            return;
        }

        try
        {
            TryInsertColor(slot.Brush, light);
            slot.Brush.Properties.InsertScalar(BlurAmountProperty, blurAmount);
            slot.Brush.Properties.InsertScalar(DimOpacityProperty, dimOpacity);
        }
        catch
        {
            // Painted blur must never take down the process.
        }

        _liveBlurAmount = blurAmount;
        _liveDimOpacity = dimOpacity;
        _liveDimIsLight = light;
    }

    private static void TryInsertColor(CompositionEffectBrush brush, bool light)
    {
        try
        {
            var (r, g, b) = ShellBackground.DimRgb(light);
            brush.Properties.InsertColor(DimColorProperty, Color.FromArgb(255, r, g, b));
        }
        catch
        {
            // Color animatable prop may be unavailable on some effect graphs — opacity still works.
        }
    }

    private static void UpdateGlassVisibility(GlassSlot slot, float blurAmount, float dimOpacity)
    {
        if (slot.Host is null)
        {
            return;
        }

        var useGlass = UsesCompositionGlass(blurAmount, dimOpacity);
        if (!useGlass)
        {
            slot.Host.Visibility = Visibility.Collapsed;
            slot.Host.Opacity = 0;
            return;
        }

        slot.Host.Visibility = Visibility.Visible;
        slot.Host.Opacity = 1;
    }

    private static void ApplyXamlDim(Border? dim, bool light, float dimOpacity)
    {
        if (dim is null)
        {
            return;
        }

        if (!ShellBackground.DarknessApplies || dimOpacity <= 0.001f)
        {
            dim.Visibility = Visibility.Collapsed;
            dim.Background = null;
            dim.Opacity = 0;
            return;
        }

        var (dr, dg, db) = ShellBackground.DimRgb(light);
        dim.Background = new SolidColorBrush(Color.FromArgb(255, dr, dg, db));
        dim.Opacity = dimOpacity;
        dim.Visibility = Visibility.Visible;
    }

    private static void BindImage(GlassSlot slot, bool light)
    {
        if (slot.Wallpaper is null)
        {
            return;
        }

        var wallpaper = slot.Wallpaper;
        var path = ShellBackground.BoundImagePath(light);
        var epoch = ShellBackground.WallpaperEpoch;
        if (string.IsNullOrWhiteSpace(path)
            || (ShellBackground.Preset == ShellPresetKind.Custom && !ShellBackground.ShouldBindWallpaper))
        {
            wallpaper.Source = null;
            wallpaper.Visibility = Visibility.Collapsed;
            if (!slot.IsPreview)
            {
                _loadedPath = null;
                _loadedEpoch = -1;
            }

            return;
        }

        var needsDecode = slot.IsPreview
            ? wallpaper.Source is null
                || !string.Equals(_sharedSurfacePath, path, StringComparison.OrdinalIgnoreCase)
            : _loadedPath != path || _loadedEpoch != epoch;

        if (needsDecode)
        {
            wallpaper.Source = null;
            try
            {
                wallpaper.Source = new BitmapImage
                {
                    DecodePixelWidth = ShellBackground.WallpaperDecodeWidth,
                    UriSource = new Uri(path, UriKind.Absolute)
                };
                if (!slot.IsPreview)
                {
                    _loadedPath = path;
                    _loadedEpoch = epoch;
                }
            }
            catch
            {
                wallpaper.Source = null;
                if (!slot.IsPreview)
                {
                    _loadedPath = null;
                    _loadedEpoch = -1;
                }
            }
        }

        UpdateWallpaperVisibility(slot);
    }

    private static void UpdateWallpaperVisibility(GlassSlot slot)
    {
        if (slot.Wallpaper is null)
        {
            return;
        }

        var light = ResolveLightChrome(slot.Gradient);
        var blurAmount = ShellBackground.BlurApplies
            ? ShellBackground.BlurRadius(ShellBackground.Blur)
            : 0f;
        var dimOpacity = ShellBackground.DarknessApplies
            ? (float)ShellBackground.DimOpacity(light, ShellBackground.Darkness)
            : 0f;
        var useGlass = UsesCompositionGlass(blurAmount, dimOpacity);
        var path = ShellBackground.BoundImagePath(light);
        var hasPath = !string.IsNullOrWhiteSpace(path)
            && !(ShellBackground.Preset == ShellPresetKind.Custom && !ShellBackground.ShouldBindWallpaper);

        // Composition glass paints the image (blur + darkness). Keep sharp bitmap warm
        // but hidden so slider ticks never re-decode.
        slot.Wallpaper.Visibility = hasPath && !useGlass
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static bool ResolveLightChrome(Border? gradient)
    {
        try
        {
            if (gradient is not null)
            {
                return gradient.ActualTheme == ElementTheme.Light;
            }
        }
        catch
        {
            // fall through
        }

        return false;
    }

    private static void Glass_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Border glass)
        {
            return;
        }

        var slot = ReferenceEquals(glass, MainSlot.Host) ? MainSlot
            : ReferenceEquals(glass, PreviewSlot.Host) ? PreviewSlot
            : null;
        if (slot is not null)
        {
            SyncBlurVisualSize(slot, glass);
        }
    }

    private static void SyncBlurVisualSize(GlassSlot slot, Border glass)
    {
        if (slot.Visual is null)
        {
            return;
        }

        var w = (float)glass.ActualWidth;
        var h = (float)glass.ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        double scale;
        try
        {
            scale = glass.XamlRoot?.RasterizationScale ?? 1.0;
        }
        catch
        {
            scale = 1.0;
        }

        var hostW = ShellBackground.BlurHostSizeDip(w, scale);
        var hostH = ShellBackground.BlurHostSizeDip(h, scale);
        var pad = ShellBackground.BlurVisualPadDip(ShellBackground.MaxBlurRadius, scale);
        slot.Visual.RelativeSizeAdjustment = Vector2.Zero;
        slot.Visual.Size = new Vector2(hostW + pad * 2f, hostH + pad * 2f);
        slot.Visual.Offset = new Vector3(-pad, -pad, 0f);
    }

    private static void ClearGlassHost(GlassSlot slot, Border glass)
    {
        glass.Visibility = Visibility.Collapsed;
        glass.Background = null;
        glass.Opacity = 0;
        // Detach the SpriteVisual + brush, but keep slot.Host so SyncLiveAmounts
        // can reattach without losing the Border reference.
        if (ReferenceEquals(slot.Host, glass))
        {
            DetachHost(slot);
            ReleaseSlotResources(slot);
        }
        else
        {
            try
            {
                ElementCompositionPreview.SetElementChildVisual(glass, null);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static void CollapseSlot(GlassSlot slot, Border glass, Border? dim, Border? tint)
    {
        if (slot.Gradient is not null)
        {
            slot.Gradient.Visibility = Visibility.Collapsed;
            slot.Gradient.Background = null;
        }

        if (slot.Wallpaper is not null)
        {
            slot.Wallpaper.Source = null;
            slot.Wallpaper.Visibility = Visibility.Collapsed;
        }

        ClearGlassHost(slot, glass);
        if (dim is not null)
        {
            dim.Visibility = Visibility.Collapsed;
            dim.Background = null;
            dim.Opacity = 0;
        }

        if (tint is not null)
        {
            tint.Visibility = Visibility.Collapsed;
            tint.Background = null;
        }

        if (!slot.IsPreview)
        {
            _loadedPath = null;
            _loadedEpoch = -1;
        }
    }

    private static void DetachHost(GlassSlot slot)
    {
        if (slot.Host is null)
        {
            return;
        }

        slot.Host.SizeChanged -= Glass_SizeChanged;
        try
        {
            ElementCompositionPreview.SetElementChildVisual(slot.Host, null);
        }
        catch
        {
            // ignore
        }
    }

    private static void ReleaseSlotResources(GlassSlot slot)
    {
        // Dispose Composition brushes before dropping refs (theme flip rebuilds the
        // slot graph; Window.SystemBackdrop must NOT be recreated — see
        // ShellBackground.ShouldRecreateSystemBackdrop / MainWindow.ApplySystemBackdrop).
        TryDisposeComposition(slot.Brush);
        TryDisposeComposition(slot.SourceBrush);
        slot.Visual = null;
        slot.Brush = null;
        slot.SourceBrush = null;
        slot.BoundSourcePath = null;
        slot.BoundSourceIsLight = false;
    }

    private static void TryDisposeComposition(CompositionObject? obj)
    {
        if (obj is null)
        {
            return;
        }

        try
        {
            obj.Dispose();
        }
        catch
        {
            // ignore — painted layers must never take down the process
        }
    }

    private static void ReleaseSlot(GlassSlot slot)
    {
        DetachHost(slot);
        slot.Host = null;
        ReleaseSlotResources(slot);
    }

    private static void ReleaseSharedSurface()
    {
        try
        {
            _sharedSurface?.Dispose();
        }
        catch
        {
            // ignore
        }

        _sharedSurface = null;
        _sharedSurfacePath = null;
    }

    private static void ReleaseFactory()
    {
        _effectFactory = null;
        _compositor = null;
        _liveBlurAmount = -1f;
        _liveDimOpacity = -1f;
    }

    private static Brush GradientBrush(string startHex, string midHex, string endHex) =>
        new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop
                {
                    Offset = 0,
                    Color = ToColor(startHex, Color.FromArgb(255, 11, 14, 20))
                },
                new GradientStop
                {
                    Offset = 0.48,
                    Color = ToColor(midHex, Color.FromArgb(255, 22, 16, 34))
                },
                new GradientStop
                {
                    Offset = 1,
                    Color = ToColor(endHex, Color.FromArgb(255, 10, 22, 40))
                }
            }
        };

    private static Color ToColor(string hex, Color fallback) =>
        ShellBackground.TryParseTint(hex, out var a, out var r, out var g, out var b)
            ? Color.FromArgb(a, r, g, b)
            : fallback;
}
