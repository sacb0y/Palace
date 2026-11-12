using System.Xml.Linq;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Library mosaic <c>BtnAssetMore</c> idle opacity gate (Oct 21 ⋮).
/// </summary>
public sealed class TileMoreRevealXamlTests
{
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

    private static string? Attr(XElement e, string localName) =>
        e.Attributes().FirstOrDefault(a =>
            a.Name.LocalName == localName
            || a.Name.LocalName.EndsWith("." + localName, StringComparison.Ordinal))?.Value;

    private static XElement LoadLibraryPage()
    {
        var path = Path.Combine(RepoRoot(), "Pages", "LibraryPage.xaml");
        Assert.True(File.Exists(path), path);
        return XDocument.Load(path).Root
            ?? throw new InvalidOperationException("LibraryPage.xaml has no root");
    }

    [Fact]
    public void BtnAssetMore_DefaultsHidden_CodeDrivenReveal()
    {
        var root = LoadLibraryPage();
        var button = root.Descendants()
            .FirstOrDefault(e => Attr(e, "AutomationId") == "BtnAssetMore"
                || Attr(e, "Name") == "BtnAssetMore");
        Assert.NotNull(button);
        Assert.Equal("0", Attr(button!, "Opacity"));
        Assert.Equal("False", Attr(button!, "IsHitTestVisible"));
        Assert.Equal("False", Attr(button!, "IsTabStop"));
        Assert.Equal("BtnAssetMore_Loaded", Attr(button!, "Loaded"));
        Assert.Equal("BtnAssetMore_Unloaded", Attr(button!, "Unloaded"));

        var tile = button!.Ancestors()
            .FirstOrDefault(e => Attr(e, "Name") == "GrdAssetTile");
        Assert.NotNull(tile);

        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "Pages", "LibraryPage.xaml"));
        Assert.DoesNotContain("MoreRevealStates", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MoreHidden", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MoreShown", xaml, StringComparison.Ordinal);

        var cs = File.ReadAllText(Path.Combine(RepoRoot(), "Pages", "LibraryPage.xaml.cs"));
        Assert.Contains("TileMoreChrome.ShouldShow", cs, StringComparison.Ordinal);
        Assert.Contains("ApplyRevealVisual", cs, StringComparison.Ordinal);
        Assert.Contains("TileMoreChrome.Opacity", cs, StringComparison.Ordinal);
        Assert.Contains("BottomEdgeAlignedRight", cs, StringComparison.Ordinal);
        Assert.Contains("FlyoutShowOptions", cs, StringComparison.Ordinal);
        Assert.Contains("PointerCanceled", cs, StringComparison.Ordinal);
        Assert.Contains("PointerCaptureLost", cs, StringComparison.Ordinal);
        Assert.Contains("_pointerOver = false", cs, StringComparison.Ordinal);
        Assert.Contains("OnDataContextChanged", cs, StringComparison.Ordinal);
        Assert.DoesNotContain(".IsPointerOver", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewEdgeChevron_IsNotBtnAssetMore()
    {
        // P-139 / #193 faint edge chevron is BtnPreviewPaneEdge, not mosaic ⋮.
        var root = LoadLibraryPage();
        var edge = root.Descendants()
            .FirstOrDefault(e => Attr(e, "Name") == "BtnPreviewPaneEdge"
                || Attr(e, "AutomationId") == "BtnPreviewPaneEdge");
        Assert.NotNull(edge);
        Assert.NotEqual(
            "BtnAssetMore",
            Attr(edge!, "AutomationId") ?? Attr(edge!, "Name"));
    }
}
