using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Headless stand-in for TagsPage MosaicReset / MosaicChunkAppended Unloaded clear.
/// </summary>
public sealed class OwnedDelegateTests
{
    [Fact]
    public void ClearIfOurs_SubscribeThenClear_BothNull()
    {
        Action? mosaicReset = null;
        Action? mosaicChunk = null;
        void OnReset() { }
        void OnChunk() { }

        mosaicReset = OnReset;
        mosaicChunk = OnChunk;
        Assert.NotNull(mosaicReset);
        Assert.NotNull(mosaicChunk);

        OwnedDelegate.ClearIfOurs(ref mosaicReset, OnReset);
        OwnedDelegate.ClearIfOurs(ref mosaicChunk, OnChunk);
        Assert.Null(mosaicReset);
        Assert.Null(mosaicChunk);
    }

    [Fact]
    public void ClearIfOurs_LeavesForeignHandler()
    {
        Action? slot = null;
        void Ours() { }
        void Foreign() { }
        slot = Foreign;
        OwnedDelegate.ClearIfOurs(ref slot, Ours);
        Assert.NotNull(slot);
        Assert.True(slot == (Action)Foreign);
    }

    [Fact]
    public void ReferenceEquals_FreshCast_DoesNotMatchAssignedSlot()
    {
        // Documents the TagsPage bug: ReferenceEquals(slot, (Action)Handler) is always false.
        Action? slot = null;
        void Handler() { }
        slot = Handler;
        Assert.False(ReferenceEquals(slot, (Action)Handler));
        Assert.True(slot == (Action)Handler);
    }
}
