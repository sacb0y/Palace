using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class RecentCustomBackgroundsTests
{
    private static RecentCustomBackgroundEntry E(
        string id,
        string file,
        string? source = null,
        double darkness = 40,
        double blur = 18,
        string? name = null,
        string? hash = null) =>
        RecentCustomBackgrounds.Create(
            file,
            name ?? file,
            source,
            darkness,
            blur,
            hash ?? "hash-" + id,
            DateTimeOffset.UtcNow,
            id);

    [Fact]
    public void Push_puts_newest_first_and_dedupes_by_hash()
    {
        var a = E("a", "shell-recent-a.jpg", hash: "same");
        var b = E("b", "shell-recent-b.jpg", hash: "other");
        var again = E("c", "shell-recent-c.jpg", hash: "same", darkness: 70, blur: 10, name: "wall.png");

        var (list, evicted) = RecentCustomBackgrounds.Push([a, b], again);

        Assert.Empty(evicted);
        Assert.Equal(["a", "b"], list.Select(e => e.Id).ToArray());
        Assert.Equal(70, list[0].Darkness);
        Assert.Equal(10, list[0].Blur);
        Assert.Equal("wall.png", list[0].DisplayName);
        Assert.Equal("shell-recent-a.jpg", list[0].FileName);
    }

    [Fact]
    public void Push_caps_at_five_dropping_oldest_non_active()
    {
        IReadOnlyList<RecentCustomBackgroundEntry> list = [];
        for (var i = 0; i < 5; i++)
        {
            (list, _) = RecentCustomBackgrounds.Push(list, E($"id{i}", $"shell-recent-{i}.jpg"));
        }

        Assert.Equal(RecentCustomBackgrounds.Max, list.Count);
        Assert.Equal(5, RecentCustomBackgrounds.Max);

        var (capped, evicted) = RecentCustomBackgrounds.Push(
            list,
            E("id5", "shell-recent-5.jpg"),
            activeId: "id4");

        Assert.Equal(RecentCustomBackgrounds.Max, capped.Count);
        Assert.Single(evicted);
        Assert.Equal("id0", evicted[0].Id);
        Assert.Equal("id5", capped[0].Id);
        Assert.Contains(capped, e => e.Id == "id4");
    }

    [Fact]
    public void Remove_drops_by_id()
    {
        var list = RecentCustomBackgrounds.Remove(
            [E("a", "shell-recent-a.jpg"), E("b", "shell-recent-b.jpg")],
            "a");

        Assert.Equal(["b"], list.Select(e => e.Id).ToArray());
    }

    [Fact]
    public void UpdateAmounts_rewrites_one_entry_and_touches_last_used()
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(-5);
        var seeded = E("b", "shell-recent-b.jpg") with { LastUsedUtc = before };
        var list = RecentCustomBackgrounds.UpdateAmounts(
            [E("a", "shell-recent-a.jpg"), seeded],
            "b",
            12,
            28);

        Assert.Equal(40, list[0].Darkness);
        Assert.Equal(12, list[1].Darkness);
        Assert.Equal(28, list[1].Blur);
        Assert.True(list[1].LastUsedUtc > before);
    }

    [Fact]
    public void TouchLastUsed_updates_one_entry()
    {
        var stamp = DateTimeOffset.Parse("2026-10-20T12:00:00Z");
        var list = RecentCustomBackgrounds.TouchLastUsed(
            [E("a", "shell-recent-a.jpg"), E("b", "shell-recent-b.jpg")],
            "a",
            stamp);

        Assert.Equal(stamp, list[0].LastUsedUtc);
    }

    [Fact]
    public void Serialize_Parse_round_trip()
    {
        var (original, _) = RecentCustomBackgrounds.Push(
            [],
            E("abc", "shell-recent-abc.png", @"C:\pics\wall.png", 33, 22, "wall.png", "deadbeef"));

        var roundTrip = RecentCustomBackgrounds.Parse(RecentCustomBackgrounds.Serialize(original));

        Assert.Single(roundTrip);
        Assert.Equal("abc", roundTrip[0].Id);
        Assert.Equal("shell-recent-abc.png", roundTrip[0].FileName);
        Assert.Equal("wall.png", roundTrip[0].DisplayName);
        Assert.Equal(@"C:\pics\wall.png", roundTrip[0].SourcePath);
        Assert.Equal(33, roundTrip[0].Darkness);
        Assert.Equal(22, roundTrip[0].Blur);
        Assert.Equal("deadbeef", roundTrip[0].ContentHash);
        Assert.NotEqual(default, roundTrip[0].LastUsedUtc);
        Assert.Empty(RecentCustomBackgrounds.Parse(null));
        Assert.Empty(RecentCustomBackgrounds.Parse("{not-json"));
    }

    [Fact]
    public void Parse_MigratesLegacyPercentBlurAboveMaxPx()
    {
        var json = """[{"id":"x","file":"shell-recent-x.jpg","name":"x","darkness":40,"blur":45,"hash":"aa","added":"2026-10-01T00:00:00Z"}]""";
        var list = RecentCustomBackgrounds.Parse(json);
        Assert.Single(list);
        Assert.Equal(ShellBackground.NormalizeBlur(45), list[0].Blur, 2);
        Assert.True(list[0].Blur <= ShellBackground.MaxBlurPx);
    }

    [Fact]
    public void ResolveImagePath_prefers_cache_then_source_then_null()
    {
        var folder = Path.Combine(Path.GetTempPath(), "palace-recent-custom-tests");
        var entry = E("x", "shell-recent-x.jpg", Path.Combine(folder, "source.jpg"));
        var cache = RecentCustomBackgrounds.CachePath(folder, entry.FileName);
        var exists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Assert.Null(RecentCustomBackgrounds.ResolveImagePath(entry, folder, exists.Contains));
        Assert.True(RecentCustomBackgrounds.IsBroken(entry, folder, exists.Contains));

        exists.Add(entry.SourcePath!);
        Assert.Equal(entry.SourcePath, RecentCustomBackgrounds.ResolveImagePath(entry, folder, exists.Contains));

        exists.Add(cache);
        Assert.Equal(cache, RecentCustomBackgrounds.ResolveImagePath(entry, folder, exists.Contains));
        Assert.False(RecentCustomBackgrounds.IsBroken(entry, folder, exists.Contains));
    }

    [Fact]
    public void DisplayLabel_strips_extension()
    {
        Assert.Equal("wall", RecentCustomBackgrounds.DisplayLabel("wall.png"));
        Assert.Equal("photo", RecentCustomBackgrounds.DisplayLabel(@"C:\pics\photo.JPEG"));
    }

    [Fact]
    public void ShouldKeepStoredFile_only_when_listed()
    {
        var listed = E("a", "shell-recent-a.jpg");
        Assert.True(RecentCustomBackgrounds.ShouldKeepStoredFile(
            @"C:\LocalState\shell-recent-a.jpg",
            [listed]));
        Assert.False(RecentCustomBackgrounds.ShouldKeepStoredFile(
            @"C:\LocalState\shell-recent-gone.jpg",
            [listed]));
        Assert.False(RecentCustomBackgrounds.ShouldKeepStoredFile(
            @"C:\LocalState\shell-wallpaper-old.jpg",
            [listed]));
    }
}

