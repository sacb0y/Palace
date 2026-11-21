using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class TagMembershipAddTests
{
    private static (List<Tag> Tags, List<TagMembership> Edges) Sample()
    {
        var tags = new List<Tag>
        {
            new() { Id = "character", Name = "Character", Color = "#E05A8C" },
            new() { Id = "subject", Name = "Subject", Color = "#3DD6C6" },
            new() { Id = "knight", Name = "knight" },
            new() { Id = "creature", Name = "creature" },
            new() { Id = "figure", Name = "figure" }
        };
        var edges = new List<TagMembership>
        {
            new() { ParentId = "subject", ChildId = "knight" },
            new() { ParentId = "character", ChildId = "creature" }
        };
        return (tags, edges);
    }

    [Fact]
    public void Classify_Added_Already_Cycle()
    {
        var (_, edges) = Sample();
        Assert.Equal(MembershipAddResult.Added, TagMembershipAdd.Classify("character", "knight", edges));
        Assert.Equal(MembershipAddResult.AlreadyMember, TagMembershipAdd.Classify("subject", "knight", edges));
        Assert.Equal(MembershipAddResult.CycleRefuse, TagMembershipAdd.Classify("character", "character", edges));
        Assert.Equal(
            MembershipAddResult.CycleRefuse,
            TagMembershipAdd.Classify("knight", "subject", edges));
    }

    [Fact]
    public void DragHint_Valid_Mixed_Refuse()
    {
        Assert.Equal("Add to Character", TagMembershipAdd.DragHint("Character", 3, 0, 0));
        Assert.Equal("Add 2 · 1 already in group", TagMembershipAdd.DragHint("Subject", 2, 1, 0));
        Assert.Equal("Already in Subject", TagMembershipAdd.DragHint("Subject", 0, 1, 0));
        Assert.Equal("Can't add Character to itself", TagMembershipAdd.DragHint("Character", 0, 0, 1));
        Assert.True(TagMembershipAdd.AllowsDrop(2));
        Assert.False(TagMembershipAdd.AllowsDrop(0));
    }

    [Fact]
    public void StatusMessage_Dg04_AndMixed()
    {
        Assert.Equal(
            "✓ Added 3 tags to Character",
            TagMembershipAdd.StatusMessage(3, 0, 0, "Character"));
        Assert.Equal(
            "✓ Added 2 tags to Hair · 1 already in group · 1 refused (cycle)",
            TagMembershipAdd.StatusMessage(2, 1, 1, "Hair"));
        Assert.Equal(
            "✓ Created New group and added 1 tag",
            TagMembershipAdd.StatusMessage(1, 0, 0, "New group", createdGroup: true));
    }

    [Fact]
    public void AddMenuPicks_ListsMemberCheckedAndOthersEnabled()
    {
        var (tags, edges) = Sample();
        var picks = TagMembershipAdd.AddMenuPicks(tags, edges, "knight");
        Assert.Equal(2, picks.Count);
        var subject = picks.Single(p => p.Name == "Subject");
        Assert.True(subject.IsMember);
        Assert.False(subject.IsEnabled);
        var character = picks.Single(p => p.Name == "Character");
        Assert.False(character.IsMember);
        Assert.True(character.IsEnabled);
        Assert.Equal("#E05A8C", character.Color);
    }

    [Fact]
    public void Payload_MultiSelect_UsesSelection()
    {
        Assert.Equal(["a", "b"], TagMembershipAdd.PayloadIds("a", true, ["a", "b"]));
        Assert.Equal(["x"], TagMembershipAdd.PayloadIds("x", false, ["a", "b"]));
        Assert.Equal("3 tags", TagMembershipAdd.MultiBadge(3));
        Assert.Equal("", TagMembershipAdd.MultiBadge(1));
    }
}
