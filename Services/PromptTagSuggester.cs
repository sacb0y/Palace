using Palace.Data;
using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>
/// From-prompt suggestion chips (audit k). Strips A1111 weights via
/// <see cref="TagNames.FromPromptToken"/>, resolves aliases, aggregates multi-select
/// with ≥2 files and ≥10% threshold.
/// </summary>
public static class PromptTagSuggester
{
    public const int Cap = 24;
    public const int MultiMinFileCount = 2;
    public const double MultiMinFraction = 0.10;

    // Keys are TagNames.Normalize form (_/- → space) so score_9 matches "score 9".
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "masterpiece", "best quality", "high quality",
        "score 9", "score 8 up", "score 7 up",
        "break", "and", "the"
    };

    /// <summary>Quality / boilerplate tokens skipped from chips (shown on the Ignored line).</summary>
    public static bool IsIgnoredQuality(string? token)
    {
        var key = TagNames.FromPromptToken(token);
        return key.Length > 0 && Skip.Contains(key);
    }

    public static bool PassesMultiThreshold(int matchCount, int selectionTotal)
    {
        if (selectionTotal <= 1)
        {
            return matchCount >= 1;
        }

        if (matchCount < MultiMinFileCount)
        {
            return false;
        }

        return matchCount >= selectionTotal * MultiMinFraction;
    }

    /// <summary>Single-file prompt suggestions.</summary>
    public static PromptSuggestResult Suggest(
        string? prompt,
        string assetId,
        IReadOnlyList<Tag> existing,
        IReadOnlyDictionary<string, string>? aliasNameToIdealName = null,
        IReadOnlySet<string>? dismissedNormalized = null) =>
        SuggestMany(
            [(assetId, prompt)],
            existing,
            aliasNameToIdealName,
            dismissedNormalized);

    /// <summary>
    /// Aggregate suggestions across a selection. Multi-select keeps a token only when it
    /// appears in ≥2 selected files and ≥10% of the selection.
    /// </summary>
    public static PromptSuggestResult SuggestMany(
        IReadOnlyList<(string AssetId, string? Prompt)> prompts,
        IReadOnlyList<Tag> existing,
        IReadOnlyDictionary<string, string>? aliasNameToIdealName = null,
        IReadOnlySet<string>? dismissedNormalized = null)
    {
        if (prompts is null || prompts.Count == 0)
        {
            return PromptSuggestResult.Empty;
        }

        var selectionTotal = prompts.Count;
        var byNormName = BuildTagLookup(existing);
        var aliasMap = BuildAliasLookup(aliasNameToIdealName);

        // Key = normalized resolve target (ideal name or stripped token).
        var buckets = new Dictionary<string, TokenBucket>(StringComparer.OrdinalIgnoreCase);
        var ignored = new List<string>();
        var ignoredSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (assetId, prompt) in prompts)
        {
            if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrEmpty(assetId))
            {
                continue;
            }

            var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in prompt.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var stripped = TagNames.StripPromptWeights(raw);
                var normalized = TagNames.Normalize(stripped);
                if (normalized.Length < 2)
                {
                    continue;
                }

                if (Skip.Contains(normalized))
                {
                    if (ignoredSeen.Add(normalized))
                    {
                        ignored.Add(normalized);
                    }

                    continue;
                }

                if (dismissedNormalized is not null
                    && dismissedNormalized.Any(d => TagNames.Same(d, normalized)))
                {
                    continue;
                }

                var resolved = TagNames.ResolvePromptToken(raw, aliasMap);
                if (resolved.Length < 2 || !seenInFile.Add(resolved))
                {
                    continue;
                }

                if (!buckets.TryGetValue(resolved, out var bucket))
                {
                    byNormName.TryGetValue(resolved, out var tag);
                    string? aliasSource = null;
                    if (tag is not null && !TagNames.Same(normalized, tag.Name))
                    {
                        aliasSource = normalized;
                    }
                    else if (tag is null && !TagNames.Same(normalized, resolved))
                    {
                        // Alias map hit but ideal tag row missing — still show resolved face.
                        aliasSource = normalized;
                    }

                    bucket = new TokenBucket
                    {
                        Key = resolved,
                        RawToken = raw.Trim(),
                        Token = normalized,
                        AliasSourceName = aliasSource,
                        ExistingTagId = tag?.Id,
                        ExistingTagName = tag?.Name ?? (aliasSource is null ? null : resolved),
                        TagColor = tag?.Color
                    };
                    // When Resolve mapped to an ideal name that exists under a different spelling.
                    if (bucket.ExistingTagId is null && byNormName.TryGetValue(resolved, out var byResolved))
                    {
                        bucket.ExistingTagId = byResolved.Id;
                        bucket.ExistingTagName = byResolved.Name;
                        bucket.TagColor = byResolved.Color;
                        if (!TagNames.Same(normalized, byResolved.Name))
                        {
                            bucket.AliasSourceName = normalized;
                        }
                    }

                    buckets[resolved] = bucket;
                }
                else if (bucket.RawToken.Length == 0)
                {
                    bucket.RawToken = raw.Trim();
                }

                bucket.AssetIds.Add(assetId);
            }
        }

        var list = new List<PromptSuggestion>();
        foreach (var bucket in buckets.Values)
        {
            var matchCount = bucket.AssetIds.Count;
            if (!PassesMultiThreshold(matchCount, selectionTotal))
            {
                continue;
            }

            list.Add(new PromptSuggestion
            {
                RawToken = bucket.RawToken.Length > 0 ? bucket.RawToken : bucket.Token,
                Token = bucket.Token,
                AliasSourceName = bucket.AliasSourceName,
                ExistingTagId = bucket.ExistingTagId,
                ExistingTagName = bucket.ExistingTagName,
                TagColor = bucket.TagColor,
                MatchCount = matchCount,
                SelectionTotal = selectionTotal,
                AssetIds = bucket.AssetIds.ToList()
            });
        }

        // Known first (by match count desc, then name); then + new. Cap 24.
        var ordered = list
            .OrderByDescending(s => s.IsKnown)
            .ThenByDescending(s => s.MatchCount)
            .ThenBy(s => s.FaceLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.FaceLabel, StringComparer.Ordinal)
            .Take(Cap)
            .ToList();

        return new PromptSuggestResult
        {
            Suggestions = ordered,
            IgnoredTokens = ignored
        };
    }

    private static Dictionary<string, Tag> BuildTagLookup(IReadOnlyList<Tag> existing)
    {
        var map = new Dictionary<string, Tag>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in existing ?? [])
        {
            var key = TagNames.Normalize(tag.Name);
            if (key.Length == 0 || map.ContainsKey(key))
            {
                continue;
            }

            map[key] = tag;
        }

        return map;
    }

    private static Dictionary<string, string> BuildAliasLookup(
        IReadOnlyDictionary<string, string>? aliasNameToIdealName)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (aliasNameToIdealName is null)
        {
            return map;
        }

        foreach (var (alias, ideal) in aliasNameToIdealName)
        {
            var a = TagNames.Normalize(alias);
            var i = TagNames.Normalize(ideal);
            if (a.Length == 0 || i.Length == 0)
            {
                continue;
            }

            map[a] = i;
        }

        return map;
    }

    private sealed class TokenBucket
    {
        public string Key { get; set; } = "";
        public string RawToken { get; set; } = "";
        public string Token { get; set; } = "";
        public string? AliasSourceName { get; set; }
        public string? ExistingTagId { get; set; }
        public string? ExistingTagName { get; set; }
        public string? TagColor { get; set; }
        public HashSet<string> AssetIds { get; } = new(StringComparer.Ordinal);
    }
}

public sealed class PromptSuggestResult
{
    public static PromptSuggestResult Empty { get; } = new()
    {
        Suggestions = [],
        IgnoredTokens = []
    };

    public IReadOnlyList<PromptSuggestion> Suggestions { get; init; } = [];
    public IReadOnlyList<string> IgnoredTokens { get; init; } = [];
}
