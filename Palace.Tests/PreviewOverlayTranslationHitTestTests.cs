using System.Text.RegularExpressions;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Wimbly: Preview overlay must use facade <c>UIElement.Translation</c> only.
/// <c>SetIsTranslationEnabled</c> + <c>InsertVector3("Translation")</c> desyncs
/// hit-test from painted pixels (Isiac: overlay not interactable).
/// </summary>
public sealed class PreviewOverlayTranslationHitTestTests
{
    private static readonly Regex SetTranslationEnabled = new(
        @"SetIsTranslationEnabled\s*\(\s*(?:target|BrdPreviewOverlay)",
        RegexOptions.Compiled);

    private static readonly Regex InsertTranslationBag = new(
        @"Properties\.InsertVector3\s*\(\s*[""']Translation[""']",
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
    public void PreviewPane_UsesFacadeTranslation_NoCompositionPropertyBag()
    {
        var path = Path.Combine(RepoRoot(), "Pages", "LibraryPage.PreviewPane.cs");
        var cs = File.ReadAllText(path);
        Assert.False(
            SetTranslationEnabled.IsMatch(cs),
            "BrdPreviewOverlay must not call SetIsTranslationEnabled (facade Translation only)");
        Assert.False(
            InsertTranslationBag.IsMatch(cs),
            "BrdPreviewOverlay must not InsertVector3(\"Translation\") on the property bag");
        Assert.Contains("StartAnimation(\"Translation\"", cs, StringComparison.Ordinal);
        Assert.Contains("anim.Target = \"Translation\"", cs, StringComparison.Ordinal);
        Assert.Contains("BrdPreviewOverlay.Translation =", cs, StringComparison.Ordinal);
        Assert.Contains("new System.Numerics.Vector3(0f, 0f, 32f)", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayOpen_CollapsesEdgeAndGallery_ForHitTest()
    {
        var cs = File.ReadAllText(Path.Combine(RepoRoot(), "Pages", "LibraryPage.PreviewPane.cs"));
        Assert.Contains("BtnPreviewPaneEdge.Visibility = Visibility.Collapsed", cs, StringComparison.Ordinal);
        Assert.Contains("GrdGalleryOverlay.Visibility = Visibility.Collapsed", cs, StringComparison.Ordinal);
        Assert.Contains("BrdPreviewOverlay.IsHitTestVisible = true", cs, StringComparison.Ordinal);
        Assert.Contains("GrdGalleryOverlay.ClearValue(VisibilityProperty)", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void Xaml_OverlayKeepsFacadeTranslationZ32()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "Pages", "LibraryPage.xaml"));
        var overlayIdx = xaml.IndexOf("x:Name=\"BrdPreviewOverlay\"", StringComparison.Ordinal);
        Assert.True(overlayIdx >= 0);
        var slice = xaml.Substring(overlayIdx, Math.Min(800, xaml.Length - overlayIdx));
        Assert.Contains("Translation=\"0,0,32\"", slice, StringComparison.Ordinal);
    }
}
