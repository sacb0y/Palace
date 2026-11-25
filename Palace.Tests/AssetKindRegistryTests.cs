using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class AssetKindRegistryTests
{
    [Fact]
    public void Every_AssetKind_has_a_registry_entry()
    {
        foreach (AssetKind kind in Enum.GetValues<AssetKind>())
        {
            var info = AssetKindRegistry.Get(kind);
            Assert.Equal(kind, info.Kind);
            Assert.False(string.IsNullOrWhiteSpace(info.Icon));
            Assert.NotNull(info.Extensions);
            Assert.NotNull(info.IndexEnabled);
            _ = info.IndexEnabled();
            _ = AssetKindRegistry.Icon(kind);
            _ = AssetKindRegistry.Label(kind);
            _ = AssetKindRegistry.BadgeId(kind);
            _ = AssetKindRegistry.DefaultAspect(kind);
            _ = AssetKindRegistry.DefaultSurface(kind);
            _ = AssetKindRegistry.ShowKindBadge(false, false, kind);
        }
    }

    [Theory]
    [InlineData(".glb", AssetKind.Model3D)]
    [InlineData(".gltf", AssetKind.Model3D)]
    [InlineData(".fbx", AssetKind.Model3D)]
    [InlineData(".png", AssetKind.Image)]
    [InlineData(".gif", AssetKind.Gif)]
    [InlineData(".mp4", AssetKind.Video)]
    [InlineData(".txt", AssetKind.Other)]
    public void FromExtension_maps_known_kinds(string ext, AssetKind kind) =>
        Assert.Equal(kind, AssetKindRegistry.FromExtension(ext));

    [Fact]
    public void Model3D_is_catalog_ext_and_default_off()
    {
        Assert.True(PathSafe.IsCatalogExt(".glb"));
        Assert.Equal(AssetKind.Model3D, PathSafe.KindFromExt(".fbx"));
        LibrarySettings.Apply(true, true, true, true, indexModel3D: false);
        Assert.False(LibrarySettings.IsIndexedExt(".glb"));
        LibrarySettings.Apply(true, true, true, true, indexModel3D: true);
        Assert.True(LibrarySettings.IsIndexedExt("hero.gltf"));
    }

    [Fact]
    public void ResolveSurface_keeps_mesh_out_of_still()
    {
        Assert.Equal(
            GallerySurface.Mesh,
            AssetKindRegistry.ResolveSurface(AssetKind.Model3D, playableVideo: false, hasStillOrPreview: true));
        Assert.Equal(
            GallerySurface.Still,
            AssetKindRegistry.ResolvePreviewSurface(AssetKind.Model3D, multi: false, cloudish: false, hasPreviewPath: true));
        Assert.Equal(
            GallerySurface.Video,
            AssetKindRegistry.ResolveSurface(AssetKind.Video, playableVideo: true, hasStillOrPreview: false));
    }
}
