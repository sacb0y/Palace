using System.Text.RegularExpressions;
using Xunit;

namespace Palace.Tests;

/// <summary>Source-level TagsPage layout contracts (no WinUI).</summary>
public sealed class TagsPageXamlLayoutTests
{
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

    private static string TagsPageXaml() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "Pages", "TagsPage.xaml"));

    [Fact]
    public void GrdTagColumns_NotWrappedByPageScrollViewer()
    {
        var xaml = TagsPageXaml();
        var grd = xaml.IndexOf("x:Name=\"GrdTagColumns\"", StringComparison.Ordinal);
        Assert.True(grd >= 0, "GrdTagColumns missing");

        var before = xaml[..grd];
        var opens = Regex.Matches(before, @"<ScrollViewer\b").Count;
        var closes = Regex.Matches(before, @"</ScrollViewer>").Count;
        Assert.Equal(
            opens,
            closes);
    }

    [Fact]
    public void WideColumns_AreStaticXaml_NotCodeAllocateDefaults()
    {
        var xaml = TagsPageXaml();
        Assert.Contains("x:Name=\"ColTagSidebar\" Width=\"200\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ColTagBoard\" Width=\"*\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"ColTagDetails\" Width=\"260\" MinWidth=\"240\" MaxWidth=\"300\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BoardHeader_IsOneRowStarAutoAutoAuto()
    {
        var xaml = TagsPageXaml();
        var header = xaml.IndexOf("x:Name=\"GrdTagBoardHeader\"", StringComparison.Ordinal);
        Assert.True(header >= 0, "GrdTagBoardHeader missing");
        var cols = xaml.IndexOf("<Grid.ColumnDefinitions>", header, StringComparison.Ordinal);
        var colsEnd = xaml.IndexOf("</Grid.ColumnDefinitions>", cols, StringComparison.Ordinal);
        Assert.True(cols >= 0 && colsEnd > cols);
        var block = xaml[cols..colsEnd];
        Assert.Contains("Width=\"*\"", block, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(block, @"Width=""Auto""").Count);
        Assert.Contains("TxtExpandAllTagBoard", xaml, StringComparison.Ordinal);
        Assert.Contains("TxtCollapseAllTagBoard", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ToolTip=\"Expand all\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ToolTip=\"Collapse all\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void BoardPane_ChipStar_MosaicAuto_ComposerAutoBottom()
    {
        var xaml = TagsPageXaml();
        var pane = xaml.IndexOf("x:Name=\"GrdTagBoardPane\"", StringComparison.Ordinal);
        Assert.True(pane >= 0);
        var rows = xaml.IndexOf("<Grid.RowDefinitions>", pane, StringComparison.Ordinal);
        var rowsEnd = xaml.IndexOf("</Grid.RowDefinitions>", rows, StringComparison.Ordinal);
        var block = xaml[rows..rowsEnd];
        Assert.DoesNotContain("2*", block, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight=\"160\"", block, StringComparison.Ordinal);
        // header Auto · chips * · mosaic Auto · composer Auto
        Assert.Single(Regex.Matches(block, @"Height=""\*"""));
        Assert.Equal(3, Regex.Matches(block, @"Height=""Auto""").Count);
        Assert.Contains("TxtTagAssetCount", xaml, StringComparison.Ordinal);
        // Composer + items count dock Auto under mosaic (bottom of board pane).
        var mosaic = xaml.IndexOf("x:Name=\"GrdTagAssets\"", StringComparison.Ordinal);
        var count = xaml.IndexOf("x:Name=\"TxtTagAssetCount\"", StringComparison.Ordinal);
        Assert.True(mosaic > 0 && count > mosaic, "TxtTagAssetCount should follow GrdTagAssets (composer at bottom)");
        var composer = xaml.IndexOf("x:Name=\"CmbNewTagsGroup\"", StringComparison.Ordinal);
        Assert.True(composer > mosaic && composer < count);
        Assert.Contains("Grid.Row=\"3\"", xaml[mosaic..composer], StringComparison.Ordinal);

        var assetsOpen = xaml[mosaic..(mosaic + 400)];
        Assert.Contains("MaxHeight=\"280\"", assetsOpen, StringComparison.Ordinal);
        Assert.Contains("HasTagAssets", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void BoardChipWrap_HorizontalSpacingFitsStarGap()
    {
        var xaml = TagsPageXaml();
        var showStar = xaml.IndexOf("ShowStar=\"True\"", StringComparison.Ordinal);
        Assert.True(showStar > 0);
        var layout = xaml.LastIndexOf("ChipWrapLayout", showStar, StringComparison.Ordinal);
        Assert.True(layout > 0);
        var block = xaml[layout..showStar];
        Assert.Contains("HorizontalSpacing=\"26\"", block, StringComparison.Ordinal);
        Assert.Contains("ArrangeIntoTrailingGap=\"True\"", block, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", block, StringComparison.Ordinal);
        Assert.DoesNotContain("HorizontalAlignment=\"Left\"", block, StringComparison.Ordinal);
    }

    [Fact]
    public void TagChip_RootAndGrdRoot_Stretch_FaceLeft()
    {
        var chipXaml = File.ReadAllText(Path.Combine(RepoRoot(), "Controls", "TagChip.xaml"));
        // UserControl root + GrdRoot Stretch; GrdFace stays Left.
        var uc = chipXaml.IndexOf("<UserControl", StringComparison.Ordinal);
        Assert.True(uc >= 0);
        var ucEnd = chipXaml.IndexOf(">", uc, StringComparison.Ordinal);
        var rootAttrs = chipXaml[uc..ucEnd];
        Assert.Contains("HorizontalAlignment=\"Stretch\"", rootAttrs, StringComparison.Ordinal);
        Assert.DoesNotContain("HorizontalAlignment=\"Left\"", rootAttrs, StringComparison.Ordinal);

        var grdRoot = chipXaml.IndexOf("x:Name=\"GrdRoot\"", StringComparison.Ordinal);
        Assert.True(grdRoot > 0);
        var grdRootEnd = chipXaml.IndexOf(">", grdRoot, StringComparison.Ordinal);
        var grdRootOpen = chipXaml[grdRoot..grdRootEnd];
        Assert.Contains("HorizontalAlignment=\"Stretch\"", grdRootOpen, StringComparison.Ordinal);
        Assert.DoesNotContain("HorizontalAlignment=\"Left\"", grdRootOpen, StringComparison.Ordinal);

        var face = chipXaml.IndexOf("x:Name=\"GrdFace\"", StringComparison.Ordinal);
        Assert.True(face > 0);
        var faceEnd = chipXaml.IndexOf(">", face, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Left\"", chipXaml[face..faceEnd], StringComparison.Ordinal);
    }

    [Fact]
    public void ScrTagDetails_EnablesVerticalScroll()
    {
        var xaml = TagsPageXaml();
        var scr = xaml.IndexOf("x:Name=\"ScrTagDetails\"", StringComparison.Ordinal);
        Assert.True(scr >= 0);
        // Opening element spans lines; take until the StackPanel child starts.
        var child = xaml.IndexOf("x:Name=\"FlyTagDetails\"", scr, StringComparison.Ordinal);
        Assert.True(child > scr);
        var open = xaml[scr..child];
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", open, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollMode=\"Enabled\"", open, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Stretch\"", open, StringComparison.Ordinal);
    }

    [Fact]
    public void DragGhostHost_InTree_AndChipPrefetchWired()
    {
        var xaml = TagsPageXaml();
        Assert.Contains("x:Name=\"CvsTagDragGhostHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PointerPressed=\"TagChip_PointerPressed\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowAccentBar", xaml, StringComparison.Ordinal);
        // Ghost RTB host must stay Visible (not Collapsed) for capture.
        var host = xaml.IndexOf("x:Name=\"CvsTagDragGhostHost\"", StringComparison.Ordinal);
        var hostEnd = xaml.IndexOf("</Canvas>", host, StringComparison.Ordinal);
        var block = xaml[host..hostEnd];
        Assert.DoesNotContain("Visibility=\"Collapsed\"", block, StringComparison.Ordinal);

        // WMC0035: host must be a child of GrdTagColumns, not a root Grid child
        // before VisualStateManager (that duplicates Grid.Children).
        var root = xaml.IndexOf("<Grid>", StringComparison.Ordinal);
        var vsm = xaml.IndexOf("VisualStateManager.VisualStateGroups", StringComparison.Ordinal);
        var cols = xaml.IndexOf("x:Name=\"GrdTagColumns\"", StringComparison.Ordinal);
        Assert.True(root >= 0 && vsm > root && cols > vsm && host > cols);
        Assert.DoesNotContain(
            "CvsTagDragGhostHost",
            xaml[root..vsm],
            StringComparison.Ordinal);
    }
}
