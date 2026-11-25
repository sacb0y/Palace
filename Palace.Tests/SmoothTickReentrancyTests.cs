using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SmoothTickReentrancyTests
{
    [Fact]
    public void TryEnter_BlocksNestedEnter_UntilExit()
    {
        var gate = new SmoothTickReentrancyGate();
        Assert.False(gate.IsInside);
        Assert.True(gate.TryEnter());
        Assert.True(gate.IsInside);
        Assert.False(gate.TryEnter());
        Assert.True(gate.IsInside);
        gate.Exit();
        Assert.False(gate.IsInside);
        Assert.True(gate.TryEnter());
        gate.Exit();
        Assert.False(gate.IsInside);
    }

    [Fact]
    public void Exit_WhenNotEntered_IsNoOp()
    {
        var gate = new SmoothTickReentrancyGate();
        gate.Exit();
        Assert.False(gate.IsInside);
        Assert.True(gate.TryEnter());
        gate.Exit();
        gate.Exit();
        Assert.False(gate.IsInside);
    }
}
