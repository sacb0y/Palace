using Microsoft.UI.Xaml;

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
}