public sealed class ShellPresetSelectionTests
{
    [Fact]
    public void KeyFor_UsesPresetId_And_Recent()
    {
        Assert.Equal("Default", ShellPresetSelection.KeyFor(ShellPresetKind.Default));
        Assert.Equal("Bokeh", ShellPresetSelection.KeyFor(ShellPresetKind.Bokeh));
        Assert.Equal("Custom", ShellPresetSelection.KeyFor(ShellPresetKind.Custom));
        Assert.Equal("recent:abc", ShellPresetSelection.KeyForRecent("abc"));
    }

    [Fact]
    public void MatchCount_OnlyOneAcrossGroupsIncludingRecent()
    {
        var keys = new[]
        {
            "Default", "Mica", "MicaAlt", "Neutral",
            "Ribbons", "Capsules", "Bokeh", "Nebula",
            "Viewport", "Sketchbook",
            "Custom",
            ShellPresetSelection.KeyForRecent("abc")
        };

        Assert.Equal(1, ShellPresetSelection.MatchCount(keys, ShellPresetSelection.KeyFor(ShellPresetKind.Default)));
        Assert.Equal(1, ShellPresetSelection.MatchCount(keys, ShellPresetSelection.KeyFor(ShellPresetKind.Bokeh)));
        Assert.Equal(1, ShellPresetSelection.MatchCount(keys, ShellPresetSelection.KeyFor(ShellPresetKind.Sketchbook)));
        Assert.Equal(1, ShellPresetSelection.MatchCount(keys, ShellPresetSelection.KeyFor(ShellPresetKind.Custom)));
        Assert.Equal(1, ShellPresetSelection.MatchCount(keys, ShellPresetSelection.KeyForRecent("abc")));
        Assert.Equal(0, ShellPresetSelection.MatchCount(keys, ShellPresetSelection.KeyForRecent("nope")));
        Assert.Equal(0, ShellPresetSelection.MatchCount(keys, ShellPresetSelection.KeyFor(ShellPresetKind.Aurora)));
    }

    [Fact]
    public void Matches_IsCaseInsensitive()
    {
        Assert.True(ShellPresetSelection.Matches("bokeh", "Bokeh"));
        Assert.False(ShellPresetSelection.Matches("Bokeh", "Custom"));
        Assert.True(ShellPresetSelection.Matches("Default", null));
    }
}
