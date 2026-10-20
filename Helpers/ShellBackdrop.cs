using System.Numerics;
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
/// Composition GaussianBlur + dim + optional tint) behind ThemeResource chrome.
/// Blur slider drives <see cref="GaussianBlurEffect.BlurAmount"/> — not opacity.
/// WinUI only.
/// </summary>
public static class ShellBackdrop
{
    private const int BlurAnimMs = 120;

    private static string? _loadedPath;
    private static int _loadedEpoch = -1;
    private static AccessibilitySettings? _accessibility;

    private static Border? _glassHost;
    private static SpriteVisual? _blurVisual;
    private static CompositionEffectBrush? _blurBrush;
    private static CompositionEffectFactory? _blurFactory;
    private static Compositor? _blurCompositor;
    private static float _appliedBlurAmount = -1f;
    private static string? _blurCacheKey;

    public static void Release(Image wallpaper)
    {
        wallpaper.Source = null;
        _loadedPath = null;
        _loadedEpoch = -1;
        ReleaseGlassBlur();
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
        if (IsHighContrast())
        {
            CollapseAll(gradient, wallpaper, glass, dim, tint);
            return false;
        }

        var light = gradient.ActualTheme == ElementTheme.Light;
        var preset = ShellBackground.Preset;

        if (ShellBackground.UsesSystemBackdrop)
        {
            CollapseAll(gradient, wallpaper, glass, dim, tint);
            return true;
        }

        var stops = ShellPresetGradients.TryStops(preset, light);
        if (stops is { } gradientStops)
        {
            gradient.Visibility = Visibility.Visible;
            gradient.Background = GradientBrush(gradientStops.Start, gradientStops.Mid, gradientStops.End);
        }
        else
        {
            // Image-only presets (packaged / Custom): keep a subtle fallback under the image.
            gradient.Visibility = Visibility.Visible;
            var fallback = ShellPresetGradients.TryStops(ShellPresetKind.Default, light)!.Value;
            gradient.Background = GradientBrush(fallback.Start, fallback.Mid, fallback.End);
        }

        BindImage(wallpaper, light);
        ApplyGlassBlur(glass, light);

        if (ShellBackground.DarknessApplies)
        {
            var darkness = ShellBackground.DimOpacity(light, ShellBackground.Darkness);
            dim.Opacity = darkness;
            dim.Visibility = darkness <= 0.001 ? Visibility.Collapsed : Visibility.Visible;
            // Light: white wash (fade toward white). Dark: black. HC collapses earlier.
            var (dr, dg, db) = ShellBackground.DimRgb(light);
            dim.Background = new SolidColorBrush(Color.FromArgb(255, dr, dg, db));
        }
        else
        {
            dim.Visibility = Visibility.Collapsed;
            dim.Background = null;
            dim.Opacity = 0;
        }

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

        return false;
    }

    private static void ApplyGlassBlur(Border glass, bool light)
    {
        var blurApplies = ShellBackground.BlurApplies;
        var blurPercent = ShellBackground.Blur;
        var radius = ShellBackground.BlurRadius(blurPercent);
        var cacheKey = ShellBackground.BlurCacheKey(ShellBackground.Preset, light, blurPercent);

        if (!blurApplies || radius <= 0.01f)
        {
            ClearGlassHost(glass);
            return;
        }

        // Opacity stays fully on — amount is BlurAmount, not a sharp/frost cross-fade.
        glass.Opacity = 1;
        glass.Visibility = Visibility.Visible;
        // Soft tint under the blur sprite (fixed alpha — not slider opacity).
        var glassTint = ToColor(ShellBackground.GlassTintHex(light), Color.FromArgb(255, 12, 14, 20));
        glass.Background = new SolidColorBrush(Color.FromArgb(28, glassTint.R, glassTint.G, glassTint.B));

        EnsureBlurVisual(glass);
        if (_blurVisual is null || _blurBrush is null)
        {
            return;
        }

        SyncBlurVisualSize(glass);

        // Cache key = preset + theme + quantized radius; skip redundant anim when unchanged.
        if (_blurCacheKey == cacheKey && Math.Abs(_appliedBlurAmount - radius) <= 0.05f)
        {
            return;
        }

        _blurCacheKey = cacheKey;
        if (Math.Abs(_appliedBlurAmount - radius) > 0.05f)
        {
            AnimateBlurAmount(radius);
            _appliedBlurAmount = radius;
        }
    }

    private static void EnsureBlurVisual(Border glass)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(glass).Compositor;
        if (_blurVisual is not null
            && ReferenceEquals(_glassHost, glass)
            && ReferenceEquals(_blurCompositor, compositor)
            && _blurBrush is not null)
        {
            return;
        }

        if (_glassHost is not null)
        {
            DetachGlassHost(_glassHost);
        }

        ReleaseGlassBlurResources();
        _glassHost = glass;
        _blurCompositor = compositor;

        // Soft pads outside samples with transparent black. We oversize the sprite
        // and clip the host so that soft edge never shows as unblurred wallpaper.
        var effect = new GaussianBlurEffect
        {
            Name = "Blur",
            BlurAmount = 0f,
            BorderMode = EffectBorderMode.Soft,
            Optimization = EffectOptimization.Balanced,
            Source = new CompositionEffectSourceParameter("backdrop")
        };
        // One factory + brush; BlurAmount is animated. Cache key tracks preset/theme/radius
        // so Apply can skip redundant work; GPU blur stays cheap at 4K.
        _blurFactory = compositor.CreateEffectFactory(effect, ["Blur.BlurAmount"]);
        _blurBrush = _blurFactory.CreateBrush();
        _blurBrush.SetSourceParameter("backdrop", compositor.CreateBackdropBrush());

