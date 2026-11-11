using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class TagAliasListTests
{
    private static readonly List<Tag> Tags =
    [
        new() { Id = "black", Name = "black hair", Color = "#FFFF8800" },
        new() { Id = "brown", Name = "brown hair" },
        new() { Id = "blk", Name = "blk hair" },
        new() { Id = "raven", Name = "raven hair" },
        new() { Id = "brunette", Name = "brunette" }
    ];

    private static readonly List<TagAlias> Aliases =
    [
        new() { AliasTagId = "brunette", IdealTagId = "brown" },
        new() { AliasTagId = "raven", IdealTagId = "black" },
        new() { AliasTagId = "blk", IdealTagId = "black" },
        new() { AliasTagId = "gone", IdealTagId = "black" }
    ];

    [Fact]
    public void Build_sortsByTagThenAlias_andDropsEdgesWithMissingTags()
    {
        var rows = TagAliasList.Build(Tags, Aliases);
        Assert.Equal(["blk hair", "raven hair", "brunette"], rows.Select(r => r.AliasName).ToArray());
        Assert.Equal(["black hair", "black hair", "brown hair"], rows.Select(r => r.IdealName).ToArray());
    }

    [Fact]
    public void Build_sortsByAlias()
    {
        var rows = TagAliasList.Build(Tags, Aliases, sort: TagAliasSort.Alias);
        Assert.Equal(["blk hair", "brunette", "raven hair"], rows.Select(r => r.AliasName).ToArray());
    }

    [Fact]
    public void Build_filtersByAliasOrTagName_foldingSpelling()
    {
        Assert.Equal(["brunette"], TagAliasList.Build(Tags, Aliases, query: "brun").Select(r => r.AliasName).ToArray());
        Assert.Equal(
            ["blk hair", "raven hair"],
            TagAliasList.Build(Tags, Aliases, query: "Black_Hair").Select(r => r.AliasName).ToArray());
        Assert.Empty(TagAliasList.Build(Tags, Aliases, query: "zzz"));
    }

    [Fact]
    public void Build_carriesIdealColorAndFileCount_andAutomationIds()
    {
        var counts = new Dictionary<string, int> { ["brown"] = 212 };
        var colors = new Dictionary<string, string?> { ["brown"] = "#FF00FF00" };
        var rows = TagAliasList.Build(Tags, Aliases, counts, colors);

        var brunette = rows.Single(r => r.AliasName == "brunette");
        Assert.Equal(212, brunette.FileCount);
        Assert.Equal("212", brunette.FileCountText);
        Assert.Equal("#FF00FF00", brunette.IdealColor);
        Assert.Equal("BtnAliasRow_brunette", brunette.AutomationId);
        Assert.Equal("BtnRemoveAlias_brunette", brunette.RemoveAutomationId);
        Assert.Equal("brunette › brown hair", brunette.ToString());

        var raven = rows.Single(r => r.AliasName == "raven hair");
        Assert.Equal("#FFFF8800", raven.IdealColor);
        Assert.Equal("", raven.FileCountText);
        Assert.Equal("BtnAliasRow_ravenhair", raven.AutomationId);
    }

    [Fact]
    public void ForIdeal_listsOnlyThatIdealsAliases()
    {
        var rows = TagAliasList.Build(Tags, Aliases);
        Assert.Equal(["blk hair", "raven hair"], TagAliasList.ForIdeal(rows, "black").Select(r => r.AliasName).ToArray());
        Assert.Empty(TagAliasList.ForIdeal(rows, "nope"));
    }

    [Fact]
    public void Caption_matchesTheMockupWording()
    {
        Assert.Equal("14 aliases · 11 tags", TagAliasList.Caption(14, 14, 11, filtering: false));
        Assert.Equal("1 alias · 1 tag", TagAliasList.Caption(1, 1, 1, filtering: false));
        Assert.Equal("1 of 14 match", TagAliasList.Caption(1, 14, 11, filtering: true));
        Assert.Equal("14 aliases", TagAliasList.StatusLine(14));
    }
}
