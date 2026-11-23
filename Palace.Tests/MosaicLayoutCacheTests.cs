using System.Collections.Specialized;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class MosaicLayoutCacheTests
{
    [Fact]
    public void NeedsRecalculate_SameCountAndWidth_Clean_IsFalse()
    {
        Assert.False(
            MosaicLayoutCache.NeedsRecalculate(
                dirty: false,
                rectCount: 10,
                itemCount: 10,
                laidOutWidth: 800,
                width: 800));
    }

    [Fact]
    public void NeedsRecalculate_DirtySameCountAndWidth_IsTrue()
    {
        // Relayout after sort/filter/aspect with unchanged count+width.
        Assert.True(
            MosaicLayoutCache.NeedsRecalculate(
                dirty: true,
                rectCount: 10,
                itemCount: 10,
                laidOutWidth: 800,
                width: 800));
    }

    [Fact]
    public void NeedsRecalculate_CountMismatch_IsTrue()
    {
        Assert.True(
            MosaicLayoutCache.NeedsRecalculate(
                dirty: false,
                rectCount: 9,
                itemCount: 10,
                laidOutWidth: 800,
                width: 800));
    }

    [Fact]
    public void NeedsRecalculate_WidthChanged_IsTrue()
    {
        Assert.True(
            MosaicLayoutCache.NeedsRecalculate(
                dirty: false,
                rectCount: 10,
                itemCount: 10,
                laidOutWidth: 800,
                width: 801));
    }

    [Fact]
    public void MarksDirtyOnItemsChange_ResetReplaceAddRemove()
    {
        Assert.True(MosaicLayoutCache.MarksDirtyOnItemsChange(NotifyCollectionChangedAction.Reset));
        Assert.True(MosaicLayoutCache.MarksDirtyOnItemsChange(NotifyCollectionChangedAction.Replace));
        Assert.True(MosaicLayoutCache.MarksDirtyOnItemsChange(NotifyCollectionChangedAction.Add));
        Assert.True(MosaicLayoutCache.MarksDirtyOnItemsChange(NotifyCollectionChangedAction.Remove));
        Assert.False(MosaicLayoutCache.MarksDirtyOnItemsChange(NotifyCollectionChangedAction.Move));
    }

    [Fact]
    public void JustifiedMosaicLayout_RelayoutAndItemsChanged_MarkDirty()
    {
        var cs = File.ReadAllText(
            Path.Combine(RepoRoot(), "Helpers", "JustifiedMosaicLayout.cs"));
        Assert.Contains("_layoutDirty = true", cs, StringComparison.Ordinal);
        Assert.Contains("public void Relayout()", cs, StringComparison.Ordinal);
        Assert.Contains("OnItemsChangedCore", cs, StringComparison.Ordinal);
        Assert.Contains("MosaicLayoutCache.NeedsRecalculate", cs, StringComparison.Ordinal);
        Assert.Contains("MosaicLayoutCache.MarksDirtyOnItemsChange", cs, StringComparison.Ordinal);
        Assert.Contains("base.OnItemsChangedCore", cs, StringComparison.Ordinal);
        // Doktor: no dead else-if — first usable width dirties via WidthEpsilon alone.
        Assert.DoesNotContain("else if (!double.IsFinite(HostWidth)", cs, StringComparison.Ordinal);
        Assert.Contains("First usable width (HostWidth 0 → positive)", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryPage_SchedulesThumbsOnUsableWidth()
    {
        var cs = File.ReadAllText(
            Path.Combine(RepoRoot(), "Pages", "LibraryPage.xaml.cs"));
        Assert.Contains("ScheduleThumbPlan(\"resize\")", cs, StringComparison.Ordinal);
        Assert.Contains("_lastThumbPlanHostWidth", cs, StringComparison.Ordinal);
        Assert.Contains("MosaicLayoutCache.WidthEpsilon", cs, StringComparison.Ordinal);
        Assert.Contains("MinUsable", cs, StringComparison.Ordinal);
        Assert.Contains("RequestMosaicFocusAfterNav", cs, StringComparison.Ordinal);
        Assert.Contains("ClearMosaicFocusAfterNav", cs, StringComparison.Ordinal);
        Assert.Contains("FocusIsTextInput(focused)", cs, StringComparison.Ordinal);
        Assert.Contains("HasOpenContentDialog", cs, StringComparison.Ordinal);
        Assert.Contains("GetOpenPopupsForXamlRoot", cs, StringComparison.Ordinal);
        Assert.Contains("RefreshTileMoreReveals", cs, StringComparison.Ordinal);
        Assert.Contains("nameof(LibraryViewModel.SelectionCount)", cs, StringComparison.Ordinal);
        // Below MinUsable resets the first-usable gate.
        Assert.Contains("visible < MosaicLayoutWidth.MinUsable", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryPage_FocusAfterNav_ClearsAtPopulateEnd_NotOnMosaicReset()
    {
        // BLOCKER: Clear on MosaicReset kills Request before chunks land.
        // Clear / TryFocus only at FinishMosaicFocusAfterPopulate (MosaicRestoreReady).
        var cs = File.ReadAllText(
            Path.Combine(RepoRoot(), "Pages", "LibraryPage.xaml.cs"));
        var resetIdx = cs.IndexOf("private void OnMosaicReset()", StringComparison.Ordinal);
        Assert.True(resetIdx >= 0);
        var nextMethod = cs.IndexOf("private void OnMosaicChunkAppended()", resetIdx, StringComparison.Ordinal);
        Assert.True(nextMethod > resetIdx);
        var resetSlice = cs.Substring(resetIdx, nextMethod - resetIdx);
        Assert.DoesNotContain("ClearMosaicFocusAfterNav()", resetSlice, StringComparison.Ordinal);

        Assert.Contains("FinishMosaicFocusAfterPopulate()", cs, StringComparison.Ordinal);
        var restoreIdx = cs.IndexOf("private void OnMosaicRestoreReady(", StringComparison.Ordinal);
        Assert.True(restoreIdx >= 0);
        var restoreSlice = cs.Substring(restoreIdx, Math.Min(500, cs.Length - restoreIdx));
        Assert.Contains("FinishMosaicFocusAfterPopulate()", restoreSlice, StringComparison.Ordinal);

        var finishIdx = cs.IndexOf("private void FinishMosaicFocusAfterPopulate()", StringComparison.Ordinal);
        Assert.True(finishIdx >= 0);
        var finishSlice = cs.Substring(finishIdx, Math.Min(600, cs.Length - finishIdx));
        Assert.Contains("ClearMosaicFocusAfterNav()", finishSlice, StringComparison.Ordinal);
        Assert.Contains("TryFocusMosaicAfterNav()", finishSlice, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryPage_RemovesTileMoreRevealOnReload()
    {
        // (4) BtnAssetMore_Loaded removes the prior TileMoreReveal from the set.
        var cs = File.ReadAllText(
            Path.Combine(RepoRoot(), "Pages", "LibraryPage.xaml.cs"));
        var loadedIdx = cs.IndexOf("private void BtnAssetMore_Loaded", StringComparison.Ordinal);
        Assert.True(loadedIdx >= 0);
        var loadedSlice = cs.Substring(loadedIdx, Math.Min(700, cs.Length - loadedIdx));
        Assert.Contains("existing.Detach()", loadedSlice, StringComparison.Ordinal);
        Assert.Contains("_tileMoreReveals.Remove(existing)", loadedSlice, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryPage_FoldersTreeDirectInStarRow_NoOuterScrollViewer()
    {
        // Wimbly: no outer ScrollViewer (ListView virtualization). Auto header + * TreFolders.
        var xaml = File.ReadAllText(
            Path.Combine(RepoRoot(), "Pages", "LibraryPage.xaml"));
        Assert.DoesNotContain("ScrFolders", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TreFolders\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"TreFolders\"", xaml, StringComparison.Ordinal);
        // TreFolders must be Grid.Row="1" of the folders pane (bounded * height).
        var treIdx = xaml.IndexOf("x:Name=\"TreFolders\"", StringComparison.Ordinal);
        Assert.True(treIdx > 0);
        var slice = xaml.Substring(Math.Max(0, treIdx - 120), Math.Min(200, xaml.Length - Math.Max(0, treIdx - 120)));
        Assert.Contains("Grid.Row=\"1\"", slice, StringComparison.Ordinal);
        // Tags browse keeps its own ScrollViewer — untouched.
        Assert.Contains("x:Name=\"TreTagsBrowse\"", xaml, StringComparison.Ordinal);
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
