using System.IO.Compression;
using Palace.Helpers;
using Palace.Imaging;
using Xunit;

namespace Palace.Tests;

public sealed class SmoothPresenterLifecycleTests
{
    [Fact]
    public void PhysicalSize_UsesDpiAndFloorsAtTwo()
    {
        Assert.Equal((1920u, 1080u), SmoothPresenterLifecycle.PhysicalSize(1920, 1080, 1f, 1f));
        Assert.Equal((2880u, 1620u), SmoothPresenterLifecycle.PhysicalSize(1920, 1080, 1.5f, 1.5f));
        Assert.Equal((2u, 2u), SmoothPresenterLifecycle.PhysicalSize(0, 0, 1f, 1f));
        Assert.Equal((100u, 100u), SmoothPresenterLifecycle.PhysicalSize(100, 100, 0f, -1f));
    }

    [Fact]
    public void Lifecycle_ResizeDpiDeviceLost_Recreate()
    {
        var life = new SmoothPresenterLifecycle();
        Assert.Equal(SmoothPresenterPhase.Idle, life.Phase);

        life.MarkCreated();
        Assert.Equal(SmoothPresenterPhase.Created, life.Phase);
        Assert.Equal(1, life.RecreateCount);

        life.MarkAttached(800, 600);
        Assert.Equal(SmoothPresenterPhase.Attached, life.Phase);
        Assert.Equal(800u, life.Width);
        Assert.Equal(600u, life.Height);

        Assert.True(life.TryResize(1024, 768));
        Assert.Equal(0, life.ResizeCount);
        Assert.Equal(800u, life.Width);
        life.CommitResize(1024, 768);
        Assert.Equal(1, life.ResizeCount);
        Assert.Equal(1024u, life.Width);
        Assert.False(life.TryResize(1024, 768));

        Assert.True(life.TrySetDpi(1.25f, 1.25f));
        Assert.Equal(1, life.DpiChangeCount);
        Assert.False(life.TrySetDpi(1.25f, 1.25f));

        life.MarkRunning();
        Assert.Equal(SmoothPresenterPhase.Running, life.Phase);

        life.MarkDeviceLost();
        Assert.Equal(SmoothPresenterPhase.DeviceLost, life.Phase);
        Assert.Equal(1, life.DeviceLostCount);
        Assert.True(life.NeedsRecreate);

        life.BeginRecreate();
        Assert.Equal(SmoothPresenterPhase.Created, life.Phase);
        Assert.Equal(2, life.RecreateCount);

        life.MarkAttached(640, 480);
        life.MarkDisposed();
        Assert.Equal(SmoothPresenterPhase.Disposed, life.Phase);
        Assert.Throws<ObjectDisposedException>(() => life.MarkCreated());
    }

    [Fact]
    public void FailedResize_DoesNotCommitSize()
    {
        var life = new SmoothPresenterLifecycle();
        life.MarkCreated();
        life.MarkAttached(800, 600);

        Assert.True(life.TryResize(1920, 1080));
        // Simulate native ResizeBuffers failure: never CommitResize.
        Assert.Equal(800u, life.Width);
        Assert.Equal(600u, life.Height);
        Assert.Equal(0, life.ResizeCount);

        // Same target still needed — retry possible.
        Assert.True(life.TryResize(1920, 1080));
        life.CommitResize(1920, 1080);
        Assert.Equal(1920u, life.Width);
        Assert.Equal(1080u, life.Height);
        Assert.Equal(1, life.ResizeCount);
        Assert.False(life.TryResize(1920, 1080));
    }

    [Fact]
    public void Attach_RejectedFromIdle()
    {
        var life = new SmoothPresenterLifecycle();
        Assert.Throws<InvalidOperationException>(() => life.MarkAttached(10, 10));
    }
}

