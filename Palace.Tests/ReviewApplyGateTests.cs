using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ReviewApplyGateTests
{
    [Fact]
    public void XThenKFast_SecondBeginRejectedWhileBusy()
    {
        var gate = new ReviewApplyGate();
        Assert.True(gate.TryBegin("asset-a", out var first));
        Assert.Equal("asset-a", first);
        Assert.True(gate.IsBusy);
        // Rapid K while X/reject is still in flight.
        Assert.False(gate.TryBegin("asset-a", out _));
        Assert.False(gate.TryBegin("asset-b", out _));
        Assert.True(gate.StillOnCaptured("asset-a"));
        Assert.False(gate.StillOnCaptured("asset-b"));
        gate.End();
        Assert.False(gate.IsBusy);
        Assert.True(gate.TryBegin("asset-b", out var second));
        Assert.Equal("asset-b", second);
    }

    [Fact]
    public void StillOnCaptured_FalseWhenSessionAdvanced()
    {
        var gate = new ReviewApplyGate();
        Assert.True(gate.TryBegin("a", out _));
        Assert.False(gate.StillOnCaptured("b"));
        Assert.False(gate.StillOnCaptured(null));
        gate.End();
        Assert.False(gate.StillOnCaptured("a"));
    }

    [Fact]
    public void TryBegin_RejectsEmptyCurrent()
    {
        var gate = new ReviewApplyGate();
        Assert.False(gate.TryBegin(null, out _));
        Assert.False(gate.TryBegin("", out _));
        Assert.False(gate.IsBusy);
    }
}
