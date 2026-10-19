using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagPriorityEditTests
{
    [Fact]
    public void FromBox_saves_new_value_not_the_previous_priority()
    {
        var commit = TagPriorityEdit.FromBox(5, loadingSelection: false);
        Assert.True(commit.Save);
        Assert.Equal(5, commit.Value);
    }

    [Fact]
    public void FromBox_skips_save_while_loading_selection()
    {
        var commit = TagPriorityEdit.FromBox(12, loadingSelection: true);
        Assert.False(commit.Save);
    }

    [Fact]
    public void FromBox_skips_non_finite_values()
    {
        Assert.False(TagPriorityEdit.FromBox(double.NaN, loadingSelection: false).Save);
        Assert.False(TagPriorityEdit.FromBox(double.PositiveInfinity, loadingSelection: false).Save);
    }
}
