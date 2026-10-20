using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Palace.Models;

namespace Palace.Helpers;

public static class BindHelpers
{
    public static Visibility BoolToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility InvertBoolToVisibility(bool value) =>
        value ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Broken Custom tiles dim to 55% (avoid x:Bind bool-literal helpers).</summary>
    public static double BrokenTileOpacity(bool isBroken) => isBroken ? 0.55 : 1.0;

    public static bool Not(bool value) => !value;

    public static bool IsFolderSort(FolderTreeSort current, string value) => current.ToString() == value;

    public static bool IsMosaicField(MosaicSort current, string field) =>
        MosaicSortOrder.FieldOf(current).ToString() == field;

    // WinUI XamlCompiler (WASDK WinUI 2.3.x) can throw opaque WMC9999 when x:Bind
    // passes bool literals (true/false or x:True/x:False) into a helper. Keep these
    // unary — do not add a (MosaicSort, bool) overload for RadioMenuFlyoutItem.IsChecked.
    public static bool IsMosaicDescending(MosaicSort current) =>
        MosaicSortOrder.IsDescending(current);

    public static bool IsMosaicAscending(MosaicSort current) =>
        !MosaicSortOrder.IsDescending(current);

    /// <summary>AccentButtonStyle when <paramref name="accent"/>; otherwise null (default button).</summary>
    public static Style? AccentButtonWhen(bool accent) =>
        accent
        && Application.Current?.Resources.TryGetValue("AccentButtonStyle", out var style) == true
            ? style as Style
            : null;

    public static string OrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    public static Visibility StringToVisibility(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility CountToVisibility(int count) =>
        count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Accent outline thickness for selected tag-board rows (no face "On" label).</summary>
    public static Thickness SelectedBorder(bool selected) =>
        selected ? new Thickness(2) : new Thickness(0);

    /// <summary>
    /// Next-plate margin: left-docked Details never steals the right inset.
    /// </summary>
    public static Thickness GalleryNextMargin(bool showDetails, bool bottomSheet, double panelWidth)
    {
        var like = ViewerDetailsLayout.NextMargin(showDetails, bottomSheet, panelWidth);
        return new Thickness(like.Left, like.Top, like.Right, like.Bottom);
    }

    /// <summary>
    /// Prev-plate margin: slides right of the left-docked Details card when open.
    /// </summary>
    public static Thickness GalleryPrevMargin(bool showDetails, bool bottomSheet, double panelWidth)
    {
        var like = ViewerDetailsLayout.PrevMargin(showDetails, bottomSheet, panelWidth);
        return new Thickness(like.Left, like.Top, like.Right, like.Bottom);
    }

    public static string DetailsLandmarkName(string? fileName) =>
        ViewerDetailsLayout.LandmarkName(fileName);

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
