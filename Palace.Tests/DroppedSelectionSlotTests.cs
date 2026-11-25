using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class DroppedSelectionSlotTests
{
    [Fact]
    public void Capture_ThenCtrlZ_RestoresIds()
    {
        var slot = new DroppedSelectionSlot();
        slot.Capture(["a", "b", "a", ""]);
        Assert.True(slot.CanRestore);
        Assert.Equal(["a", "b"], slot.Ids);
        var taken = slot.TryTakeRestore();
        Assert.Equal(["a", "b"], taken);
        Assert.False(slot.CanRestore);
        Assert.Null(slot.TryTakeRestore());
    }

    [Fact]
    public void LaterFileOp_DiscardsSlot_CtrlZFallsThrough()
    {
        var slot = new DroppedSelectionSlot();
        slot.Capture(["x"]);
        slot.NotifyOtherAction();
        Assert.False(slot.CanRestore);
        Assert.Null(slot.TryTakeRestore());
    }

    [Fact]
    public void EmptyCapture_DoesNotArmRestore()
    {
        var slot = new DroppedSelectionSlot();
        slot.Capture(["", "  "]);
        Assert.False(slot.CanRestore);
        Assert.Null(slot.Ids);
    }

    [Fact]
    public void Discard_ClearsMostRecent()
    {
        var slot = new DroppedSelectionSlot();
        slot.Capture(["z"]);
        slot.Discard();
        Assert.False(slot.IsMostRecent);
        Assert.Null(slot.Ids);
    }
}
