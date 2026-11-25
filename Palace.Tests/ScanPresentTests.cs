using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class ScanPresentTests
{
    public ScanPresentTests()
    {
        LibrarySettings.Apply(
            LibrarySettings.DefaultWatchForChanges,
            LibrarySettings.DefaultIndexImages,
            LibrarySettings.DefaultIndexVideo,
            LibrarySettings.DefaultGenerateThumbsOnScan,
            LibrarySettings.DefaultIndexModel3D);
    }

    [Theory]
    [InlineData(".glb")]
    [InlineData(".gltf")]
    [InlineData(".fbx")]
    [InlineData(".mp4")]
    [InlineData("hero.glb")]
    public void Catalog_exts_are_retained_when_not_indexed(string pathOrExt) =>
        Assert.True(ScanPresent.RetainWhenNotIndexed(pathOrExt));

    [Fact]
    public void Present_glb_and_mp4_are_not_orphaned_when_index_toggles_off()
    {
        LibrarySettings.Apply(
            watchForChanges: true,
            indexImages: true,
            indexVideo: false,
            generateThumbsOnScan: true,
            indexModel3D: false);
        try
        {
            Assert.False(LibrarySettings.IsIndexedExt(".glb"));
            Assert.False(LibrarySettings.IsIndexedExt(".mp4"));
            Assert.True(ScanPresent.RetainWhenNotIndexed(".glb"));
            Assert.True(ScanPresent.RetainWhenNotIndexed(".mp4"));
            Assert.Equal(AssetKind.Model3D, PathSafe.KindFromExt(".glb"));
            Assert.Equal(AssetKind.Video, PathSafe.KindFromExt(".mp4"));
        }
        finally
        {
            LibrarySettings.Apply(
                LibrarySettings.DefaultWatchForChanges,
                LibrarySettings.DefaultIndexImages,
                LibrarySettings.DefaultIndexVideo,
                LibrarySettings.DefaultGenerateThumbsOnScan,
                LibrarySettings.DefaultIndexModel3D);
        }
    }

    [Fact]
    public void Non_catalog_ext_is_not_retained() =>
        Assert.False(ScanPresent.RetainWhenNotIndexed(".txt"));
}