        _blurVisual = compositor.CreateSpriteVisual();
        _blurVisual.Brush = _blurBrush;
        // Manual Size/Offset (not RelativeSizeAdjustment) so we can overscan past the host.
        ElementCompositionPreview.SetElementChildVisual(glass, _blurVisual);
        // Clip child sprite to the glass bounds — overscan soft edges stay cropped.
        ElementCompositionPreview.GetElementVisual(glass).Clip =
            compositor.CreateInsetClip(0, 0, 0, 0);
        glass.SizeChanged -= Glass_SizeChanged;
        glass.SizeChanged += Glass_SizeChanged;
        SyncBlurVisualSize(glass);
        _appliedBlurAmount = -1f;
    }

    private static void AnimateBlurAmount(float radius)
    {
        if (_blurBrush is null)
        {
            return;
        }

        try
        {
            var compositor = _blurBrush.Compositor;
            var anim = compositor.CreateScalarKeyFrameAnimation();
            anim.InsertKeyFrame(1f, radius);
            anim.Duration = TimeSpan.FromMilliseconds(BlurAnimMs);
            anim.StopBehavior = AnimationStopBehavior.SetToFinalValue;
            _blurBrush.Properties.StartAnimation("Blur.BlurAmount", anim);
        }
        catch
        {
            try
            {
                _blurBrush.Properties.InsertScalar("Blur.BlurAmount", radius);
            }
            catch
            {
                // Painted blur must never take down the process.
            }
        }
    }

    private static void Glass_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Border glass)
        {
            SyncBlurVisualSize(glass);
        }
    }

    private static void SyncBlurVisualSize(Border glass)
    {
        if (_blurVisual is null)
        {
            return;
        }

        var w = (float)glass.ActualWidth;
        var h = (float)glass.ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        // Pad for max radius so dragging the slider does not resize the sprite.
        // Soft kernel grows ~3× BlurAmount past each edge; crop hides that fringe.
        var pad = ShellBackground.BlurEdgePad(ShellBackground.MaxBlurRadius);
        _blurVisual.Size = new Vector2(w + pad * 2f, h + pad * 2f);
        _blurVisual.Offset = new Vector3(-pad, -pad, 0f);
    }

    private static void ClearGlassHost(Border glass)
    {
        glass.Visibility = Visibility.Collapsed;
        glass.Background = null;
        glass.Opacity = 0;
        if (ReferenceEquals(_glassHost, glass))
        {
            ReleaseGlassBlur();
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

    private static void DetachGlassHost(Border glass)
    {
        glass.SizeChanged -= Glass_SizeChanged;
        try
        {
            ElementCompositionPreview.SetElementChildVisual(glass, null);
            ElementCompositionPreview.GetElementVisual(glass).Clip = null;
        }
        catch
        {
            // ignore
        }
    }

    private static void ReleaseGlassBlurResources()
    {
        _blurVisual = null;
        _blurBrush = null;
        _blurFactory = null;
        _blurCompositor = null;
        _appliedBlurAmount = -1f;
        _blurCacheKey = null;
    }

    private static void ReleaseGlassBlur()
    {
        if (_glassHost is not null)
        {
            DetachGlassHost(_glassHost);
        }

        _glassHost = null;
        ReleaseGlassBlurResources();
    }

    private static void BindImage(Image wallpaper, bool light)
    {
        var path = ShellBackground.BoundImagePath(light);
        var epoch = ShellBackground.WallpaperEpoch;
        if (string.IsNullOrWhiteSpace(path)
            || (ShellBackground.Preset == ShellPresetKind.Custom && !ShellBackground.ShouldBindWallpaper))
        {
            wallpaper.Source = null;
            wallpaper.Visibility = Visibility.Collapsed;
            _loadedPath = null;
            _loadedEpoch = -1;
            return;
        }

        if (_loadedPath != path || _loadedEpoch != epoch)
        {
            wallpaper.Source = null;
            try
            {
                wallpaper.Source = new BitmapImage
                {
                    DecodePixelWidth = ShellBackground.WallpaperDecodeWidth,
                    UriSource = new Uri(path, UriKind.Absolute)
                };
                wallpaper.Visibility = Visibility.Visible;
                _loadedPath = path;
                _loadedEpoch = epoch;
            }
            catch
            {
                wallpaper.Source = null;
                wallpaper.Visibility = Visibility.Collapsed;
                _loadedPath = null;
                _loadedEpoch = -1;
            }
        }
        else
        {
            wallpaper.Visibility = Visibility.Visible;
        }
    }

    private static void CollapseAll(
        Border gradient,
        Image wallpaper,
        Border glass,
        Border dim,
        Border tint)
    {
        gradient.Visibility = Visibility.Collapsed;
        gradient.Background = null;
        wallpaper.Source = null;
        wallpaper.Visibility = Visibility.Collapsed;
        ClearGlassHost(glass);
        dim.Visibility = Visibility.Collapsed;
        dim.Background = null;
        tint.Visibility = Visibility.Collapsed;
        tint.Background = null;
        _loadedPath = null;
        _loadedEpoch = -1;
        ReleaseGlassBlur();
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
