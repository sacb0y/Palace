using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Palace.Helpers;

public static class BindHelpers
{
    public static Visibility BoolToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility InvertBoolToVisibility(bool value) =>
        value ? Visibility.Collapsed : Visibility.Visible;

    public static bool Not(bool value) => !value;

    public static string OrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    public static Visibility StringToVisibility(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Accent outline thickness for selected tag-board rows (no face "On" label).</summary>
    public static Thickness SelectedBorder(bool selected) =>
        selected ? new Thickness(2) : new Thickness(0);

    public static Stretch ImageStretch(ImageScaling scaling) =>
        scaling switch
        {
            // Actual sizes the Image to pixels/raster DIPs; Uniform scales
            // the bitmap into that box. None would paint PixelWidth DIPs and
            // clip at >100% DPI.
            ImageScaling.Actual => Stretch.Uniform,
            ImageScaling.Fill => Stretch.UniformToFill,
            _ => Stretch.Uniform
        };
}
