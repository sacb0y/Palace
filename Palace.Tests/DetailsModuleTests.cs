using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class DetailsModuleTests
{
    [Fact]
    public void ForKind_AlwaysIncludesFileAndTags()
    {
        foreach (AssetKind kind in Enum.GetValues<AssetKind>())
        {
            var modules = DetailsModuleCatalog.ForKind(kind);
            Assert.Contains(modules, m => m.Id == "file");
            Assert.Contains(modules, m => m.Id == "tags");
            Assert.Contains(modules, m => m.Id == "generation");
        }
    }

    [Fact]
    public void ForKind_ImageAndGif_GetStillModules()
    {
        var image = DetailsModuleCatalog.ForKind(AssetKind.Image);
        Assert.Contains(image, m => m.Kind == DetailsModuleKind.ColorHdr);
        Assert.Contains(image, m => m.Kind == DetailsModuleKind.Histogram);
        Assert.Contains(image, m => m.Kind == DetailsModuleKind.Gamut);
        Assert.Contains(DetailsModuleCatalog.ForKind(AssetKind.Gif), m => m.Kind == DetailsModuleKind.ColorHdr);
        Assert.DoesNotContain(DetailsModuleCatalog.ForKind(AssetKind.Video), m => m.Kind == DetailsModuleKind.ColorHdr);
        Assert.DoesNotContain(DetailsModuleCatalog.ForKind(AssetKind.Other), m => m.Kind == DetailsModuleKind.Histogram);
    }

    [Fact]
    public void ForKind_Video_GetsVideoModule()
    {
        Assert.Contains(DetailsModuleCatalog.ForKind(AssetKind.Video), m => m.Kind == DetailsModuleKind.Video);
        Assert.DoesNotContain(DetailsModuleCatalog.ForKind(AssetKind.Image), m => m.Kind == DetailsModuleKind.Video);
    }

    [Fact]
    public void Includes_AudioAndMesh_ReservedFalseUntilKindsExist()
    {
        Assert.False(DetailsModuleCatalog.Includes(DetailsModuleKind.Audio, AssetKind.Other));
        Assert.False(DetailsModuleCatalog.Includes(DetailsModuleKind.Mesh3D, AssetKind.Other));
        Assert.Equal("audio", DetailsModuleCatalog.Audio.Id);
        Assert.Equal("mesh3d", DetailsModuleCatalog.Mesh3D.Id);
    }

    [Fact]
    public void CollapseSettingsKey_UsesGalleryChromePrefix()
    {
        Assert.Equal("GalleryDetailsSection_file", DetailsModuleCatalog.File.CollapseSettingsKey);
        Assert.Equal(
            GalleryChrome.DetailsSectionKey("colorhdr"),
            DetailsModuleCatalog.ColorHdr.CollapseSettingsKey);
    }

    [Fact]
    public void Layout_LeftDock_DoesNotStealNextInset()
    {
        Assert.False(ViewerDetailsLayout.UsesBottomSheet(719));
        Assert.False(ViewerDetailsLayout.UsesBottomSheet(720));

        var closed = ViewerDetailsLayout.NextMargin(false, false, 270);
        Assert.Equal(16, closed.Right);

        var open = ViewerDetailsLayout.NextMargin(true, false, 270);
        Assert.Equal(16, open.Right);

        Assert.Equal(270, ViewerDetailsLayout.PanelWidth);
        Assert.Equal(720 * ViewerDetailsLayout.MaxHeightFraction, ViewerDetailsLayout.PanelMaxHeight(720), 3);
        // Under a tall toolbar the fit-under-toolbar cap wins over 70%.
        Assert.Equal(
            720 - 200 - ViewerDetailsLayout.Inset,
            ViewerDetailsLayout.PanelMaxHeight(720, topInset: 200),
            3);
        Assert.Equal(56 + 8, ViewerDetailsLayout.TopBelowToolbar(56));
        Assert.Equal(ViewerDetailsLayout.TopInset, ViewerDetailsLayout.TopBelowToolbar(0));
        Assert.Equal(
            720 - ViewerDetailsLayout.Inset - ViewerDetailsLayout.ToolbarGap,
            ViewerDetailsLayout.InFlowPanelMaxHeight(720),
            3);
        Assert.Equal("Details for shot.png", ViewerDetailsLayout.LandmarkName("shot.png"));
    }

    [Fact]
    public void FocusCycle_SkipsDetailsWhenClosed_AndGatesArrows()
    {
        Assert.Equal(ViewerFocusZone.Controls, ViewerFocusCycle.Next(ViewerFocusZone.Image, false));
        Assert.Equal(ViewerFocusZone.Image, ViewerFocusCycle.Next(ViewerFocusZone.Controls, false));
        Assert.Equal(ViewerFocusZone.Details, ViewerFocusCycle.Next(ViewerFocusZone.Controls, true));
        Assert.Equal(ViewerFocusZone.Image, ViewerFocusCycle.Next(ViewerFocusZone.Details, true));
        Assert.True(ViewerFocusCycle.AllowsArrowNavigate(ViewerFocusZone.Image));
        Assert.False(ViewerFocusCycle.AllowsArrowNavigate(ViewerFocusZone.Details));
        Assert.False(ViewerFocusCycle.AllowsArrowNavigate(ViewerFocusZone.Controls));
    }
}
