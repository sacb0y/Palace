using System.Xml.Linq;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Guards the swipe hit-test contract: Manipulation must stay on
/// <c>GrdStillContent</c> (on-image pages). <c>ScrStill</c> must stay
/// <c>ManipulationMode=None</c> — Translate on the ScrollViewer made
/// on-image swipes look like taps (chrome toggle, no page).
/// </summary>
public sealed class GalleryStillSurfaceXamlTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Palace.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Palace.csproj not found above the test output");
    }

    private static string? Attr(XElement e, string localName) =>
        e.Attributes().FirstOrDefault(a => a.Name.LocalName == localName)?.Value;

    [Fact]
    public void SwipeStartingOnImage_UsesGrdStillContentManipulation()
    {
        var path = Path.Combine(RepoRoot(), "Controls", "GalleryStillSurface.xaml");
        Assert.True(File.Exists(path), path);
        var doc = XDocument.Load(path);

        var content = doc.Descendants()
            .FirstOrDefault(e => Attr(e, "Name") == "GrdStillContent");
        Assert.NotNull(content);
        Assert.Equal(
            "TranslateX,TranslateY,Scale",
            Attr(content!, "ManipulationMode"),
            StringComparer.Ordinal);
        Assert.False(
            string.IsNullOrWhiteSpace(Attr(content!, "ManipulationDelta")),
            "GrdStillContent must own ManipulationDelta so swipe-on-image pages");
        Assert.Equal("Transparent", Attr(content!, "Background"), StringComparer.Ordinal);

        var scr = doc.Descendants()
            .FirstOrDefault(e => Attr(e, "Name") == "ScrStill");
        Assert.NotNull(scr);
        Assert.Equal("None", Attr(scr!, "ManipulationMode"), StringComparer.Ordinal);
    }

    [Fact]
    public void FitSwipePanel_FillsViewport_ImageLetterboxedInside()
    {
        // Fit at 1×: panel = viewport so letterbox hits the same surface as the image.
        Assert.True(GalleryScale.FitSwipeUsesViewportPanel(scrolls: false));
        Assert.False(GalleryScale.FitSwipeUsesViewportPanel(scrolls: true));

        const double panelW = 800;
        const double panelH = 600;
        var image = GalleryScale.FitContainDipSize(100, 200, panelW, panelH);
        Assert.True(image.Width < panelW);
        Assert.Equal(600, image.Height, 3);
    }

    [Fact]
    public void FillPortrait_NoHorizontalPan_PagesImmediately()
    {
        // Tall cover: width matches viewport, height overflows — no H pan room.
        var fill = GalleryScale.FillCoverDipSize(100, 200, 400, 200);
        Assert.Equal(400, fill.Width, 3);
        Assert.True(fill.Height > 200);
        var maxH = TouchGestures.MaxScrollOffset(fill.Width, 400);
        Assert.True(TouchGestures.NoHorizontalPanRoom(maxH));
        Assert.True(TouchGestures.AllowsSwipeHandoff(
            zoomed: true,
            accumulatedOutwardPx: 0,
            noHorizontalPanRoom: true));
    }

    [Fact]
    public void ToolbarHost_SwallowsGapTapsAndHolds()
    {
        foreach (var relative in new[]
                 {
                     Path.Combine("Pages", "LibraryPage.xaml"),
                     Path.Combine("GalleryWindow.xaml"),
                 })
        {
            var path = Path.Combine(RepoRoot(), relative);
            Assert.True(File.Exists(path), path);
            var doc = XDocument.Load(path);
            var toolbar = doc.Descendants()
                .FirstOrDefault(e => Attr(e, "Name") == "GrdViewerToolbar");
            Assert.NotNull(toolbar);
            Assert.Equal("Transparent", Attr(toolbar!, "Background"), StringComparer.Ordinal);
            Assert.False(
                string.IsNullOrWhiteSpace(Attr(toolbar!, "Holding")),
                relative + ": toolbar Holding must swallow gap long-press");
        }
    }
}
