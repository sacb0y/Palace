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
        // First usable HostWidth must dirty+InvalidateMeasure (not stamp-only).
        Assert.Contains("First usable width must dirty", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryPage_SchedulesThumbsOnUsableWidth()
    {
        var cs = File.ReadAllText(
            Path.Combine(RepoRoot(), "Pages", "LibraryPage.xaml.cs"));
        Assert.Contains("ScheduleThumbPlan(\"resize\")", cs, StringComparison.Ordinal);
        Assert.Contains("MinUsable", cs, StringComparison.Ordinal);
        Assert.Contains("RequestMosaicFocusAfterNav", cs, StringComparison.Ordinal);
        Assert.Contains("RefreshTileMoreReveals", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryPage_FoldersTreeWrappedInScrollViewer()
    {
        var xaml = File.ReadAllText(
            Path.Combine(RepoRoot(), "Pages", "LibraryPage.xaml"));
        Assert.Contains("AutomationProperties.AutomationId=\"ScrFolders\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TreFolders\"", xaml, StringComparison.Ordinal);
        var scrIdx = xaml.IndexOf("ScrFolders", StringComparison.Ordinal);
        var treIdx = xaml.IndexOf("x:Name=\"TreFolders\"", StringComparison.Ordinal);
        Assert.True(scrIdx >= 0 && treIdx > scrIdx, "TreFolders must sit inside ScrFolders ScrollViewer");
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
