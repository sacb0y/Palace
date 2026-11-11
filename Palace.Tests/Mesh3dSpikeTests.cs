using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class Mesh3dSpikeTests
{
    [Fact]
    public void ParseEnabled_AcceptsCommonTruthyValues()
    {
        Assert.True(Mesh3dSpike.ParseEnabled("1"));
        Assert.True(Mesh3dSpike.ParseEnabled("true"));
        Assert.True(Mesh3dSpike.ParseEnabled("TRUE"));
        Assert.True(Mesh3dSpike.ParseEnabled("yes"));
        Assert.False(Mesh3dSpike.ParseEnabled("0"));
        Assert.False(Mesh3dSpike.ParseEnabled(""));
        Assert.False(Mesh3dSpike.ParseEnabled(null));
        Assert.False(Mesh3dSpike.ParseEnabled("maybe"));
    }

    [Fact]
    public void ShowsDevSurface_FollowsOverride()
    {
        var prior = Mesh3dSpike.EnabledOverride;
        try
        {
            Mesh3dSpike.EnabledOverride = false;
            Assert.False(Mesh3dSpike.ShowsDevSurface);
            Mesh3dSpike.EnabledOverride = true;
            Assert.True(Mesh3dSpike.ShowsDevSurface);
        }
        finally
        {
            Mesh3dSpike.EnabledOverride = prior;
        }
    }

    [Fact]
    public void DefaultSample_IsPackagedSpikeBox()
    {
        Assert.Equal("Assets/Mesh3dSpike/SpikeBox.gltf", Mesh3dSpike.DefaultSampleRelativePath);
        Assert.Equal("PALACE_MESH3D_SPIKE", Mesh3dSpike.EnvVar);
    }
}

public sealed class Mesh3dGltfTests
{
    [Fact]
    public void IsGltfPath_AcceptsGltfAndGlb()
    {
        Assert.True(Mesh3dGltf.IsGltfPath(@"D:\a\model.gltf"));
        Assert.True(Mesh3dGltf.IsGltfPath(@"D:\a\model.GLB"));
        Assert.False(Mesh3dGltf.IsGltfPath(@"D:\a\model.fbx"));
        Assert.False(Mesh3dGltf.IsGltfPath(""));
        Assert.False(Mesh3dGltf.IsGltfPath(null));
    }

    [Fact]
    public void ResolveLoadPath_PrefersExistingPreferred()
    {
        var dir = Path.Combine(Path.GetTempPath(), "palace-mesh3d-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var preferred = Path.Combine(dir, "pref.glb");
            File.WriteAllText(preferred, "glb");
            var resolved = Mesh3dGltf.ResolveLoadPath(preferred, dir);
            Assert.Equal(Path.GetFullPath(preferred), resolved);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveLoadPath_FallsBackToPackagedSample()
    {
        var dir = Path.Combine(Path.GetTempPath(), "palace-mesh3d-" + Guid.NewGuid().ToString("N"));
        var sampleDir = Path.Combine(dir, "Assets", "Mesh3dSpike");
        Directory.CreateDirectory(sampleDir);
        try
        {
            var sample = Path.Combine(sampleDir, "SpikeBox.gltf");
            File.WriteAllText(sample, "{}");
            var resolved = Mesh3dGltf.ResolveLoadPath(null, dir);
            Assert.Equal(Path.GetFullPath(sample), resolved);
            Assert.Equal("SpikeBox.gltf", Mesh3dGltf.DescribeLoad(resolved));
            Assert.Equal("no glTF (sample missing)", Mesh3dGltf.DescribeLoad(null));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public sealed class Mesh3dStreamlineProbeTests
{
    [Fact]
    public void FromPointers_ReadyOnlyWhenBothNonZero()
    {
        var ok = Mesh3dStreamlineProbe.FromPointers(0x1, 0x2);
        Assert.True(ok.ReadyForStreamlineHooks);
        Assert.Contains("obtained", ok.StatusLine, StringComparison.OrdinalIgnoreCase);

        var deviceOnly = Mesh3dStreamlineProbe.FromPointers(0x1, 0);
        Assert.False(deviceOnly.ReadyForStreamlineHooks);
        Assert.Contains("command list", deviceOnly.Detail, StringComparison.OrdinalIgnoreCase);

        var blocked = Mesh3dStreamlineProbe.Unavailable("missing dll");
        Assert.False(blocked.ReadyForStreamlineHooks);
        Assert.StartsWith("Streamline probe blocked", blocked.StatusLine, StringComparison.Ordinal);
    }
}
