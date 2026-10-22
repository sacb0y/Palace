using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SectionToggleGuardTests
{
    [Fact]
    public void OverlappingToggles_AlwaysDecrement_DepthReachesZero()
    {
        var guard = new SectionToggleGuard();
        var first = guard.Start();
        var second = guard.Start();
        Assert.Equal(2, guard.Depth);
        Assert.Equal(1, first);
        Assert.Equal(2, second);

        // Older Ended must still decrement (not compare to current generation).
        Assert.True(guard.TryEnd(first, out var restoreFirst));
        Assert.False(restoreFirst);
        Assert.Equal(1, guard.Depth);

        Assert.True(guard.TryEnd(second, out var restoreSecond));
        Assert.True(restoreSecond);
        Assert.Equal(0, guard.Depth);
    }

    [Fact]
    public void StaleEndedAfterLoaded_Ignored_NewToggleStillEnds()
    {
        var guard = new SectionToggleGuard();
        var stale = guard.Start();
        Assert.Equal(1, guard.Depth);

        // Leave mid-rebuild: depth cleared, mark stamped; generation kept.
        guard.OnLoaded();
        Assert.Equal(0, guard.Depth);
        Assert.Equal(stale, guard.ResetMark);
        Assert.Equal(stale, guard.Generation);

        var fresh = guard.Start();
        Assert.Equal(1, guard.Depth);
        Assert.True(fresh > stale);

        Assert.False(guard.TryEnd(stale, out var restoreStale));
        Assert.False(restoreStale);
        Assert.Equal(1, guard.Depth);

        Assert.True(guard.TryEnd(fresh, out var restoreFresh));
        Assert.True(restoreFresh);
        Assert.Equal(0, guard.Depth);
    }

    [Fact]
    public void LoadedDoesNotResetGeneration_StaleCannotMatchNewToggle()
    {
        var guard = new SectionToggleGuard();
        guard.Start();
        guard.Start();
        Assert.Equal(2, guard.Generation);

        guard.OnLoaded();
        Assert.Equal(2, guard.Generation);
        Assert.Equal(2, guard.ResetMark);

        // A new toggle is gen 3 — stale gen 2 is ≤ mark and ignored.
        var next = guard.Start();
        Assert.Equal(3, next);
        Assert.False(guard.TryEnd(2, out _));
        Assert.Equal(1, guard.Depth);
        Assert.True(guard.TryEnd(next, out var restore));
        Assert.True(restore);
    }
}
