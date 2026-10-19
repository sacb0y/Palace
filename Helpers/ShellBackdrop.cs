using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Palace.Models;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Palace.Helpers;

/// <summary>
/// Paints the glass wallpaper layer (theme gradient / preset / user image + Acrylic blur +
/// dim + optional tint) behind ThemeResource chrome. WinUI only.
/// </summary>
public static class ShellBackdrop
{
    private static string? _loadedPath;
    private static int _loadedEpoch = -1;
    private static AccessibilitySettings? _accessibility;

    public static void Release(Image wallpaper)
    {
        wallpaper.Source = null;
        _loadedPath = null;
        _loadedEpoch = -1;
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

        var blurApplies = ShellBackground.BlurApplies;
        var blur = ShellBackground.Blur / 100.0;
        if (blurApplies && blur > 0.001)
        {
            glass.Opacity = blur;
            glass.Visibility = Visibility.Visible;
            var glassTint = ToColor(ShellBackground.GlassTintHex(light), Color.FromArgb(255, 12, 14, 20));
            glass.Background = new AcrylicBrush
            {
                TintColor = glassTint,
                TintOpacity = 0.18 + (blur * 0.22),
                TintLuminosityOpacity = light ? 0.85 : 0.45,
                FallbackColor = Color.FromArgb(220, glassTint.R, glassTint.G, glassTint.B)
            };
        }
        else
        {
            glass.Visibility = Visibility.Collapsed;
            glass.Background = null;
            glass.Opacity = 0;
        }

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
        glass.Visibility = Visibility.Collapsed;
        glass.Background = null;
        dim.Visibility = Visibility.Collapsed;
        dim.Background = null;
        tint.Visibility = Visibility.Collapsed;
        tint.Background = null;
        _loadedPath = null;
        _loadedEpoch = -1;
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
