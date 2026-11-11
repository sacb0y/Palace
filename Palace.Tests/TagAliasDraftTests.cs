using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class TagAliasDraftTests
{
    private static readonly Tag Brown = new() { Id = "brown", Name = "brown hair" };
    private static readonly Tag Dark = new() { Id = "dark", Name = "dark hair" };
    private static readonly Tag Brunette = new() { Id = "brunette", Name = "brunette" };
    private static readonly Tag Chestnut = new() { Id = "chestnut", Name = "chestnut hair" };

    private static readonly List<Tag> Tags = [Brown, Dark, Brunette, Chestnut];

    private static List<TagAlias> Aliases() =>
    [
        new() { AliasTagId = Brunette.Id, IdealTagId = Dark.Id },
        new() { AliasTagId = Chestnut.Id, IdealTagId = Brown.Id }
    ];

    private static TagAliasDraft Eval(string? typed, IReadOnlyDictionary<string, int>? counts = null) =>
        TagAliasDraft.Evaluate(typed, Brown, Tags, Aliases(), counts);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("_ - _")]
    public void Empty_whenNothingTypedAfterFolding(string? typed)
    {
        var draft = Eval(typed);
        Assert.Equal(TagAliasDraftKind.Empty, draft.Kind);
        Assert.Equal(TagAliasDraftSeverity.None, draft.Severity);
        Assert.Equal("", draft.Headline);
        Assert.False(draft.CanCreate);
    }

    [Theory]
    [InlineData("Brown_Hair")]
    [InlineData("brown-hair")]
    [InlineData("  BROWN   hair ")]
    public void AutoMatch_whenTypedFoldsToTheIdealName(string typed)
    {
        var draft = Eval(typed);
        Assert.Equal(TagAliasDraftKind.AutoMatch, draft.Kind);
        Assert.Equal(TagAliasDraftSeverity.Info, draft.Severity);
        Assert.Equal("Already matches brown hair automatically. No alias needed.", draft.Headline);
        Assert.Equal("Capitals, _ , - and extra spaces are ignored. Enter does nothing.", draft.Detail);
        Assert.False(draft.CanCreate);
        Assert.False(draft.ShowsMove);
        Assert.False(draft.ShowsMerge);
    }

    [Fact]
    public void AlreadyHere_whenTheNameIsAlreadyAnAliasOfThisIdeal()
    {
        var draft = Eval("Chestnut_Hair");
        Assert.Equal(TagAliasDraftKind.AlreadyHere, draft.Kind);
        Assert.Equal(TagAliasDraftSeverity.Info, draft.Severity);
        Assert.Equal(Chestnut.Id, draft.ExistingTagId);
        Assert.Equal("Chestnut Hair is already an alias of brown hair.", draft.Headline);
        Assert.False(draft.CanCreate);
    }

    [Fact]
    public void ConflictOther_whenAnotherIdealOwnsTheAlias()
    {
        var draft = Eval("brunette");
        Assert.Equal(TagAliasDraftKind.ConflictOther, draft.Kind);
        Assert.Equal(TagAliasDraftSeverity.Error, draft.Severity);
        Assert.Equal(Brunette.Id, draft.ExistingTagId);
        Assert.Equal(Dark.Id, draft.OtherIdealId);
        Assert.Equal("dark hair", draft.OtherIdealName);
        Assert.Equal("brunette already points to dark hair", draft.Headline);
        Assert.Equal("An alias can point to only one tag.", draft.Detail);
        Assert.Equal("Move it here", draft.MoveLabel);
        Assert.True(draft.ShowsMove);
        Assert.False(draft.ShowsMerge);
        Assert.Contains("brunette", draft.MoveConfirmMessage);
        Assert.Contains("dark hair", draft.MoveConfirmMessage);
        Assert.Contains("brown hair", draft.MoveConfirmMessage);
    }

    [Fact]
    public void ExistingTag_offersMergeAndNamesTheFileCount()
    {
        var draft = Eval("Dark_Hair", new Dictionary<string, int> { [Dark.Id] = 42 });
        Assert.Equal(TagAliasDraftKind.ExistingTag, draft.Kind);
        Assert.Equal(TagAliasDraftSeverity.Error, draft.Severity);
        Assert.Equal(Dark.Id, draft.ExistingTagId);
        Assert.Equal("dark hair is a tag (42 files), so it can’t be an alias.", draft.Headline);
        Assert.Equal("To make it one, merge it into brown hair first.", draft.Detail);
        Assert.Equal("Merge dark hair into brown hair…", draft.MergeLabel);
        Assert.True(draft.ShowsMerge);
        Assert.False(draft.ShowsMove);
    }

    [Fact]
    public void ExistingTag_withoutCountsOmitsThem_andOneFileIsSingular()
    {
        Assert.Equal("dark hair is a tag, so it can’t be an alias.", Eval("dark hair").Headline);
        Assert.StartsWith(
            "dark hair is a tag (1 file),",
            Eval("dark hair", new Dictionary<string, int> { [Dark.Id] = 1 }).Headline);
    }

    [Fact]
    public void Ready_forAnUnusedName_andKeepsTheNormalizedSpelling()
    {
        var draft = Eval("  light_brown   hair ");
        Assert.Equal(TagAliasDraftKind.Ready, draft.Kind);
        Assert.Equal(TagAliasDraftSeverity.None, draft.Severity);
        Assert.Equal("light brown hair", draft.Name);
        Assert.True(draft.CanCreate);
        Assert.Equal("", draft.Headline);
    }

    [Fact]
    public void HelperTextMatchesTheMockup() =>
        Assert.Equal(
            "Spelling, capitals and _ are matched automatically; add only different words.",
            TagAliasDraft.HelperText);
}
