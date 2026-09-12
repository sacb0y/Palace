using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class GenerationFieldsTests
{
    [Fact]
    public void HasAny_FalseWhenAllBlank()
    {
        Assert.False(GenerationFields.HasAny(null, null, null, null));
        Assert.False(GenerationFields.HasAny("", "  ", "\t", null));
        Assert.False(GenerationFields.ForPreview(null, " ", null, "").HasAny);
    }

    [Theory]
    [InlineData("sdxl", null, null, null)]
    [InlineData(null, "a cat", null, null)]
    [InlineData(null, null, "blurry", null)]
    [InlineData(null, null, null, "123")]
    public void HasAny_TrueWhenAnyFieldSet(string? model, string? prompt, string? negative, string? seed)
    {
        Assert.True(GenerationFields.HasAny(model, prompt, negative, seed));
        Assert.True(GenerationFields.ForPreview(model, prompt, negative, seed).HasAny);
    }

    [Fact]
    public void ForPreview_TrimsAndClearsBlanks()
    {
        var snap = GenerationFields.ForPreview("  sdxl  ", " a cat ", "", "  ");
        Assert.Equal("sdxl", snap.Model);
        Assert.Equal("a cat", snap.Prompt);
        Assert.Null(snap.Negative);
        Assert.Null(snap.Seed);
        Assert.True(snap.HasAny);
    }

    [Fact]
    public void ForPreview_EmptyExtractReplacesPriorAiFields()
    {
        var prior = GenerationFields.ForPreview("sdxl", "a cat in space", "blurry", "42");
        Assert.True(prior.HasAny);

        var next = GenerationFields.ForPreview(null, null, null, null);
        Assert.Null(next.Model);
        Assert.Null(next.Prompt);
        Assert.Null(next.Negative);
        Assert.Null(next.Seed);
        Assert.False(next.HasAny);
        Assert.NotEqual(prior.Prompt, next.Prompt);
    }
}
