using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class ShellPresetSelectionTests
{
    [Fact]
    public void KeyFor_UsesPresetId()
    {
        Assert.Equal("Default", ShellPresetSelection.KeyFor(ShellPresetKind.Default));
        Assert.Equal("Bokeh", ShellPresetSelection.KeyFor(ShellPresetKind.Bokeh));
        Assert.Equal("Custom", ShellPresetSelection.KeyFor(ShellPresetKind.Custom));
        Assert.Equal("recent:C:\\wall.jpg", ShellPresetSelection.KeyForRecent(@"C:\wall.jpg"));
    }

    [Fact]
    public void MatchCount_OnlyOneAcrossGroups()
    {
        // Same keys the picker buckets expose (chrome + Vivid + Creator + Custom).
        var keys = new[]
        {
            "Default", "Mica", "MicaAlt", "Neutral",
            "Ribbons", "Capsules", "Bokeh", "Nebula",
            "Viewport", "Sketchbook",
            "Custom",
            ShellPresetSelection.KeyForRecent(@"D:\recent.png")
        };

        Assert.Equal(1, ShellPresetSelection.MatchCount(keys, ShellPresetSelection.KeyFor(ShellPresetKind.Default)));
        Assert.Equal(1, ShellPresetSelection.MatchCount(keys, ShellPresetSelection.KeyFor(ShellPresetKind.Bokeh)));
        Assert.Equal(1, ShellPresetSelection.MatchCount(keys, ShellPresetSelection.KeyFor(ShellPresetKind.Sketchbook)));
        Assert.Equal(1, ShellPresetSelection.MatchCount(keys, ShellPresetSelection.KeyFor(ShellPresetKind.Custom)));
        Assert.Equal(1, ShellPresetSelection.MatchCount(keys, ShellPresetSelection.KeyForRecent(@"D:\recent.png")));
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
