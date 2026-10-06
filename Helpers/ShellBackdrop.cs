using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace Palace.Helpers;

/// <summary>
/// Paints the glass wallpaper layer (gradient / user image + Acrylic blur +
/// dim + optional tint) behind ThemeResource chrome. WinUI only.
/// </summary>
public static class ShellBackdrop
{
    public static void Apply(
        Border gradient,
        Image wallpaper,
        Border glass,
        Border dim,
        Border tint)
    {
        var path = ShellBackground.WallpaperPath;
        if (ShellBackground.HasWallpaper && !string.IsNullOrWhiteSpace(path))
        {
            try
            {
                wallpaper.Source = new BitmapImage(new Uri(path, UriKind.Absolute));
                wallpaper.Visibility = Visibility.Visible;
            }
            catch
            {
                wallpaper.Source = null;
                wallpaper.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            wallpaper.Source = null;
            wallpaper.Visibility = Visibility.Collapsed;
        }

        gradient.Visibility = Visibility.Visible;

        var blur = ShellBackground.Blur / 100.0;
        glass.Opacity = blur;
        glass.Visibility = blur <= 0.001 ? Visibility.Collapsed : Visibility.Visible;
        glass.Background = new AcrylicBrush
        {
            TintColor = Color.FromArgb(255, 12, 14, 20),
            TintOpacity = 0.18 + (blur * 0.22),
            TintLuminosityOpacity = 0.45,
            FallbackColor = Color.FromArgb(220, 12, 14, 20)
        };

        var darkness = ShellBackground.Darkness / 100.0;
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
}
