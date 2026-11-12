using System.Xml.Linq;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Wimbly Preview overlay structure: sibling of GrdLibraryColumns (64b7ece tree),
/// AcrylicInApp on the shadow host, Close ✕ Top/Right, single body title.
/// </summary>
public sealed class PreviewOverlayMockupXamlTests
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

    private static XElement RequireNamed(XElement root, string name)
    {
        var el = root.Descendants().FirstOrDefault(e => Attr(e, "Name") == name);
        Assert.True(el is not null, $"missing x:Name={name}");
        return el!;
    }

    [Fact]
    public void BrdPreviewOverlay_IsSiblingOfGrdLibraryColumns_NotRowInside()
    {
        // fa5e598 re-parented overlay into GrdLibraryColumns Grid.Row=1 — Studio 0×0 body.
        // 64b7ece: sibling under the page root Grid; top margin from code.
        var root = LoadLibraryPage();
        var columns = RequireNamed(root, "GrdLibraryColumns");
        var overlay = RequireNamed(root, "BrdPreviewOverlay");
        Assert.False(
            overlay.Ancestors().Any(a => ReferenceEquals(a, columns)),
            "BrdPreviewOverlay must NOT be inside GrdLibraryColumns (sibling tree)");
        Assert.Null(Attr(overlay, "Row"));
        Assert.Null(Attr(overlay, "ColumnSpan"));
        Assert.Equal("Right", Attr(overlay, "HorizontalAlignment"));
        Assert.Equal("Stretch", Attr(overlay, "VerticalAlignment"));

        var edge = RequireNamed(root, "BtnPreviewPaneEdge");
        Assert.False(
            edge.Ancestors().Any(a => ReferenceEquals(a, columns)),
            "BtnPreviewPaneEdge must be a sibling of GrdLibraryColumns");
        Assert.Null(Attr(edge, "Row"));
    }

    [Fact]
    public void OverlayClose_IsTopRight_BodyOwnsSinglePreviewTitle()
    {
        var root = LoadLibraryPage();
        var overlay = RequireNamed(root, "BrdPreviewOverlay");
        var close = RequireNamed(overlay, "BtnPreviewOverlayClose");
        Assert.Equal("Right", Attr(close, "HorizontalAlignment"));
        Assert.Equal("Top", Attr(close, "VerticalAlignment"));
        Assert.Null(
            overlay.Descendants().FirstOrDefault(e => Attr(e, "Name") == "BtnPreviewPanePin"));
        Assert.Null(
            overlay.Descendants().FirstOrDefault(e => Attr(e, "Name") == "TxtPreviewOverlayTitle"));
        Assert.Null(
            root.Descendants().FirstOrDefault(e => Attr(e, "Name") == "TxtPreviewDockTitle"));

        var body = RequireNamed(root, "PnlPreviewBody");
        var previewCaptions = body.Elements()
            .Where(e => e.Name.LocalName == "TextBlock" && Attr(e, "Text") == "Preview")
            .ToList();
        Assert.True(
            previewCaptions.Count == 1,
            "PnlPreviewBody must own the single Preview title");
    }

    [Fact]
    public void OverlayPaintsAcrylicOnShadowHost_DockHostTransparent_NoCustomBrush()
    {
        var root = LoadLibraryPage();
        var overlay = RequireNamed(root, "BrdPreviewOverlay");
        Assert.Contains(
            "AcrylicInAppFillColorDefaultBrush",
            Attr(overlay, "Background") ?? "",
            StringComparison.Ordinal);
        Assert.Equal("1,0,0,0", Attr(overlay, "BorderThickness"));
        Assert.Equal("0,0,32", Attr(overlay, "Translation"));
        Assert.NotNull(overlay.Elements().FirstOrDefault(e => e.Name.LocalName == "Border.Shadow"));
        Assert.Null(
            root.Descendants().FirstOrDefault(e => Attr(e, "Name") == "BrdPreviewOverlayFill"));

        var dock = RequireNamed(root, "GrdPreviewDockHost");
        var dockBg = Attr(dock, "Background");
        Assert.True(
            string.IsNullOrEmpty(dockBg)
                || dockBg.Contains("Transparent", StringComparison.OrdinalIgnoreCase),
            "GrdPreviewDockHost must be Transparent (9c8f684 glass)");

        var appXaml = File.ReadAllText(Path.Combine(RepoRoot(), "App.xaml"));
        Assert.DoesNotContain("PalacePreviewPaneFillBrush", appXaml, StringComparison.Ordinal);
        Assert.Equal("AcrylicInAppFillColorDefaultBrush", ShellChrome.PreviewOverlayFill);
    }

    [Fact]
    public void OverlayTopMargin_ComesFromCode_NotGridRow()
    {
        var cs = File.ReadAllText(Path.Combine(RepoRoot(), "Pages", "LibraryPage.PreviewPane.cs"));
        Assert.Contains("OverlayTopMarginBelowToolbar", cs, StringComparison.Ordinal);
        Assert.Contains("ResolvePreviewOverlayTopMargin", cs, StringComparison.Ordinal);
        Assert.Contains("CloseOverlay", cs, StringComparison.Ordinal);
        Assert.Contains("EnsurePreviewOverlayTranslationResting", cs, StringComparison.Ordinal);
        Assert.Contains("BrdPreviewOverlay.StartAnimation(", cs, StringComparison.Ordinal);
        Assert.Contains(
            "BrdPreviewOverlay.Translation = new System.Numerics.Vector3(0f, 0f, 32f)",
            cs,
            StringComparison.Ordinal);

        Assert.Equal(64, PreviewPane.OverlayTopMarginBelowToolbar(56), 0.01);
        Assert.Equal(PreviewPane.OverlayTopFallbackDip, PreviewPane.OverlayTopMarginBelowToolbar(0), 0.01);
    }

    [Fact]
    public void EdgeTab_CodeHidesWhileOverlayOpen()
    {
        var root = LoadLibraryPage();
        var edge = RequireNamed(root, "BtnPreviewPaneEdge");
        Assert.Equal("0.35", Attr(edge, "Opacity"));

        var cs = File.ReadAllText(Path.Combine(RepoRoot(), "Pages", "LibraryPage.PreviewPane.cs"));
        Assert.Contains("BtnPreviewPaneEdge.Visibility = Visibility.Collapsed", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusAndRowHeight_StayInsideLibraryColumns()
    {
        var root = LoadLibraryPage();
        var columns = RequireNamed(root, "GrdLibraryColumns");
        var status = RequireNamed(columns, "TxtStatus");
        var rowSlider = RequireNamed(columns, "SldRowHeight");
        Assert.True(
            status.AncestorsAndSelf().Any(a => Attr(a, "Row") == "3"),
            "TxtStatus must stay in Grid.Row=3 inside GrdLibraryColumns");
        Assert.True(
            rowSlider.AncestorsAndSelf().Any(a => Attr(a, "Row") == "3"),
            "SldRowHeight must stay in the status row inside GrdLibraryColumns");
    }
}
