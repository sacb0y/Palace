using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagRemoveFlyoutTests
{
    [Fact]
    public void OrderMostUsed_ThenName()
    {
        var rows = new[]
        {
            ("sketch", 24),
            ("wip", 18),
            ("character", 15),
            ("aardvark", 18)
        };
        var ordered = TagRemoveFlyout.OrderMostUsed(rows, r => r.Item2, r => r.Item1);
        Assert.Equal(["sketch", "aardvark", "wip", "character"], ordered.Select(r => r.Item1).ToArray());
    }

    [Fact]
    public void FilterByName_SubstringCaseInsensitive()
    {
        var rows = new[] { "wip", "sketch", "landscape", "color study" };
        Assert.Equal(
            ["landscape"],
            TagRemoveFlyout.FilterByName(rows, "LAND", n => n));
        Assert.Equal(4, TagRemoveFlyout.FilterByName(rows, "  ", n => n).Count);
        Assert.Empty(TagRemoveFlyout.FilterByName(rows, "zzz", n => n));
    }

    [Fact]
    public void SelectAllState_AllNoneMixed()
    {
        Assert.Equal(true, TagRemoveFlyout.SelectAllState([true, true]));
        Assert.Equal(false, TagRemoveFlyout.SelectAllState([false, false]));
        Assert.Null(TagRemoveFlyout.SelectAllState([true, false]));
        Assert.Equal(false, TagRemoveFlyout.SelectAllState([]));
    }

    [Fact]
    public void ImpliedNote_ListsNames()
    {
        Assert.Equal(
            "Implied tags (figure, drawing, environment) follow their rules, so they aren't listed here.",
            TagRemoveFlyout.ImpliedNote(["figure", "drawing", "environment"]));
        Assert.Equal(TagRemoveConfirm.DialogImpliedNote, TagRemoveFlyout.ImpliedNote(null));
        Assert.Equal(TagRemoveConfirm.DialogImpliedNote, TagRemoveFlyout.ImpliedNote([]));
    }

    [Fact]
    public void UsageBarFillWidth_Clamps()
    {
        Assert.Equal(45, TagRemoveFlyout.UsageBarFillWidth(0.75));
        Assert.Equal(60, TagRemoveFlyout.UsageBarFillWidth(1.5));
        Assert.Equal(0, TagRemoveFlyout.UsageBarFillWidth(-0.2));
        Assert.Equal(TagRemoveFlyout.UsageBarWidthDip, 60);
    }

    [Fact]
    public void CheckedCounts_DriveFooter()
    {
        Assert.Equal(2, TagRemoveFlyout.CheckedTagCount([true, false, true]));
        Assert.Equal(
            28,
            TagRemoveFlyout.CheckedAssignmentCount(
            [
                (true, 18),
                (false, 24),
                (true, 6),
                (true, 4)
            ]));
        Assert.Equal("Remove 3 tags", TagRemoveConfirm.RemoveButtonLabel(3));
        Assert.Equal("28 assignments · one Undo", TagRemoveConfirm.AssignmentSummary(28));
    }

    [Fact]
    public void Copy_FilterSelectSortLabels()
    {
        Assert.Equal("Filter tags", TagRemoveFlyout.FilterPlaceholder);
        Assert.Equal("Select all", TagRemoveFlyout.SelectAllLabel);
        Assert.Equal("Sort: most used", TagRemoveFlyout.SortMostUsedLabel);
    }
}
