using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagAddInputTests
{
    [Fact]
    public void OpensSuggestions_SingleToken_True()
    {
        Assert.True(TagAddInput.OpensSuggestions("vis"));
        Assert.True(TagAddInput.OpensSuggestions("  landscape  "));
    }

    [Fact]
    public void OpensSuggestions_PasteList_False()
    {
        Assert.False(TagAddInput.OpensSuggestions("sketch, wip"));
        Assert.False(TagAddInput.OpensSuggestions("sketch; wip"));
        Assert.False(TagAddInput.OpensSuggestions("sketch\nwip"));
        Assert.False(TagAddInput.OpensSuggestions("sketch，wip"));
        Assert.False(TagAddInput.OpensSuggestions(""));
        Assert.False(TagAddInput.OpensSuggestions(null));
    }

    [Fact]
    public void CommitOnSeparator_TrailingComma_CommitsAndClears()
    {
        var (committed, remainder) = TagAddInput.CommitOnSeparator("vis", "vis,");
        Assert.Equal(["vis"], committed);
        Assert.Equal("", remainder);
    }

    [Fact]
    public void CommitOnSeparator_PasteList_CommitsAll()
    {
        var (committed, remainder) = TagAddInput.CommitOnSeparator(
            "",
            "sketch, wip, value study, scenery");
        Assert.Equal(["sketch", "wip", "value study", "scenery"], committed);
        Assert.Equal("", remainder);
    }

    [Fact]
    public void CommitOnSeparator_NoSeparator_LeavesText()
    {
        var (committed, remainder) = TagAddInput.CommitOnSeparator("vi", "vis");
        Assert.Empty(committed);
        Assert.Equal("vis", remainder);
    }

    [Fact]
    public void PasteSummary_CountsNewAndAlias()
    {
        Assert.Equal("Pasted 4 tags · 1 new · 1 alias", TagAddInput.PasteSummary(4, 1, 1));
        Assert.Equal("Pasted 1 tag", TagAddInput.PasteSummary(1, 0, 0));
    }

    [Fact]
    public void ApplyButtonLabel_IncludesFileCount()
    {
        Assert.Equal("Apply 4 tags to 24 files", TagAddInput.ApplyButtonLabel(4, 24));
        Assert.Equal("Apply 1 tag", TagAddInput.ApplyButtonLabel(1, 1));
    }

    [Fact]
    public void ApplyHint_MatchesT2() =>
        Assert.Equal(
            "Enter applies · Esc clears · one Undo for the whole batch",
            TagAddInput.ApplyHint);

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "")]
    [InlineData(24, "on 24 selected")]
    public void AssignedTagsHeaderSuffix_MultiOnly(int n, string expected) =>
        Assert.Equal(expected, TagAddInput.AssignedTagsHeaderSuffix(n));

    [Fact]
    public void AliasFace_JoinsWithChevron() =>
        Assert.Equal("scenery › landscape", TagAddInput.AliasFace("scenery", "landscape"));

    [Fact]
    public void CreateSuggestionLabel_CurlyQuotes() =>
        Assert.Equal("Create tag “vis”", TagAddInput.CreateSuggestionLabel("vis"));

    [Fact]
    public void SuggestionHints_MatchT2() =>
        Assert.Equal(
            "↑ ↓ choose · Enter add · , add and keep typing",
            TagAddInput.SuggestionHints);

    [Fact]
    public void AliasPillLabel_IsAlias() =>
        Assert.Equal("alias", TagAddInput.AliasPillLabel);

    [Fact]
    public void DotColor_NewOrUngrouped_Slate()
    {
        Assert.Equal(TagAddInput.SlateHex, TagAddInput.DotColor(null, isNew: true));
        Assert.Equal(TagAddInput.SlateHex, TagAddInput.DotColor("", isNew: false));
        Assert.Equal("#A855F7", TagAddInput.DotColor("#A855F7", isNew: false));
    }
}
