using System.Text;
using System.Text.RegularExpressions;

namespace Palace.Helpers;

/// <summary>
/// Automatic1111 / Forge "parameters" text: prompt, optional Negative prompt
/// block, then a final <c>Steps: …, Sampler: …, Seed: …</c> key/value line.
/// Empty positive prompt is valid (text may start with a newline or Steps).
/// </summary>
public static class A1111Parameters
{
    private static readonly Regex FooterKey = new(
        @"^(?<key>[A-Za-z][A-Za-z0-9 ]*?):\s*",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public readonly record struct Parsed(
        string Prompt,
        string? NegativePrompt,
        string? Seed,
        string? Model,
        string? Sampler,
        string? Steps,
        string? CfgScale,
        string? Size);

    public static Parsed Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new Parsed("", null, null, null, null, null, null, null);
        }

        // Writers sometimes prefix with a BOM after UNICODE decode.
        var body = text.TrimStart('\uFEFF');

        var negIdx = IndexOfLineKey(body, "Negative prompt:");
        var stepsIdx = IndexOfStepsLine(body);

        string prompt;
        string? negative = null;
        if (negIdx >= 0)
        {
            prompt = body[..negIdx].Trim();
            var negEnd = stepsIdx > negIdx ? stepsIdx : body.Length;
            negative = body[(negIdx + "Negative prompt:".Length)..negEnd].Trim();
            if (negative.Length == 0)
            {
                negative = null;
            }
        }
        else if (stepsIdx >= 0)
        {
            prompt = body[..stepsIdx].Trim();
        }
        else
        {
            prompt = body.Trim();
        }

        var footer = stepsIdx >= 0 ? body[stepsIdx..].TrimStart() : "";
        var fields = ParseFooterFields(footer);

        return new Parsed(
            prompt,
            negative,
            GetField(fields, "Seed"),
            GetField(fields, "Model"),
            GetField(fields, "Sampler"),
            GetField(fields, "Steps"),
            GetField(fields, "CFG scale") ?? GetField(fields, "CFG Scale"),
            GetField(fields, "Size"));
    }

    /// <summary>True when the text looks like A1111 parameters (not a free caption).</summary>
    public static bool LooksLike(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && (IndexOfLineKey(text, "Negative prompt:") >= 0
            || IndexOfLineKey(text, "Steps:") >= 0);

    /// <summary>Index of <paramref name="key"/> at start-of-line only (not mid-string).</summary>
    public static int IndexOfLineKey(string text, string key)
    {
        if (text.StartsWith(key, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\n' or '\r'))
            {
                continue;
            }

            var next = i + 1;
            if (next < text.Length && text[i] == '\r' && text[next] == '\n')
            {
                next++;
            }

            if (next < text.Length
                && text.AsSpan(next).StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                return next;
            }
        }

        return -1;
    }

    private static int IndexOfStepsLine(string text) => IndexOfLineKey(text, "Steps:");

    private static Dictionary<string, string> ParseFooterFields(string footer)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(footer))
        {
            return result;
        }

        // Take the first line of the footer (A1111 packs settings on one line).
        var lineEnd = footer.IndexOfAny(['\r', '\n']);
        var line = lineEnd >= 0 ? footer[..lineEnd] : footer;

        var parts = SplitRespectingQuotes(line);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var match = FooterKey.Match(trimmed);
            if (!match.Success)
            {
                continue;
            }

            var key = match.Groups["key"].Value.Trim();
            var value = trimmed[match.Length..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            if (key.Length > 0 && !result.ContainsKey(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Split on commas that are outside double quotes so Lora hashes /
    /// Wildcard prompt values keep their internal commas.
    /// </summary>
    public static List<string> SplitRespectingQuotes(string line)
    {
        var parts = new List<string>();
        if (string.IsNullOrEmpty(line))
        {
            return parts;
        }

        var sb = new StringBuilder(line.Length);
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                sb.Append(c);
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                parts.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        parts.Add(sb.ToString());
        return parts;
    }

    private static string? GetField(Dictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
}
