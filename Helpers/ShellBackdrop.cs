using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace Palace.Helpers;

/// <summary>
/// Paints the glass wallpaper layer (theme gradient / user image + Acrylic blur +
/// dim + optional tint) behind ThemeResource chrome. WinUI only.
/// </summary>
public static class ShellBackdrop
{
    private static string? _loadedPath;
    private static int _loadedEpoch = -1;

    public static void Release(Image wallpaper)
    {
        wallpaper.Source = null;
        _loadedPath = null;
        _loadedEpoch = -1;
    }

    public static void Apply(
        Border gradient,
        Image wallpaper,
        Border glass,
        Border dim,
        Border tint)
    {
        var light = gradient.ActualTheme == ElementTheme.Light;
        gradient.Visibility = Visibility.Visible;
        gradient.Background = GradientBrush(light);

        var path = ShellBackground.WallpaperPath;
        var epoch = ShellBackground.WallpaperEpoch;
        if (ShellBackground.HasWallpaper && !string.IsNullOrWhiteSpace(path))
        {
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
        else
        {
            wallpaper.Source = null;
            wallpaper.Visibility = Visibility.Collapsed;
            _loadedPath = null;
            _loadedEpoch = -1;
        }

        var blur = ShellBackground.Blur / 100.0;
        glass.Opacity = blur;
        glass.Visibility = blur <= 0.001 ? Visibility.Collapsed : Visibility.Visible;
        var glassTint = ToColor(ShellBackground.GlassTintHex(light), Color.FromArgb(255, 12, 14, 20));
        glass.Background = new AcrylicBrush
        {
            TintColor = glassTint,
            TintOpacity = 0.18 + (blur * 0.22),
            TintLuminosityOpacity = light ? 0.85 : 0.45,
            FallbackColor = Color.FromArgb(220, glassTint.R, glassTint.G, glassTint.B)
        };

        var darkness = ShellBackground.DimOpacity(light, ShellBackground.Darkness);
        dim.Opacity = darkness;
        dim.Visibility = darkness <= 0.001 ? Visibility.Collapsed : Visibility.Visible;
        dim.Background = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));

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

    private static Brush GradientBrush(bool light) =>
        new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop
                {
                    Offset = 0,
                    Color = ToColor(ShellBackground.GradientStartHex(light), Color.FromArgb(255, 11, 14, 20))
                },
                new GradientStop
                {
                    Offset = 0.48,
                    Color = ToColor(ShellBackground.GradientMidHex(light), Color.FromArgb(255, 22, 16, 34))
                },
                new GradientStop
                {
                    Offset = 1,
                    Color = ToColor(ShellBackground.GradientEndHex(light), Color.FromArgb(255, 10, 22, 40))
                }
            }
        };

    private static Color ToColor(string hex, Color fallback) =>
        ShellBackground.TryParseTint(hex, out var a, out var r, out var g, out var b)
            ? Color.FromArgb(a, r, g, b)
            : fallback;
}
