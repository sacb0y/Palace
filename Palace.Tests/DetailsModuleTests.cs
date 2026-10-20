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
    public void ForKind_ImageAndGif_GetImageModule()
    {
        Assert.Contains(DetailsModuleCatalog.ForKind(AssetKind.Image), m => m.Kind == DetailsModuleKind.Image);
        Assert.Contains(DetailsModuleCatalog.ForKind(AssetKind.Gif), m => m.Kind == DetailsModuleKind.Image);
        Assert.DoesNotContain(DetailsModuleCatalog.ForKind(AssetKind.Video), m => m.Kind == DetailsModuleKind.Image);
        Assert.DoesNotContain(DetailsModuleCatalog.ForKind(AssetKind.Other), m => m.Kind == DetailsModuleKind.Image);
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
            GalleryChrome.DetailsSectionKey("image"),
            DetailsModuleCatalog.Image.CollapseSettingsKey);
    }

    [Fact]
    public void Layout_NarrowUsesBottomSheet_AndNextMargin()
    {
        Assert.True(ViewerDetailsLayout.UsesBottomSheet(719));
        Assert.False(ViewerDetailsLayout.UsesBottomSheet(720));
        Assert.False(ViewerDetailsLayout.UsesBottomSheet(0));

        var closed = ViewerDetailsLayout.NextMargin(false, false, 340);
        Assert.Equal(16, closed.Right);

        var open = ViewerDetailsLayout.NextMargin(true, false, 340);
        Assert.Equal(16 + 340 + 16, open.Right);

        var sheet = ViewerDetailsLayout.NextMargin(true, true, 700);
        Assert.Equal(16, sheet.Right);

        Assert.Equal(360, ViewerDetailsLayout.BottomSheetMaxHeight(720));
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