public sealed class SmoothPresenterLogTests
{
    [Fact]
    public void FormatLine_IncludesFrameTimingFields()
    {
        var stats = new SmoothPresenterNative.FrameStats
        {
            WaitMs = 1.5,
            RenderMs = 2.25,
            PresentMs = 0.5,
            TotalMs = 4.25,
            FrameIndex = 7,
            Width = 1280,
            Height = 720,
            Backend = (int)SmoothPresenterNative.Backend.D3D12
        };
        var line = SmoothPresenterLog.FormatLine(stats, "ok");
        Assert.Contains("frame=7", line, StringComparison.Ordinal);
        Assert.Contains("d3d12", line, StringComparison.Ordinal);
        Assert.Contains("1280x720", line, StringComparison.Ordinal);
        Assert.Contains("wait_ms=1.500", line, StringComparison.Ordinal);
        Assert.Contains("render_ms=2.250", line, StringComparison.Ordinal);
        Assert.Contains("present_ms=0.500", line, StringComparison.Ordinal);
        Assert.Contains("total_ms=4.250", line, StringComparison.Ordinal);
        Assert.Contains("note=ok", line, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatImageStatsNote_StableFieldOrderForStudioScrape()
    {
        var stats = new ImageStats(
            P50: 0.418761f,
            P99995: 0.727577f,
            Max: 0.73233f,
            MaxRgb: 0.775478f,
            Lc: 1f,
            RefWhite: 1f,
            MetadataPeak: null,
            SourceClipped: false,
            SourceClipRatio: 0f);
        var note = SmoothPresenterLog.FormatImageStatsNote(stats);
        Assert.StartsWith("ImageStats P50=", note, StringComparison.Ordinal);
        Assert.Contains(" P99995=", note, StringComparison.Ordinal);
        Assert.Contains(" Max=", note, StringComparison.Ordinal);
        Assert.Contains(" MaxRgb=", note, StringComparison.Ordinal);
        Assert.Contains(" Lc=", note, StringComparison.Ordinal);
        Assert.Contains(" RefWhite=", note, StringComparison.Ordinal);
        Assert.Contains(" SourceClipped=False", note, StringComparison.Ordinal);
        Assert.Contains(" SourceClipRatio=", note, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', note);
    }

    [Fact]
    public void ResolvePath_PrefersEnvThenDataDir()
    {
        var prior = Environment.GetEnvironmentVariable(SmoothPresenterLog.EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(SmoothPresenterLog.EnvVar, @"D:\logs\frames.log");
            Assert.Equal(@"D:\logs\frames.log", SmoothPresenterLog.ResolvePath(@"C:\data"));

            Environment.SetEnvironmentVariable(SmoothPresenterLog.EnvVar, null);
            var path = SmoothPresenterLog.ResolvePath(Path.Combine(Path.GetTempPath(), "palace-smooth-test"));
            Assert.EndsWith(SmoothPresenterLog.DefaultFileName, path, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SmoothPresenterLog.EnvVar, prior);
        }
    }

    [Fact]
    public void Append_WritesRealLineToFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "palace-smooth-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var stats = new SmoothPresenterNative.FrameStats
            {
                FrameIndex = 1,
                Width = 64,
                Height = 64,
                TotalMs = 8.0,
                Backend = (int)SmoothPresenterNative.Backend.D3D11
            };
            SmoothPresenterLog.AppendStats(dir, stats, "unit");
            var path = SmoothPresenterLog.ResolvePath(dir);
            Assert.True(File.Exists(path));
            var text = File.ReadAllText(path);
            Assert.Contains("frame=1", text, StringComparison.Ordinal);
            Assert.Contains("d3d11", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public sealed class SmoothPresenterPackagingTests
{
    [Fact]
    public void Csproj_PackagesPalaceSmoothDll()
    {
        var root = FindRepoRoot();
        var csproj = File.ReadAllText(Path.Combine(root, "Palace.csproj"));
        Assert.True(SmoothPresenterPackaging.CsprojPackagesDll(csproj));
        Assert.EndsWith(
            SmoothPresenterPackaging.DllFileName,
            SmoothPresenterPackaging.ExpectedNativePath(root),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            Path.Combine("Native", "smooth", "win-x64"),
            SmoothPresenterPackaging.ExpectedNativePath(root),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LayoutContainsDll_FindsBesideAppOrNested()
    {
        var dir = Path.Combine(Path.GetTempPath(), "palace-smooth-pkg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.False(SmoothPresenterPackaging.LayoutContainsDll(dir));
            File.WriteAllText(Path.Combine(dir, SmoothPresenterPackaging.DllFileName), "dll");
            Assert.True(SmoothPresenterPackaging.LayoutContainsDll(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MsixContainsDllAtPackageRoot_RequiresRootEntry()
    {
        var dir = Path.Combine(Path.GetTempPath(), "palace-smooth-msix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var nested = Path.Combine(dir, "nested.zip");
        var root = Path.Combine(dir, "root.zip");
        try
        {
            using (var z = ZipFile.Open(nested, ZipArchiveMode.Create))
            {
                z.CreateEntry("smooth/" + SmoothPresenterPackaging.DllFileName);
            }

            using (var z = ZipFile.Open(root, ZipArchiveMode.Create))
            {
                z.CreateEntry(SmoothPresenterPackaging.DllFileName);
            }

            Assert.False(SmoothPresenterPackaging.MsixContainsDllAtPackageRoot(nested));
            Assert.True(SmoothPresenterPackaging.MsixContainsDllAtPackageRoot(root));
            Assert.False(SmoothPresenterPackaging.MsixContainsDllAtPackageRoot(null));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Palace.csproj")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        // Palace.Tests compiles beside the repo — walk up from the test project file.
        var cwd = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (cwd is not null)
        {
            if (File.Exists(Path.Combine(cwd.FullName, "Palace.csproj")))
            {
                return cwd.FullName;
            }

            cwd = cwd.Parent;
        }

        throw new InvalidOperationException("Palace.csproj not found from test base directory");
    }
}
