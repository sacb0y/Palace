using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ComfyParametersTests
{
    [Fact]
    public void ParsePromptJson_ReadsClipText_Checkpoint_AndSampler()
    {
        const string json = """
            {
              "3": {
                "class_type": "KSampler",
                "inputs": { "seed": 99, "steps": 20 }
              },
              "4": {
                "class_type": "CheckpointLoaderSimple",
                "inputs": { "ckpt_name": "models/sdxl.safetensors" }
              },
              "6": {
                "class_type": "CLIPTextEncode",
                "inputs": { "text": "a red fox" }
              },
              "7": {
                "class_type": "CLIPTextEncode",
                "inputs": { "text": "blurry" }
              }
            }
            """;

        var parsed = ComfyParameters.ParsePromptJson(json);
        Assert.Equal("a red fox", parsed.Prompt);
        Assert.Equal("blurry", parsed.NegativePrompt);
        Assert.Equal("sdxl", parsed.Model);
        Assert.Equal("99", parsed.Seed);
        Assert.Equal("20", parsed.Steps);
    }

    [Fact]
    public void ParsePromptJson_Invalid_ReturnsDefault()
    {
        Assert.Equal(default, ComfyParameters.ParsePromptJson("{not json"));
        Assert.Equal(default, ComfyParameters.ParsePromptJson(null));
    }

    [Fact]
    public void ParseWorkflowJson_ReadsNodes()
    {
        const string json = """
            {
              "nodes": [
                { "type": "CheckpointLoaderSimple", "widgets_values": ["pony.safetensors"] },
                { "type": "CLIPTextEncode", "widgets_values": ["portrait"] }
              ]
            }
            """;

        var parsed = ComfyParameters.ParseWorkflowJson(json);
        Assert.Equal("pony", parsed.Model);
        Assert.Equal("portrait", parsed.Prompt);
    }
}
