using System.Xml.Linq;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Gooey / Isiac Preview overlay mockup fidelity (LibraryPage.xaml).
/// Positioning PASS alone is not enough — chrome must match.
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
    public void BrdPreviewOverlay_LivesInContentRow_BelowToolbar_NotFullPage()
    {
        var root = LoadLibraryPage();
        var overlay = RequireNamed(root, "BrdPreviewOverlay");
        Assert.Equal("1", Attr(overlay, "Row"));
        Assert.Equal("3", Attr(overlay, "ColumnSpan"));
        Assert.Equal("Right", Attr(overlay, "HorizontalAlignment"));
        Assert.Equal("Stretch", Attr(overlay, "VerticalAlignment"));

        var columns = RequireNamed(root, "GrdLibraryColumns");
        Assert.True(
            overlay.Ancestors().Any(a => ReferenceEquals(a, columns)),
            "BrdPreviewOverlay must be a descendant of GrdLibraryColumns");
    }

    [Fact]
    public void OverlayHeader_IsTitlePlusClose_BodyHasNoPreviewCaption()
    {
        var root = LoadLibraryPage();
        var overlay = RequireNamed(root, "BrdPreviewOverlay");
        Assert.NotNull(RequireNamed(overlay, "TxtPreviewOverlayTitle"));
        Assert.NotNull(RequireNamed(overlay, "BtnPreviewOverlayClose"));
        Assert.Null(
            overlay.Descendants().FirstOrDefault(e => Attr(e, "Name") == "BtnPreviewPanePin"));

        var body = RequireNamed(root, "PnlPreviewBody");
        var previewCaptions = body.Descendants()
            .Where(e => e.Name.LocalName == "TextBlock" && Attr(e, "Text") == "Preview")
            .ToList();
        Assert.True(
            previewCaptions.Count == 0,
            "PnlPreviewBody must not duplicate the Preview caption strip");
    }

    [Fact]
    public void OverlayAndDock_UseStrongAcrylic_NotOpaqueBase()
    {
        var root = LoadLibraryPage();
        var overlay = RequireNamed(root, "BrdPreviewOverlay");
        Assert.Contains("PalacePreviewPaneFillBrush", Attr(overlay, "Background") ?? "", StringComparison.Ordinal);
        Assert.Equal("1,0,0,0", Attr(overlay, "BorderThickness"));

        var dock = RequireNamed(root, "GrdPreviewDockHost");
        Assert.Contains("PalacePreviewPaneFillBrush", Attr(dock, "Background") ?? "", StringComparison.Ordinal);

        var appXaml = File.ReadAllText(Path.Combine(RepoRoot(), "App.xaml"));
        Assert.Contains("x:Key=\"PalacePreviewPaneFillBrush\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("TintOpacity=\"0.55\"", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void EdgeTab_IsInContentRow_CodeHidesWhileOverlayOpen()
    {
        var root = LoadLibraryPage();
        var edge = RequireNamed(root, "BtnPreviewPaneEdge");
        Assert.Equal("1", Attr(edge, "Row"));
        Assert.Equal("0.35", Attr(edge, "Opacity"));

        var cs = File.ReadAllText(Path.Combine(RepoRoot(), "Pages", "LibraryPage.PreviewPane.cs"));
        Assert.Contains("BtnPreviewPaneEdge.Visibility = Visibility.Collapsed", cs, StringComparison.Ordinal);
        Assert.Contains("CloseOverlay", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusAndRowHeight_StayOutsideOverlayRow()
    {
        var root = LoadLibraryPage();
        var columns = RequireNamed(root, "GrdLibraryColumns");
        var status = RequireNamed(columns, "TxtStatus");
        var rowSlider = RequireNamed(columns, "SldRowHeight");
        Assert.True(
            status.AncestorsAndSelf().Any(a => Attr(a, "Row") == "3"),
            "TxtStatus must stay in Grid.Row=3 below the overlay band");
        Assert.True(
            rowSlider.AncestorsAndSelf().Any(a => Attr(a, "Row") == "3"),
            "SldRowHeight must stay in the status row below the overlay band");
    }
}
