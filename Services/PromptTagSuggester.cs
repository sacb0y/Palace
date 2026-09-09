using System.Text.RegularExpressions;
using Palace.Models;

namespace Palace.Services;

public static class PromptTagSuggester
{
    private static readonly Regex Weight = new(@"^[\(\[]+|[:\|][\d.]+|[\)\]]+$", RegexOptions.Compiled);
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "masterpiece", "best quality", "high quality", "score_9", "score_8_up", "score_7_up",
        "break", "and", "the"
    };

    public static IReadOnlyList<PromptSuggestion> Suggest(string? prompt, IReadOnlyList<Tag> existing)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return [];
        }

        var byName = existing.ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<PromptSuggestion>();
        foreach (var raw in prompt.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = Weight.Replace(raw, "").Trim();
            token = token.Trim('"', '\'', ' ');
            if (token.Length < 2 || !seen.Add(token) || Skip.Contains(token))
            {
                continue;
            }

            byName.TryGetValue(token, out var tag);
            list.Add(new PromptSuggestion
            {
                Token = token,
                ExistingTagId = tag?.Id,
                ExistingTagName = tag?.Name
            });
            if (list.Count >= 24)
            {
                break;
            }
        }

        return list;
    }
}
