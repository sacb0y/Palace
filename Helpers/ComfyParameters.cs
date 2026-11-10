using System.Text.Json;

namespace Palace.Helpers;

/// <summary>
/// ComfyUI PNG <c>prompt</c> / <c>workflow</c> JSON → generation fields.
/// </summary>
public static class ComfyParameters
{
    public readonly record struct Parsed(
        string? Prompt,
        string? NegativePrompt,
        string? Model,
        string? Seed,
        string? Steps);

    public static Parsed ParsePromptJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            string? model = null;
            string? seed = null;
            string? steps = null;
            var prompts = new List<string>();
            foreach (var node in doc.RootElement.EnumerateObject())
            {
                if (node.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var classType = node.Value.TryGetProperty("class_type", out var ct) ? ct.GetString() : "";
                if (!node.Value.TryGetProperty("inputs", out var inputs))
                {
                    continue;
                }

                if (classType is "CLIPTextEncode" or "CLIPTextEncodeSDXL" or "CLIPTextEncodeFlux")
                {
                    if (inputs.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        var value = text.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            prompts.Add(value);
                        }
                    }
                }

                if (classType is "CheckpointLoaderSimple" or "CheckpointLoader")
                {
                    if (inputs.TryGetProperty("ckpt_name", out var ckpt) && ckpt.ValueKind == JsonValueKind.String)
                    {
                        model ??= Path.GetFileNameWithoutExtension(ckpt.GetString());
                    }
                }

                if (classType is "UNETLoader")
                {
                    if (inputs.TryGetProperty("unet_name", out var unet) && unet.ValueKind == JsonValueKind.String)
                    {
                        model ??= Path.GetFileNameWithoutExtension(unet.GetString());
                    }
                }

                if (classType is "KSampler" or "KSamplerAdvanced")
                {
                    if (inputs.TryGetProperty("seed", out var seedEl))
                    {
                        seed ??= seedEl.ToString();
                    }

                    if (inputs.TryGetProperty("steps", out var stepsEl))
                    {
                        steps ??= stepsEl.ToString();
                    }
                }
            }

            string? prompt = null;
            string? negative = null;
            if (prompts.Count > 0)
            {
                prompt = prompts[0];
                if (prompts.Count > 1)
                {
                    negative = prompts[^1];
                }
            }

            return new Parsed(prompt, negative, model, seed, steps);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public static Parsed ParseWorkflowJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
            {
                return default;
            }

            string? model = null;
            string? prompt = null;
            foreach (var node in nodes.EnumerateArray())
            {
                var type = node.TryGetProperty("type", out var t) ? t.GetString() : "";
                if (type is "CheckpointLoaderSimple" && node.TryGetProperty("widgets_values", out var widgets)
                    && widgets.ValueKind == JsonValueKind.Array && widgets.GetArrayLength() > 0)
                {
                    model ??= Path.GetFileNameWithoutExtension(widgets[0].GetString());
                }

                if (type is "CLIPTextEncode" && node.TryGetProperty("widgets_values", out var texts)
                    && texts.ValueKind == JsonValueKind.Array && texts.GetArrayLength() > 0)
                {
                    var value = texts[0].GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        prompt ??= value;
                    }
                }
            }

            return new Parsed(prompt, null, model, null, null);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
