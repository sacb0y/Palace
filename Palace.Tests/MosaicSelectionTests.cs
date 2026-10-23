using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class MosaicSelectionTests
{
    [Fact]
    public void ResolveByIds_KeepsCollapsedSectionTilesInOrder()
    {
        var ordered = new[]
        {
            H("A"),
            T("a1"),
            H("B"),
            T("b1"),
            T("b2"),
            H("C"),
            T("c1")
        };
        // User had b1 selected; collapse B removes it from display but id stays selected.
        var resolved = MosaicSelection.ResolveByIds(
            ordered,
            ["b1", "missing", ""],
            item => item.Header,
            item => item.Id);
        Assert.Equal(["b1"], resolved.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void ResolveByIds_SelectAllAcrossCollapsed()
    {
        var ordered = new[]
        {
            H("A"),
            T("a1"),
            H("B"),
            T("b1"),
            T("b2")
        };
        var ids = ordered.Where(i => !i.Header).Select(i => i.Id).ToList();
        var resolved = MosaicSelection.ResolveByIds(
            ordered, ids, item => item.Header, item => item.Id);
        Assert.Equal(["a1", "b1", "b2"], resolved.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void MergeVisibleWithHiddenSelected_PreservesCollapsedOnEmptyItemsView()
    {
        var a = T("a1");
        var b = T("b1");
        var displayed = new HashSet<Item> { a }; // b is under a collapsed header
        // ItemsSource rebuild → ItemsView reports no selection.
        var merged = MosaicSelection.MergeVisibleWithHiddenSelected(
            visibleSelected: [],
            previousSelected: [a, b],
            displayed,
            item => item.Header);
        Assert.Equal(["b1"], merged.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void MergeVisibleWithHiddenSelected_KeepsVisibleAndHidden()
    {
        var a = T("a1");
        var b = T("b1");
        var c = T("c1");
        var displayed = new HashSet<Item> { a, c };
        var merged = MosaicSelection.MergeVisibleWithHiddenSelected(
            visibleSelected: [a],
            previousSelected: [a, b, c],
            displayed,
            item => item.Header);
        // Visible a kept; hidden b kept; c was previous but visible and not in ItemsView → dropped.
        Assert.Equal(["a1", "b1"], merged.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void CollapseFilter_ThenResolveByIds_MatchesIsiacViewOnlyRule()
    {
        var ordered = new[]
        {
            H("Deep"),
            T("d1"),
            H("SubB"),
            T("b1"),
            T("b2")
        };
        ordered[2].Collapsed = true;
        var display = FolderSectionPrefs.FilterCollapsedDisplay(
            ordered, i => i.Header, i => i.Collapsed);
        Assert.Equal(["Deep", "d1", "SubB"], display.Select(i => i.Id).ToArray());

        // Selection captured before republish; restored from ordered (not display).
        var restored = MosaicSelection.ResolveByIds(
            ordered, ["b1"], i => i.Header, i => i.Id);
        Assert.Single(restored);
        Assert.Equal("b1", restored[0].Id);
        Assert.DoesNotContain(restored[0], display);
    }

    [Fact]
    public void ExpandSection_ResolveByIds_KeepsSelectionFromCollapsedState()
    {
        // Laptop regression: expand cleared selection while collapse kept it.
        // Restore-by-id must keep ids across both filter directions.
        var ordered = new[]
        {
            H("Root"),
            T("r1"),
            H("AI"),
            T("a1"),
            T("a2")
        };
        ordered[2].Collapsed = true;
        var ids = new[] { "r1", "a1" };
        var whileCollapsed = MosaicSelection.ResolveByIds(
            ordered, ids, i => i.Header, i => i.Id);
        Assert.Equal(["r1", "a1"], whileCollapsed.Select(i => i.Id).ToArray());

        ordered[2].Collapsed = false;
        var expandedDisplay = FolderSectionPrefs.FilterCollapsedDisplay(
            ordered, i => i.Header, i => i.Collapsed);
        Assert.Contains(expandedDisplay, i => i.Id == "a1");
        var afterExpand = MosaicSelection.ResolveByIds(
            ordered, ids, i => i.Header, i => i.Id);
        Assert.Equal(["r1", "a1"], afterExpand.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void MergeVisibleWithHiddenSelected_ExpandMakesTilesVisible_EmptyItemsViewDropsThem()
    {
        // Documents why Ended must suppress SelectionChanged while re-stamping
        // highlights after expand: once tiles are displayed again, Merge cannot
        // preserve them from an empty ItemsView report.
        var root = T("r1");
        var ai = T("a1");
        var displayedExpanded = new HashSet<Item> { root, ai };
        var merged = MosaicSelection.MergeVisibleWithHiddenSelected(
            visibleSelected: [],
            previousSelected: [root, ai],
            displayedExpanded,
            item => item.Header);
        Assert.Empty(merged);
    }

    [Fact]
    public void ResolveAnchorAfterRestore_KeepsPriorWhenStillInOrder()
    {
        var a = T("a1");
        var b = T("b1");
        var ordered = new[] { H("A"), a, H("B"), b };
        // LastOrDefault after restore would be b; keep a as Shift-click origin.
        Assert.Same(a, MosaicSelection.ResolveAnchorAfterRestore(a, ordered, fallback: b));
        Assert.Same(b, MosaicSelection.ResolveAnchorAfterRestore(T("gone"), ordered, fallback: b));
        Assert.Null(MosaicSelection.ResolveAnchorAfterRestore(null, ordered, fallback: null));
    }

    [Fact]
    public void IndexInOrdered_FindsHiddenAnchorForRangeSelect()
    {
        var a = T("a1");
        var b = T("b1");
        var c = T("c1");
        var ordered = new[] { H("A"), a, H("B"), b, H("C"), c };
        // b is under collapsed B — not in display, but still rangeable by order index.
        Assert.Equal(3, MosaicSelection.IndexInOrdered(ordered, b));
        Assert.Equal(-1, MosaicSelection.IndexInOrdered(ordered, T("other")));
        var flags = ordered.Select(i => i.Header).ToList();
        var indexes = RangeSelect.ContiguousIndexes(flags, from: 3, to: 5);
        Assert.Equal([3, 5], indexes); // b and c; skips header at 4
    }

    private static Item H(string id) => new(id, true);

    private static Item T(string id) => new(id, false);

    private sealed class Item(string id, bool header)
    {
        public string Id { get; } = id;
        public bool Header { get; } = header;
        public bool Collapsed { get; set; }
    }
}
