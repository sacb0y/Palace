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

    public static Stretch ImageStretch(ImageScaling scaling) =>
        scaling switch
        {
            ImageScaling.Actual => Stretch.None,
            ImageScaling.Fill => Stretch.UniformToFill,
            _ => Stretch.Uniform
        };
}
