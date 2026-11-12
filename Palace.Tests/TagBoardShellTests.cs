using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagBoardShellTests
{
    [Fact]
    public void NewTagsHeader_UsesGroupOrUngrouped()
    {
        Assert.Equal("New tags in Medium", TagBoardShell.NewTagsHeader("Medium", ungrouped: false));
        Assert.Equal("New tags in Ungrouped", TagBoardShell.NewTagsHeader(null, ungrouped: true));
        Assert.Equal("New tags in Ungrouped", TagBoardShell.NewTagsHeader("  ", ungrouped: false));
    }

    [Fact]
    public void NextRecentlyCreated_StampsThenClears()
    {
        var stamped = TagBoardShell.NextRecentlyCreated([], ["a", "b"]);
        Assert.Equal(["a", "b"], stamped.OrderBy(x => x).ToArray());

        var faded = TagBoardShell.NextRecentlyCreated(stamped, justCreated: null);
        Assert.Empty(faded);
    }

    [Fact]
    public void NavKeys_RecognizePinnedSections()
    {
        Assert.True(TagBoardShell.IsNavSection("all"));
        Assert.True(TagBoardShell.IsNavSection("aliases"));
        Assert.True(TagBoardShell.IsGroupsHeader("groups-header"));
        Assert.True(TagBoardShell.IsUserGroupKey("medium"));
        Assert.False(TagBoardShell.IsUserGroupKey("recent"));
        Assert.False(TagBoardShell.IsUserGroupKey("aliases"));
    }

    [Fact]
    public void ViewIconSlot_RequiresTwentyDipForSizeSixteen()
    {
        Assert.Equal(16, TagBoardShell.ViewIconDip);
        Assert.Equal(20, TagBoardShell.ViewIconSlotDip);
        Assert.True(TagBoardShell.ViewIconSlotIsSafe(20));
        Assert.True(TagBoardShell.ViewIconSlotIsSafe(24, iconDip: 16));
        Assert.False(TagBoardShell.ViewIconSlotIsSafe(16));
        Assert.False(TagBoardShell.ViewIconSlotIsSafe(20, iconDip: 24));
    }

    [Fact]
    public void TagsPage_BoardViewIconHost_IsAtLeastTwentyDip()
    {
        var root = RepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "Pages", "TagsPage.xaml"));
        const string marker = "<!-- 20 DIP slot for Size16 Fluent glyphs";
        var at = xaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, "Tags board view-icon slot marker missing");
        var window = xaml.AsSpan(at, Math.Min(900, xaml.Length - at)).ToString();
        Assert.Contains("Width=\"20\"", window);
        Assert.Contains("Height=\"20\"", window);
        Assert.Contains("IconSize=\"Size16\"", window);
        // Regression: prior tip used a 16×16 host that clipped Tag/Clock/Link left ink.
        Assert.DoesNotContain("Width=\"16\"", window);
        Assert.DoesNotContain("Height=\"16\"", window);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Palace.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Palace.csproj not found above the test output");
    }
}
