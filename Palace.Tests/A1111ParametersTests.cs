using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class A1111ParametersTests
{
    [Fact]
    public void Parse_FullString_WithNegativeAndQuotedLoraHashes()
    {
        var text =
            "masterpiece, best quality, a red fox\n" +
            "Negative prompt: blurry, lowres, worst quality\n" +
            "Steps: 25, Sampler: Euler a, CFG scale: 7.5, Seed: 424242, Size: 512x768, " +
            "Model: realisticVision, " +
            "Lora hashes: \"foxLoRA: abcd1234, other: efgh5678\", " +
            "Wildcard prompt: \"forest, night\"";

        var parsed = A1111Parameters.Parse(text);

        Assert.Equal("masterpiece, best quality, a red fox", parsed.Prompt);
        Assert.Equal("blurry, lowres, worst quality", parsed.NegativePrompt);
        Assert.Equal("25", parsed.Steps);
        Assert.Equal("Euler a", parsed.Sampler);
        Assert.Equal("7.5", parsed.CfgScale);
        Assert.Equal("424242", parsed.Seed);
        Assert.Equal("512x768", parsed.Size);
        Assert.Equal("realisticVision", parsed.Model);
    }

    [Fact]
    public void Parse_EmptyPrompt_StartsAtNegative()
    {
        var text =
            "\nNegative prompt: bad anatomy\n" +
            "Steps: 20, Sampler: UniPC, Seed: 7, Model: sd15";

        var parsed = A1111Parameters.Parse(text);

        Assert.Equal("", parsed.Prompt);
        Assert.Equal("bad anatomy", parsed.NegativePrompt);
        Assert.Equal("7", parsed.Seed);
        Assert.Equal("20", parsed.Steps);
        Assert.Equal("UniPC", parsed.Sampler);
        Assert.Equal("sd15", parsed.Model);
    }

    [Fact]
    public void Parse_EmptyPrompt_StartsAtSteps()
    {
        var text = "Steps: 30, Seed: 1, Model: only_settings";
        var parsed = A1111Parameters.Parse(text);

        Assert.Equal("", parsed.Prompt);
        Assert.Null(parsed.NegativePrompt);
        Assert.Equal("1", parsed.Seed);
        Assert.Equal("only_settings", parsed.Model);
    }

    [Fact]
    public void SplitRespectingQuotes_KeepsCommasInsideQuotes()
    {
        var parts = A1111Parameters.SplitRespectingQuotes(
            "Steps: 1, Lora hashes: \"a: 1, b: 2\", Seed: 9");

        Assert.Equal(3, parts.Count);
        Assert.Equal("Steps: 1", parts[0].Trim());
        Assert.Equal("Lora hashes: \"a: 1, b: 2\"", parts[1].Trim());
        Assert.Equal("Seed: 9", parts[2].Trim());
    }

    [Fact]
    public void LooksLike_RequiresA1111Markers()
    {
        Assert.True(A1111Parameters.LooksLike("cat\nNegative prompt: dog\nSteps: 1"));
        Assert.True(A1111Parameters.LooksLike("Steps: 1, Seed: 2"));
        Assert.False(A1111Parameters.LooksLike("just a caption"));
        Assert.False(A1111Parameters.LooksLike(null));
    }
}
