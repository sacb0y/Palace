using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Unit coverage for the approved Library modifier-click plan
/// (anchor / range / entry-kind / empty-exit).
/// </summary>
public sealed class SelectModeModifierPolicyTests
{
    [Fact]
    public void Anchor_MovedByPlainAndCtrl_NotByShift()
    {
        // Plain / Ctrl move anchor; Shift and Ctrl+Shift keep it.
        Assert.True(MosaicModifierSelect.MovesAnchor(ctrl: false, shift: false));
        Assert.True(MosaicModifierSelect.MovesAnchor(ctrl: true, shift: false));
        Assert.False(MosaicModifierSelect.MovesAnchor(ctrl: false, shift: true));
        Assert.False(MosaicModifierSelect.MovesAnchor(ctrl: true, shift: true));
    }

    [Fact]
    public void ShiftRange_RepeatedReRangesFromSameAnchor()
    {
        bool[] headers = [false, false, false, false, false];
        const int anchor = 1;
        Assert.Equal([1, 2, 3], MosaicModifierSelect.RangeIndexes(headers, anchor, 3));
        Assert.Equal([1, 2], MosaicModifierSelect.RangeIndexes(headers, anchor, 2));
        Assert.Equal([0, 1], MosaicModifierSelect.RangeIndexes(headers, anchor, 0));
    }

    [Fact]
    public void CtrlShift_AddsRange_KeepsPrior()
    {
        bool[] headers = [false, false, false, false, false];
        var first = MosaicModifierSelect.RangeIndexes(headers, 0, 1); // 0,1
        var added = MosaicModifierSelect.UnionRange(headers, first, anchorIndex: 3, targetIndex: 4);
        Assert.Equal([0, 1, 3, 4], added);
    }

    [Fact]
    public void ModifierEntry_PlainBodyReplacesAndExits_CheckboxStillToggles()
    {
        var kind = SelectMode.EntryFromModifier();
        Assert.True(SelectMode.TileBodyClickReplacesAndExits(kind));
        Assert.True(SelectMode.CheckboxClickTogglesOnly(kind));
        Assert.True(SelectMode.ExitsWhenSelectionEmpty(true, 0));
    }

    [Fact]
    public void PurposefulEntry_PlainBodyToggles()
    {
        var kind = SelectMode.EntryFromPurposeful();
        Assert.True(SelectMode.TileBodyClickToggles(kind));
        Assert.False(SelectMode.TileBodyClickReplacesAndExits(kind));
    }
}
