using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

public sealed class PromptTagSuggesterTests
{
    private static List<Tag> SampleTags() =>
    [
        new Tag { Id = "t-long", Name = "long hair", Color = "#F97316" },
        new Tag { Id = "t-brown", Name = "brown hair", Color = "#F97316" },
        new Tag { Id = "t-smile", Name = "smile", Color = "#14B8A6" },
        new Tag { Id = "t-alias", Name = "brunette", Color = "#888888" }
    ];

    private static Dictionary<string, string> BrunetteAlias() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["brunette"] = "brown hair"
        };

    [Fact]
    public void Suggest_UsesFromPromptToken_AndAliasResolve()
    {
        var result = PromptTagSuggester.Suggest(
            "masterpiece, (brunette:1.1), (long_hair:1.2), [smile], {city lights}, denim jacket, score_9",
            "a1",
            SampleTags(),
            BrunetteAlias());

        Assert.Contains("masterpiece", result.IgnoredTokens);
        Assert.Contains("score 9", result.IgnoredTokens);

        var known = result.Suggestions.Where(s => s.IsKnown).ToList();
        Assert.Contains(known, s => s.ExistingTagId == "t-brown" && s.AliasSourceName == "brunette");
        Assert.Contains(known, s => s.ExistingTagId == "t-long" && s.RawToken.Contains("long_hair", StringComparison.Ordinal));
        Assert.Contains(known, s => s.ExistingTagId == "t-smile");

        Assert.Contains(result.Suggestions, s => s.IsNew && s.Token == "city lights");
        Assert.Contains(result.Suggestions, s => s.IsNew && s.Token == "denim jacket");
    }

    [Fact]
    public void SuggestMany_RequiresTwoFilesAndTenPercent()
    {
        var tags = SampleTags();
        var prompts = new List<(string, string?)>();
        for (var i = 0; i < 24; i++)
        {
            // rain only on 1 file → below ≥2 and ≥10%
            // black hair on 18 → keep
            // neon on 2 → exactly 2 and ~8.3% → fail 10%
            // bokeh on 3 → 12.5% → keep
            var parts = new List<string> { "masterpiece" };
            if (i < 18)
            {
                parts.Add("black_hair");
            }

            if (i < 2)
            {
                parts.Add("neon reflections");
            }

            if (i < 3)
            {
                parts.Add("bokeh");
            }

            if (i == 0)
            {
                parts.Add("rain");
            }

            prompts.Add(($"a{i}", string.Join(", ", parts)));
        }

        // Add black hair / rain / bokeh as existing so Accept-all counts known.
        tags.Add(new Tag { Id = "t-black", Name = "black hair" });
        tags.Add(new Tag { Id = "t-rain", Name = "rain" });
        tags.Add(new Tag { Id = "t-bokeh", Name = "bokeh" });

        var result = PromptTagSuggester.SuggestMany(prompts, tags);
        Assert.DoesNotContain(result.Suggestions, s => TagNames.Same(s.Token, "rain"));
        Assert.DoesNotContain(result.Suggestions, s => TagNames.Same(s.Token, "neon reflections"));
        var black = Assert.Single(result.Suggestions, s => s.ExistingTagId == "t-black");
        Assert.Equal(18, black.MatchCount);
        Assert.Equal(24, black.SelectionTotal);
        Assert.Equal(18, black.AssetIds.Count);
        Assert.Contains(result.Suggestions, s => s.ExistingTagId == "t-bokeh" && s.MatchCount == 3);
    }

    [Fact]
    public void PassesMultiThreshold_MatchesDecision()
    {
        Assert.True(PromptTagSuggester.PassesMultiThreshold(1, 1));
        Assert.False(PromptTagSuggester.PassesMultiThreshold(1, 24));
        Assert.False(PromptTagSuggester.PassesMultiThreshold(2, 24)); // 8.3%
        Assert.True(PromptTagSuggester.PassesMultiThreshold(3, 24)); // 12.5%
        Assert.True(PromptTagSuggester.PassesMultiThreshold(18, 24));
    }

    [Fact]
    public void Suggest_SkipsDismissedTokens()
    {
        var dismissed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "denim jacket" };
        var result = PromptTagSuggester.Suggest(
            "long_hair, denim jacket",
            "a1",
            SampleTags(),
            dismissedNormalized: dismissed);
        Assert.DoesNotContain(result.Suggestions, s => TagNames.Same(s.Token, "denim jacket"));
        Assert.Contains(result.Suggestions, s => s.ExistingTagId == "t-long");
    }

    [Fact]
    public void FaceLabel_ShowsAliasArrow()
    {
        var chip = new PromptSuggestion
        {
            Token = "brunette",
            AliasSourceName = "brunette",
            ExistingTagId = "t-brown",
            ExistingTagName = "brown hair"
        };
        Assert.Equal("brunette › brown hair", chip.FaceLabel);
        Assert.Equal("brown hair", chip.ToString());
    }

    [Fact]
    public void ChipTooltip_MultiKeepsRawToResolved()
    {
        var chip = new PromptSuggestion
        {
            RawToken = "(long_hair:1.2)",
            Token = "long hair",
            ExistingTagId = "t-long",
            ExistingTagName = "long hair",
            MatchCount = 18,
            SelectionTotal = 24
        };
        Assert.Contains("(long_hair:1.2) → long hair", chip.ChipTooltip, StringComparison.Ordinal);
        Assert.Contains("18 of 24", chip.ChipTooltip, StringComparison.Ordinal);
    }
}

public sealed class DismissedPromptTokensTests
{
    [Fact]
    public void Remember_NormalizesAndCaps()
    {
        var next = DismissedPromptTokens.Remember([], "Long_Hair");
        Assert.Equal(["Long Hair"], next);
        Assert.True(DismissedPromptTokens.Contains(next, "(long_hair:1.2)"));
        var many = DismissedPromptTokens.RememberMany(next, ["bokeh", "Long Hair"]);
        // Last remembered wins the front slot.
        Assert.Equal("Long Hair", many[0]);
        Assert.Equal(["Long Hair", "bokeh"], many);
    }

    [Fact]
    public void ParseSerialize_RoundTrip()
    {
        var parsed = DismissedPromptTokens.Parse("long_hair, bokeh; long hair");
        Assert.Equal(["long hair", "bokeh"], parsed);
        Assert.Equal("long hair,bokeh", DismissedPromptTokens.Serialize(parsed));
    }
}
