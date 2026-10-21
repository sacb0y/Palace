using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class FolderSectionPrefsTests
{
    [Fact]
    public void FlattenAndCollapsed_RoundTripKeys()
    {
        var path = Path.Combine("library", "Forest");
        Assert.StartsWith(FolderSectionPrefs.FlattenPrefix, FolderSectionPrefs.FlattenKey(path));
        Assert.StartsWith(FolderSectionPrefs.CollapsedPrefix, FolderSectionPrefs.CollapsedKey(path));
        Assert.Equal(
            FolderSectionPrefs.NormalizePath(path),
            FolderSectionPrefs.NormalizePath(path + "\\"));
    }

    [Fact]
    public void ParseCollapsed_SplitsAndNormalizes()
    {
        var raw = "C:\\Photos\\Bunny\n  c:/photos/cave  \n\n";
        var set = FolderSectionPrefs.ParseCollapsed(raw);
        Assert.Equal(2, set.Count);
        Assert.True(FolderSectionPrefs.IsCollapsed(set, @"C:\Photos\Bunny"));
        Assert.True(FolderSectionPrefs.IsCollapsed(set, @"C:\Photos\Cave"));
        Assert.False(FolderSectionPrefs.IsCollapsed(set, @"C:\Photos\Lake"));
    }

    [Fact]
    public void ToggleCollapsed_AddsAndRemoves()
    {
        var path = Path.Combine("library", "Bunny");
        var once = FolderSectionPrefs.ToggleCollapsed(new HashSet<string>(StringComparer.OrdinalIgnoreCase), path);
        Assert.True(FolderSectionPrefs.IsCollapsed(once, path));
        var twice = FolderSectionPrefs.ToggleCollapsed(once, path);
        Assert.False(FolderSectionPrefs.IsCollapsed(twice, path));
        Assert.Equal("", FolderSectionPrefs.SerializeCollapsed(twice));
    }

    [Fact]
    public void IsSmallSection_UsesPackFraction()
    {
        // Three square tiles at 140px ≈ 436 wide; 60% of 800 = 480 → small.
        Assert.True(FolderSectionPrefs.IsSmallSection([1, 1, 1], 800, 140, 8));
        // Ten squares fill more than 60% of 800.
        Assert.False(FolderSectionPrefs.IsSmallSection(
            Enumerable.Repeat(1.0, 10).ToList(), 800, 140, 8));
    }

    [Fact]
    public void ParseBool_HandlesCommonStoredShapes()
    {
        Assert.True(FolderSectionPrefs.ParseBool(true));
        Assert.False(FolderSectionPrefs.ParseBool(false, defaultValue: true));
        Assert.True(FolderSectionPrefs.ParseBool("true"));
        Assert.False(FolderSectionPrefs.ParseBool("nope", defaultValue: false));
        Assert.True(FolderSectionPrefs.ParseBool(1));
    }
}
