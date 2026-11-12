using System.Text.RegularExpressions;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Wimbly: facade Translation on BrdPreviewOverlay — animate via
/// <c>BrdPreviewOverlay.StartAnimation</c> only (no GetElementVisual on the overlay).
/// No GrdGalleryOverlay Visibility writes. Edge Collapsed/Visible (not ClearValue).
/// </summary>
public sealed class PreviewOverlayTranslationHitTestTests
{
    private static readonly Regex GetElementVisualOnOverlay = new(
        @"GetElementVisual\s*\(\s*BrdPreviewOverlay\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex SetTranslationEnabled = new(
        @"SetIsTranslationEnabled\s*\(\s*(?:target|BrdPreviewOverlay)",
        RegexOptions.Compiled);

    private static readonly Regex InsertTranslationBag = new(
        @"Properties\.InsertVector3\s*\(\s*[""']Translation[""']",
        RegexOptions.Compiled);

    private static readonly Regex GalleryVisibilityWrite = new(
        @"GrdGalleryOverlay\.Visibility\s*=",
        RegexOptions.Compiled);

    private static readonly Regex GalleryClearValue = new(
        @"GrdGalleryOverlay\.ClearValue\s*\(\s*VisibilityProperty\s*\)",
        RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Palace.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Palace.csproj not found above the test output");
    }

    [Fact]
    public void PreviewPane_NoGetElementVisualOnOverlay_UsesElementStartAnimation()
    {
        var cs = File.ReadAllText(Path.Combine(RepoRoot(), "Pages", "LibraryPage.PreviewPane.cs"));
        Assert.False(
            GetElementVisualOnOverlay.IsMatch(cs),
            "Must not GetElementVisual(BrdPreviewOverlay) — use BrdPreviewOverlay.StartAnimation");
        Assert.False(SetTranslationEnabled.IsMatch(cs));
        Assert.False(InsertTranslationBag.IsMatch(cs));
        Assert.Contains("BrdPreviewOverlay.StartAnimation(", cs, StringComparison.Ordinal);
        Assert.Contains(
            "BrdPreviewOverlay.Translation = new System.Numerics.Vector3(0f, 0f, 32f)",
            cs,
            StringComparison.Ordinal);
        Assert.Contains("ProbeOverlayBoundsVsXamlRoot", cs, StringComparison.Ordinal);
        Assert.Contains("overWide=", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewPane_NoGalleryVisibilityWrites_EdgeCollapsedAndVisible()
    {
        var cs = File.ReadAllText(Path.Combine(RepoRoot(), "Pages", "LibraryPage.PreviewPane.cs"));
        Assert.False(
            GalleryVisibilityWrite.IsMatch(cs),
            "Must not write GrdGalleryOverlay.Visibility");
        Assert.False(
            GalleryClearValue.IsMatch(cs),
            "Must not ClearValue Gallery Visibility");
        Assert.Contains(
            "BtnPreviewPaneEdge.Visibility = Visibility.Collapsed",
            cs,
            StringComparison.Ordinal);
        Assert.Contains(
            "BtnPreviewPaneEdge.Visibility = Visibility.Visible",
            cs,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Xaml_OverlayFacadeTranslationZ32()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "Pages", "LibraryPage.xaml"));
        var overlayIdx = xaml.IndexOf("x:Name=\"BrdPreviewOverlay\"", StringComparison.Ordinal);
        Assert.True(overlayIdx >= 0);
        var slice = xaml.Substring(overlayIdx, Math.Min(900, xaml.Length - overlayIdx));
        Assert.Contains("Translation=\"0,0,32\"", slice, StringComparison.Ordinal);
    }
}
