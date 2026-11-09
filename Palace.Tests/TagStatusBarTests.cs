using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagStatusBarTests
{
    [Fact]
    public void Tagged_UsesFilesAndCreatedClause()
    {
        var text = TagStatusBar.Tagged(
            24,
            ["sketch", "wip", "value study", "landscape"],
            createdCount: 1);
        Assert.Equal(
            "✓ Tagged 24 files with sketch, wip, value study, landscape · created 1 tag",
            text);
    }

    [Fact]
    public void Removed_ListsTagsAndFileCount()
    {
        var text = TagStatusBar.Removed(
            ["wip", "color study", "needs review"],
            24);
        Assert.Equal(
            "✓ Removed wip, color study, needs review from 24 files",
            text);
    }

    [Fact]
    public void WithCheck_Idempotent()
    {
        Assert.Equal("✓ Tagged 1 file with a", TagStatusBar.WithCheck("✓ Tagged 1 file with a"));
        Assert.Equal("✓ hi", TagStatusBar.WithCheck("hi"));
    }

    [Fact]
    public void CreatedInGroup_TagsPageWording() =>
        Assert.Equal(
            "✓ Created 3 tags in Medium: charcoal, gouache, pastel",
            TagStatusBar.CreatedInGroup(3, "Medium", ["charcoal", "gouache", "pastel"]));
}
