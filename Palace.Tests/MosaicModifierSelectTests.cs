using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class MosaicModifierSelectTests
{
    [Fact]
    public void ModifierFlags_ShiftRange_CtrlToggle_CtrlShiftAdd()
    {
        Assert.True(MosaicModifierSelect.IsModifierClick(ctrl: true, shift: false));
        Assert.True(MosaicModifierSelect.IsModifierClick(ctrl: false, shift: true));
        Assert.False(MosaicModifierSelect.IsModifierClick(ctrl: false, shift: false));

        Assert.True(MosaicModifierSelect.RangesFromAnchor(ctrl: false, shift: true));
        Assert.False(MosaicModifierSelect.MovesAnchor(ctrl: false, shift: true));

        Assert.True(MosaicModifierSelect.TogglesOneTile(ctrl: true, shift: false));
        Assert.True(MosaicModifierSelect.MovesAnchor(ctrl: true, shift: false));

        Assert.True(MosaicModifierSelect.AddsRangeToSelection(ctrl: true, shift: true));
        Assert.False(MosaicModifierSelect.MovesAnchor(ctrl: true, shift: true));
    }

    [Fact]
    public void RangeIndexes_ReadingOrder_SkipsHeaders()
    {
        bool[] headers = [true, false, false, true, false, false];
        Assert.Equal([1, 2, 4], MosaicModifierSelect.RangeIndexes(headers, 1, 4));
        Assert.Equal([1, 2, 4], MosaicModifierSelect.RangeIndexes(headers, 4, 1));
    }

    [Fact]
    public void UnionRange_AddsWithoutClearingPrior()
    {
        bool[] headers = [false, false, false, false, false];
        // Prior: indexes 0 and 1. Ctrl+Shift from 3→4 adds 3,4 → {0,1,3,4}.
        Assert.Equal(
            [0, 1, 3, 4],
            MosaicModifierSelect.UnionRange(headers, [0, 1], anchorIndex: 3, targetIndex: 4));
    }

    [Fact]
    public void KeyboardChords_Extend_FocusOnly_Space()
    {
        Assert.True(MosaicModifierSelect.ExtendsWithShiftArrow(shift: true, ctrl: false, 39));
        Assert.True(MosaicModifierSelect.MovesFocusOnly(shift: false, ctrl: true, 37));
        Assert.True(MosaicModifierSelect.TogglesWithCtrlSpace(ctrl: true, shift: false, 32));
        Assert.True(MosaicModifierSelect.RangesWithShiftSpace(ctrl: false, shift: true, 32));
        Assert.False(MosaicModifierSelect.ExtendsWithShiftArrow(shift: true, ctrl: true, 39));
    }

    [Fact]
    public void StepFocus_SkipsHeaders()
    {
        bool[] headers = [true, false, true, false, false];
        Assert.Equal(3, MosaicModifierSelect.StepFocus(headers, from: 1, delta: 1));
        Assert.Equal(1, MosaicModifierSelect.StepFocus(headers, from: 3, delta: -1));
        Assert.Equal(-1, MosaicModifierSelect.StepFocus(headers, from: 1, delta: -1));
        Assert.Equal(-1, MosaicModifierSelect.StepFocus(headers, from: 4, delta: 1));
    }

    [Fact]
    public void ArrowDelta_LeftUpBack_RightDownForward()
    {
        Assert.Equal(-1, MosaicModifierSelect.ArrowDelta(37));
        Assert.Equal(-1, MosaicModifierSelect.ArrowDelta(38));
        Assert.Equal(1, MosaicModifierSelect.ArrowDelta(39));
        Assert.Equal(1, MosaicModifierSelect.ArrowDelta(40));
        Assert.Equal(0, MosaicModifierSelect.ArrowDelta(32));
    }
}
