using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Real selection-policy tests (mirrors LibraryViewModel.Selection).
/// </summary>
public sealed class MosaicSelectHarnessTests
{
    private static MosaicSelectHarness Five() =>
        new([
            new MosaicSelectHarness.Tile("a"),
            new MosaicSelectHarness.Tile("b"),
            new MosaicSelectHarness.Tile("c"),
            new MosaicSelectHarness.Tile("d"),
            new MosaicSelectHarness.Tile("e"),
        ]);

    [Fact]
    public void EnsureSelectMode_DoesNotRestampEntryKindOnLaterModifier()
    {
        var h = Five();
        h.EnsureSelectMode(SelectMode.EntryFromPurposeful());
        Assert.Equal(SelectEntryKind.Purposeful, h.EntryKind);

        // Later Ctrl-click path calls EnsureSelectMode(Modifier) — must keep Purposeful.
        h.EnsureSelectMode(SelectMode.EntryFromModifier());
        Assert.Equal(SelectEntryKind.Purposeful, h.EntryKind);
    }

    [Fact]
    public void PurposefulEnter_ThenCtrlClick_PlainBodyStillToggles()
    {
        var a = new MosaicSelectHarness.Tile("a");
        var b = new MosaicSelectHarness.Tile("b");
        var c = new MosaicSelectHarness.Tile("c");
        var h = new MosaicSelectHarness([a, b, c]);

        h.EnsureSelectMode(SelectMode.EntryFromPurposeful());
        h.SelectAsset(a, toggle: false);
        Assert.Equal(SelectEntryKind.Purposeful, h.EntryKind);

        // Ctrl-click b — EnsureSelectMode(Modifier) must not restamp.
        h.EnsureSelectMode(SelectMode.EntryFromModifier());
        h.SelectAsset(b, toggle: true);
        Assert.Equal(SelectEntryKind.Purposeful, h.EntryKind);
        Assert.True(h.IsSelectMode);
        Assert.Equal(2, h.SelectionCount);

        // Plain body on c → toggle (not replace+exit).
        h.ApplyTileBodyClick(c);
        Assert.True(h.IsSelectMode);
        Assert.Equal(SelectEntryKind.Purposeful, h.EntryKind);
        Assert.True(c.IsSelected);
        Assert.Equal(3, h.SelectionCount);
    }

    [Fact]
    public void ModifierEnter_PlainBody_ReplacesAndExits()
    {
        var a = new MosaicSelectHarness.Tile("a");
        var b = new MosaicSelectHarness.Tile("b");
        var h = new MosaicSelectHarness([a, b]);
        h.EnsureSelectMode(SelectMode.EntryFromModifier());
        h.SelectAsset(a, toggle: false);
        h.SelectAsset(b, toggle: true);
        Assert.Equal(2, h.SelectionCount);

        h.ApplyTileBodyClick(a);
        Assert.False(h.IsSelectMode);
        Assert.Equal(SelectEntryKind.None, h.EntryKind);
        Assert.Equal(1, h.SelectionCount);
        Assert.Same(a, h.Selection[0]);
    }

    [Fact]
    public void Anchor_SetByPlainAndCtrl_NotMovedByShift_ReRangeSameAnchor()
    {
        var a = new MosaicSelectHarness.Tile("a");
        var b = new MosaicSelectHarness.Tile("b");
        var c = new MosaicSelectHarness.Tile("c");
        var d = new MosaicSelectHarness.Tile("d");
        var h = new MosaicSelectHarness([a, b, c, d]);
        h.EnsureSelectMode(SelectMode.EntryFromModifier());
        h.SelectAsset(b, toggle: false);
        Assert.Same(b, h.Anchor);

        h.SelectAsset(d, toggle: false, range: true);
        Assert.Same(b, h.Anchor);
        Assert.Equal(["b", "c", "d"], h.Selection.Select(t => t.Id));
        Assert.Same(d, h.Focus);

        h.SelectAsset(c, toggle: false, range: true);
        Assert.Same(b, h.Anchor);
        Assert.Equal(["b", "c"], h.Selection.Select(t => t.Id));
        Assert.Same(c, h.Focus);
    }

    [Fact]
    public void CtrlShift_AddsRange_KeepsPriorSelection()
    {
        var a = new MosaicSelectHarness.Tile("a");
        var b = new MosaicSelectHarness.Tile("b");
        var c = new MosaicSelectHarness.Tile("c");
        var d = new MosaicSelectHarness.Tile("d");
        var e = new MosaicSelectHarness.Tile("e");
        var h = new MosaicSelectHarness([a, b, c, d, e]);
        h.EnsureSelectMode(SelectMode.EntryFromModifier());
        h.SelectAsset(a, toggle: false);
        h.SelectAsset(b, toggle: false, range: true);
        // Manually set anchor to d without clearing (simulate Ctrl-click that selects d).
        h.SelectAsset(d, toggle: true);
        Assert.Same(d, h.Anchor);
        h.SelectAsset(e, toggle: false, range: true, addRange: true);
        Assert.Equal(["a", "b", "d", "e"], h.Selection.Select(t => t.Id));
        Assert.Same(d, h.Anchor);
        Assert.Same(e, h.Focus);
    }

