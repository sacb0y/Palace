using Palace.Helpers;
using Palace.Mesh3d;
using Xunit;

namespace Palace.Tests;

public sealed class Mesh3dSeamTests
{
    [Fact]
    public async Task RecordingMeshRenderer_records_load_play_resize_dispose()
    {
        await using var renderer = new RecordingMeshRenderer();
        var scene = new SceneDesc
        {
            RootPath = "/tmp/hero.glb",
            Character = new Character { Id = "c1", Name = "Hero" }
        };
        await renderer.LoadAsync(scene);
        renderer.SetVisibility("body", true);
        renderer.Play(new ClipRef { Id = "idle", Name = "Idle", DurationSeconds = 1 });
        renderer.Resize(1280, 720, 1.5f);
        await renderer.DisposeAsync();

        Assert.Equal("/tmp/hero.glb", renderer.LastScene?.RootPath);
        Assert.Equal("idle", renderer.LastClip?.Id);
        Assert.Equal((1280, 720, 1.5f), renderer.LastSize);
        Assert.Contains("Dispose", renderer.Log);
    }

    [Fact]
    public void MeshMetadata_round_trips_json()
    {
        var meta = new MeshMetadata
        {
            TriangleCount = 1200,
            BoneCount = 45,
            ClipCount = 3,
            Dependencies = ["hero.bin", "tex.png"]
        };
        var parsed = MeshMetadata.TryParse(meta.ToJson());
        Assert.NotNull(parsed);
        Assert.Equal(1200, parsed!.TriangleCount);
        Assert.Equal(2, parsed.Dependencies!.Count);
    }

    [Fact]
    public void EmptyAssetDependencies_returns_none() =>
        Assert.Empty(EmptyAssetDependencies.Instance.Resolve("a.gltf"));
}
