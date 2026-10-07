using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagPortableTests
{
    [Fact]
    public void Serialize_RoundTripsSchemaAndFields()
    {
        var doc = new TagPortableDocument
        {
            Tags =
            [
                new TagPortableEntry
                {
                    Name = "Character",
                    Priority = 2,
                    Color = "#AABBCC",
                    Starred = true,
                    Parents = ["Intellectual Property"],
                    Implies = ["Sonic"]
                },
                new TagPortableEntry { Name = "Intellectual Property" },
                new TagPortableEntry { Name = "Sonic" }
            ]
        };

        var json = TagPortable.Serialize(doc);
        Assert.Contains("palace-tags/1", json, StringComparison.Ordinal);
        Assert.Contains("Character", json, StringComparison.Ordinal);

        var parsed = TagPortable.Parse(json);
        Assert.Equal(TagPortable.Schema, parsed.Schema);
        Assert.Equal(3, parsed.Tags.Count);
        var character = Assert.Single(parsed.Tags, t => t.Name == "Character");
        Assert.Equal(2, character.Priority);
        Assert.Equal("#AABBCC", character.Color);
        Assert.True(character.Starred);
        Assert.Equal(["Intellectual Property"], character.Parents);
        Assert.Equal(["Sonic"], character.Implies);
    }

    [Fact]
    public void FromCatalog_ExportsParentsAndImpliesByName()
    {
        var tags = new[]
        {
            new TagPortableTagSource { Id = "1", Name = "IP", Priority = 1 },
            new TagPortableTagSource { Id = "2", Name = "Sonic", Priority = 0, Color = "#112233", IsStarred = true },
        };
        var memberships = new[] { new TagPortableEdge { FromId = "1", ToId = "2" } };
        var implications = new[] { new TagPortableEdge { FromId = "2", ToId = "1" } };

        var doc = TagPortable.FromCatalog(tags, memberships, implications);
        var sonic = Assert.Single(doc.Tags, t => t.Name == "Sonic");
        Assert.Equal(["IP"], sonic.Parents);
        Assert.Equal(["IP"], sonic.Implies);
        Assert.Equal("#112233", sonic.Color);
        Assert.True(sonic.Starred);
    }

    [Fact]
    public void PlanMerge_CreatesMissingAndUpdatesExisting()
    {
        var existing = new[]
        {
            new TagPortableTagSource { Id = "a", Name = "Character", Priority = 0 }
        };
        var doc = TagPortable.Parse("""
            {
              "schema": "palace-tags/1",
              "tags": [
                { "name": "Character", "priority": 5, "color": "#ff0000", "starred": true },
                { "name": "Sonic", "parents": ["Character"], "implies": ["Hedgehog"] },
                { "name": "Hedgehog" }
              ]
            }
            """);

        var plan = TagPortable.PlanMerge(existing, [], [], doc);
        Assert.Equal(2, plan.Created);
        Assert.Equal(1, plan.Updated);
        Assert.Equal(1, plan.MembershipAdded);
        Assert.Equal(1, plan.ImplicationAdded);
        Assert.Contains(plan.Updates, u => u.Name == "Character" && u.Priority == 5 && u.Starred);
        Assert.Contains(plan.Creates, c => c.Name == "Sonic");
        Assert.Contains(plan.Creates, c => c.Name == "Hedgehog");
    }

    [Fact]
    public void PlanMerge_RejectsMembershipCycle()
    {
        var existing = new[]
        {
            new TagPortableTagSource { Id = "a", Name = "A" },
            new TagPortableTagSource { Id = "b", Name = "B" }
        };
        var memberships = new[] { new TagPortableEdge { FromId = "a", ToId = "b" } };
        // Import asks B to be parent of A — would cycle.
        var doc = TagPortable.Parse("""
            {
              "schema": "palace-tags/1",
              "tags": [
                { "name": "A", "parents": ["B"] }
              ]
            }
            """);

        var plan = TagPortable.PlanMerge(existing, memberships, [], doc);
        Assert.Equal(0, plan.MembershipAdded);
        Assert.Equal(1, plan.MembershipRejected);
    }

    [Fact]
    public void PlanMerge_RejectsImplicationCycle()
    {
        var existing = new[]
        {
            new TagPortableTagSource { Id = "a", Name = "A" },
            new TagPortableTagSource { Id = "b", Name = "B" }
        };
        var implications = new[] { new TagPortableEdge { FromId = "a", ToId = "b" } };
        var doc = TagPortable.Parse("""
            {
              "schema": "palace-tags/1",
              "tags": [
                { "name": "B", "implies": ["A"] }
              ]
            }
            """);

        var plan = TagPortable.PlanMerge(existing, [], implications, doc);
        Assert.Equal(0, plan.ImplicationAdded);
        Assert.Equal(1, plan.ImplicationRejected);
    }

    [Fact]
    public void PlanMerge_DoesNotWipeUnrelated()
    {
        var existing = new[]
        {
            new TagPortableTagSource { Id = "keep", Name = "KeepMe", Priority = 9 }
        };
        var doc = TagPortable.Parse("""
            { "schema": "palace-tags/1", "tags": [ { "name": "NewTag" } ] }
            """);

        var plan = TagPortable.PlanMerge(existing, [], [], doc);
        Assert.Equal(1, plan.Created);
        Assert.Equal(0, plan.Updated);
        Assert.DoesNotContain(plan.Creates, c => c.Name == "KeepMe");
        Assert.DoesNotContain(plan.Updates, u => u.Name == "KeepMe");
    }

    [Fact]
    public void Parse_RejectsUnknownSchemaFamily()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TagPortable.Parse("""{ "schema": "other/1", "tags": [] }"""));
    }
}