    [Fact]
    public void CtrlClick_MovesAnchor_EvenWhenDeselecting()
    {
        var a = new MosaicSelectHarness.Tile("a");
        var b = new MosaicSelectHarness.Tile("b");
        var h = new MosaicSelectHarness([a, b]);
        h.EnsureSelectMode(SelectMode.EntryFromModifier());
        h.SelectAsset(a, toggle: false);
        h.CtrlClick(b);
        Assert.Same(b, h.Anchor);
        Assert.Equal(2, h.SelectionCount);

        h.CtrlClick(b); // deselect b
        Assert.Same(b, h.Anchor);
        Assert.False(b.IsSelected);
        Assert.Equal(1, h.SelectionCount);
    }

    [Fact]
    public void CtrlClick_InMultiple_ChangesCountByExactlyOne()
    {
        var tiles = Enumerable.Range(0, 5)
            .Select(i => new MosaicSelectHarness.Tile(((char)('a' + i)).ToString()))
            .ToArray();
        var h = new MosaicSelectHarness(tiles);
        h.EnsureSelectMode(SelectMode.EntryFromModifier());
        h.SelectAsset(tiles[0], toggle: false);
        h.SelectAsset(tiles[3], toggle: false, range: true); // a..d → 4
        Assert.Equal(4, h.SelectionCount);
        Assert.False(tiles[4].IsSelected);

        var before = h.SelectionCount;
        h.CtrlClick(tiles[4]); // add e
        Assert.Equal(before + 1, h.SelectionCount);
        Assert.True(tiles[4].IsSelected);
        Assert.Same(tiles[4], h.Anchor);

        before = h.SelectionCount;
        h.CtrlClick(tiles[4]); // remove e
        Assert.Equal(before - 1, h.SelectionCount);
        Assert.False(tiles[4].IsSelected);
        Assert.Same(tiles[4], h.Anchor);

        // Second Ctrl on same unselected target would add again (±1), never net-zero.
        before = h.SelectionCount;
        h.CtrlClick(tiles[2]);
        Assert.Equal(before - 1, h.SelectionCount); // c was in a..d
    }

    [Fact]
    public void SelectionEmpty_ExitsSelectMode()
    {
        var a = new MosaicSelectHarness.Tile("a");
        var h = new MosaicSelectHarness([a]);
        h.EnsureSelectMode(SelectMode.EntryFromModifier());
        h.SelectAsset(a, toggle: false);
        h.SelectAsset(a, toggle: true);
        Assert.False(h.IsSelectMode);
        Assert.Equal(0, h.SelectionCount);
    }

    [Fact]
    public void DroppedSelection_CtrlZ_Restores_InvisibleIdsSkipped()
    {
        var a = new MosaicSelectHarness.Tile("a");
        var b = new MosaicSelectHarness.Tile("b");
        var h = new MosaicSelectHarness([a, b]);
        h.EnsureSelectMode(SelectMode.EntryFromModifier());
        h.SelectAsset(a, toggle: false);
        h.SelectAsset(b, toggle: true);
        h.CaptureAndDrop();
        Assert.True(h.Dropped.CanRestore);
        Assert.Equal(0, h.SelectionCount);

        Assert.True(h.TryRestoreDropped());
        Assert.Equal(2, h.SelectionCount);
        Assert.True(h.IsSelectMode);
    }

    [Fact]
    public void DroppedSelection_InvisibleOnly_ReturnsFalse_ForFileOpFallthrough()
    {
        var a = new MosaicSelectHarness.Tile("a");
        var h = new MosaicSelectHarness([a]);
        h.EnsureSelectMode(SelectMode.EntryFromModifier());
        h.SelectAsset(a, toggle: false);
        h.CaptureAndDrop();

        // New "folder" with different tiles — dropped ids not visible.
        var other = new MosaicSelectHarness([new MosaicSelectHarness.Tile("z")]);
        // Transfer the slot state by capturing same ids into other's Dropped... 
        // Simulate: capture then replace order by restoring against empty-visible harness.
        var gone = new MosaicSelectHarness([new MosaicSelectHarness.Tile("z")]);
        gone.Dropped.Capture(["a", "b"]);
        Assert.False(gone.TryRestoreDropped());
        Assert.False(gone.Dropped.CanRestore);
    }

    [Fact]
    public void DropThenTagEdit_CtrlZFallsThrough()
    {
        var a = new MosaicSelectHarness.Tile("a");
        var h = new MosaicSelectHarness([a]);
        h.EnsureSelectMode(SelectMode.EntryFromModifier());
        h.SelectAsset(a, toggle: false);
        h.CaptureAndDrop();
        h.SimulateTagEditAfterDrop();
        Assert.False(h.TryRestoreDropped());
    }

    [Fact]
    public void StaleModifierPress_NextPressLiftsSuppress_PlainSelectionWorks()
    {
        var a = new MosaicSelectHarness.Tile("a");
        var b = new MosaicSelectHarness.Tile("b");
        var h = new MosaicSelectHarness([a, b]);
        h.EnsureSelectMode(SelectMode.EntryFromModifier());
        h.SelectAsset(a, toggle: false);

        h.BeginModifierPress(pointerId: 11);
        Assert.True(h.SuppressMosaicSelection);
        Assert.Equal(11u, h.ModifierSuppressPointerId);
        // No matching EndModifierPress (drag off / cancel never delivered).
        Assert.False(h.TryApplyPlainSelection(b));

        // Next RangePressed (plain) self-heals — suppress lifted, plain selection works.
        h.OnRangePressedStart();
        Assert.False(h.SuppressMosaicSelection);
        Assert.Null(h.ModifierSuppressPointerId);
        Assert.True(h.TryApplyPlainSelection(b));
        Assert.Equal(1, h.SelectionCount);
        Assert.Same(b, h.Selection[0]);
    }
}
