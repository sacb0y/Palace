using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Isiac freeze: RefreshLibraryLayout must converge (≤2 applies) and expose a
/// call-rate probe for the FENRIR hang detector (250ms heartbeat).
/// </summary>
public sealed class LibraryLayoutRefreshTests
{
    [Fact]
    public void RefreshGate_StableWidth_ConvergesInAtMostTwoApplies()
    {
        // Shell-slot host ~911−248 → content ~647; same inputs forever after commit.
        var host = 663d;
        var content = LibraryColumnLayout.ContentWidthFromHostDip(host);
        var window = host + LibraryColumnLayout.ShellNavPaneWidthDip;
        var applies = LibraryLayoutRefreshGate.CountAppliesUntilStable(
            content,
            window,
            autoHideEnabled: true,
            PreviewPane.Initial,
            maxPasses: 10);
        Assert.True(applies <= 2, $"expected ≤2 applies, got {applies}");
        Assert.Equal(1, applies);
    }

    [Fact]
    public void RefreshGate_WidthChange_AllowsAnotherApply_ThenNoOps()
    {
        var gate = new LibraryLayoutRefreshGate();
        var narrowHost = 500d;
        var wideHost = 900d;
        var narrow = LibraryColumnLayout.ContentWidthFromHostDip(narrowHost);
        var wide = LibraryColumnLayout.ContentWidthFromHostDip(wideHost);
        var nav = LibraryColumnLayout.ShellNavPaneWidthDip;

        Assert.True(gate.TryEnter());
        try
        {
            var pane = PreviewPane.OnWidth(PreviewPane.Initial, narrow, true);
            var layout = PreviewPane.Layout(PreviewPane.Effective(pane, narrow));
            Assert.True(gate.ShouldApply(narrow, narrowHost + nav, true, pane, layout));
            gate.Commit(narrow, narrowHost + nav, true, pane, layout);
        }
        finally
        {
            gate.Exit();
        }

        Assert.True(gate.TryEnter());
        try
        {
            var pane = PreviewPane.OnWidth(PreviewPane.Initial, wide, true);
            var layout = PreviewPane.Layout(PreviewPane.Effective(pane, wide));
            Assert.True(gate.ShouldApply(wide, wideHost + nav, true, pane, layout));
            gate.Commit(wide, wideHost + nav, true, pane, layout);

            // Same wide inputs again — no-op (2nd pass settles).
            pane = PreviewPane.OnWidth(pane, wide, true);
            layout = PreviewPane.Layout(PreviewPane.Effective(pane, wide));
            Assert.False(gate.ShouldApply(wide, wideHost + nav, true, pane, layout));
        }
        finally
        {
            gate.Exit();
        }

        Assert.Equal(2, gate.ApplyCount);
        Assert.True(gate.ApplyCount <= 2);
    }

    [Fact]
    public void RefreshGate_Reentrancy_TryEnterFalseWhileBusy()
    {
        var gate = new LibraryLayoutRefreshGate();
        Assert.True(gate.TryEnter());
        Assert.False(gate.TryEnter());
        gate.Exit();
        Assert.True(gate.TryEnter());
        gate.Exit();
    }

    [Fact]
    public void RefreshProbe_HotIdle_FailsAboveTenPerSecond()
    {
        LibraryLayoutRefreshProbe.Reset();
        var t0 = 1_000_000L;
        for (var i = 0; i < 12; i++)
        {
            LibraryLayoutRefreshProbe.RecordCall(t0 + (i * 50));
        }

        var now = t0 + 600;
        Assert.True(LibraryLayoutRefreshProbe.CallsPerSecond(now) > 10);
        Assert.Equal(
            LibraryLayoutHangVerdict.HotIdle,
            LibraryLayoutRefreshProbe.Evaluate(now, expectingProgress: false));
        Assert.Equal(
            LibraryLayoutHangVerdict.Ok,
            LibraryLayoutRefreshProbe.Evaluate(now, expectingProgress: true));
    }

    [Fact]
    public void RefreshProbe_Stall_FailsWhenExpectingProgressAndSilent()
    {
        LibraryLayoutRefreshProbe.Reset();
        LibraryLayoutRefreshProbe.RecordCall(1_000L);
        Assert.Equal(
            LibraryLayoutHangVerdict.Stall,
            LibraryLayoutRefreshProbe.Evaluate(
                1_000L + LibraryLayoutRefreshProbe.StallFailMs + 1,
                expectingProgress: true));
        Assert.Equal(
            LibraryLayoutHangVerdict.Ok,
            LibraryLayoutRefreshProbe.Evaluate(
                1_000L + LibraryLayoutRefreshProbe.StallFailMs + 1,
                expectingProgress: false));
    }

    [Fact]
    public void RefreshProbe_HeartbeatConstants_MatchHarnessContract()
    {
        Assert.Equal(250, LibraryLayoutRefreshProbe.HeartbeatMs);
        Assert.Equal(1000, LibraryLayoutRefreshProbe.StallFailMs);
        Assert.Equal(10, LibraryLayoutRefreshProbe.MaxIdleCallsPerSecond, 0.01);
    }

    [Fact]
    public void LibraryPage_UsesShellSlotWidth_NotPageActualWidth()
    {
        var root = RepoRoot();
        var cs = File.ReadAllText(Path.Combine(root, "Pages", "LibraryPage.xaml.cs"));
        Assert.Contains("ResolveShellContentSlotWidthDip", cs, StringComparison.Ordinal);
        Assert.Contains("LibraryLayoutRefreshGate", cs, StringComparison.Ordinal);
        Assert.Contains("LibraryLayoutRefreshProbe.RecordCall", cs, StringComparison.Ordinal);
        // Must not prefer page ActualWidth for OnWidth content (loop).
        Assert.DoesNotContain(
            "return LibraryColumnLayout.ContentWidthFromHostDip(ActualWidth)",
            cs,
            StringComparison.Ordinal);
    }

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
}
