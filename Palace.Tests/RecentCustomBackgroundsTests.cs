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
        double blur = 45,
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
    public void Push_caps_at_six_dropping_oldest_non_active()
    {
        IReadOnlyList<RecentCustomBackgroundEntry> list = [];
        for (var i = 0; i < 6; i++)
        {
            (list, _) = RecentCustomBackgrounds.Push(list, E($"id{i}", $"shell-recent-{i}.jpg"));
        }

        Assert.Equal(RecentCustomBackgrounds.Max, list.Count);

        var (capped, evicted) = RecentCustomBackgrounds.Push(
            list,
            E("id6", "shell-recent-6.jpg"),
            activeId: "id5");

        Assert.Equal(RecentCustomBackgrounds.Max, capped.Count);
        Assert.Single(evicted);
        Assert.Equal("id0", evicted[0].Id);
        Assert.Equal("id6", capped[0].Id);
        Assert.Contains(capped, e => e.Id == "id5");
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
    public void UpdateAmounts_rewrites_one_entry()
    {
        var list = RecentCustomBackgrounds.UpdateAmounts(
            [E("a", "shell-recent-a.jpg"), E("b", "shell-recent-b.jpg")],
            "b",
            12,
            88);

        Assert.Equal(40, list[0].Darkness);
        Assert.Equal(12, list[1].Darkness);
        Assert.Equal(88, list[1].Blur);
    }

    [Fact]
    public void Serialize_Parse_round_trip()
    {
        var (original, _) = RecentCustomBackgrounds.Push(
            [],
            E("abc", "shell-recent-abc.png", @"C:\pics\wall.png", 33, 66, "wall.png", "deadbeef"));

        var roundTrip = RecentCustomBackgrounds.Parse(RecentCustomBackgrounds.Serialize(original));

        Assert.Single(roundTrip);
        Assert.Equal("abc", roundTrip[0].Id);
        Assert.Equal("shell-recent-abc.png", roundTrip[0].FileName);
        Assert.Equal("wall.png", roundTrip[0].DisplayName);
        Assert.Equal(@"C:\pics\wall.png", roundTrip[0].SourcePath);
        Assert.Equal(33, roundTrip[0].Darkness);
        Assert.Equal(66, roundTrip[0].Blur);
        Assert.Equal("deadbeef", roundTrip[0].ContentHash);
        Assert.Empty(RecentCustomBackgrounds.Parse(null));
        Assert.Empty(RecentCustomBackgrounds.Parse("{not-json"));
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
        Assert.Equal(1, ShellPresetSelection.MatchCount(keys, ShellPresetSelection.KeyForRecent("abc")));
        Assert.Equal(0, ShellPresetSelection.MatchCount(keys, ShellPresetSelection.KeyForRecent("nope")));
    }

    [Fact]
    public void Matches_IsCaseInsensitive()
    {
        Assert.True(ShellPresetSelection.Matches("bokeh", "Bokeh"));
        Assert.False(ShellPresetSelection.Matches("Bokeh", "Custom"));
        Assert.True(ShellPresetSelection.Matches("Default", null));
    }
}
