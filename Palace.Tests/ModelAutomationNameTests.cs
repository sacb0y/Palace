using Palace.Models;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// List / picker items without an explicit AutomationProperties.Name are announced by ToString();
/// the default is the type name (e.g. "Palace.Models.TagGroupPick").
/// </summary>
public sealed class ModelAutomationNameTests
{
    [Fact]
    public void Entities_read_as_their_names_not_their_type()
    {
        Assert.Equal("Cats", new Tag { Name = "Cats" }.ToString());
        Assert.Equal("Moodboard", new Room { Name = "Moodboard" }.ToString());
        Assert.Equal("Sacb0y", new Project { Name = "Sacb0y" }.ToString());
        Assert.Equal(@"D:\Art", new SourceFolder { Path = @"D:\Art" }.ToString());
    }

    [Fact]
    public void Prompt_suggestion_reads_as_the_existing_tag_or_the_token()
    {
        Assert.Equal("cat", new PromptSuggestion { Token = "cat" }.ToString());
        Assert.Equal("Cat", new PromptSuggestion { Token = "cat", ExistingTagId = "t1", ExistingTagName = "Cat" }.ToString());
    }

    [Theory]
    [InlineData(typeof(Tag))]
    [InlineData(typeof(Room))]
    [InlineData(typeof(Project))]
    [InlineData(typeof(SourceFolder))]
    [InlineData(typeof(PromptSuggestion))]
    public void Picker_entities_override_ToString(Type type) =>
        Assert.Equal(type, type.GetMethod(nameof(ToString), Type.EmptyTypes)!.DeclaringType);
}
