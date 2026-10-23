using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class FolderSectionPrefsTests
{
    [Fact]
    public void FlattenAndCollapsed_KeysHashPath()
    {
        var path = Path.Combine("library", "Forest");
        var flatten = FolderSectionPrefs.FlattenKey(path);
        var collapsed = FolderSectionPrefs.CollapsedKey(path);
        Assert.StartsWith(FolderSectionPrefs.FlattenPrefix, flatten);
        Assert.StartsWith(FolderSectionPrefs.CollapsedPrefix, collapsed);
        Assert.False(FolderSectionPrefs.IsLegacySettingsKey(flatten));
        Assert.False(FolderSectionPrefs.IsLegacySettingsKey(collapsed));
        Assert.Equal(
            FolderSectionPrefs.PathHash(path),
            FolderSectionPrefs.PathHash(path + "\\"));
        Assert.NotEqual(FolderSectionPrefs.PathHash(path), FolderSectionPrefs.PathHash(path + "Other"));
    }

    [Fact]
    public void IsLegacySettingsKey_DetectsRawPathKeys()
    {
        Assert.True(FolderSectionPrefs.IsLegacySettingsKey(
            FolderSectionPrefs.FlattenPrefix + "c:/photos/forest"));
        Assert.True(FolderSectionPrefs.IsLegacySettingsKey(
            FolderSectionPrefs.CollapsedPrefix + @"c:\photos\forest"));
        // Bare 16-hex (pre-h prefix or raw path) is legacy — must not look like current hash.
        Assert.True(FolderSectionPrefs.IsLegacySettingsKey(
            FolderSectionPrefs.FlattenPrefix + "deadbeefdeadbeef"));
        Assert.False(FolderSectionPrefs.IsLegacySettingsKey(
            FolderSectionPrefs.FlattenKey(@"c:\photos\forest")));
        Assert.True(FolderSectionPrefs.IsCurrentHashSuffix(
            FolderSectionPrefs.PathHash(@"c:\photos\forest")));
        Assert.False(FolderSectionPrefs.IsLegacySettingsKey("OtherKey"));
    }

    [Fact]
    public void PruneLegacySettingsKeys_RemovesRawPathEntries()
    {
        var values = new Dictionary<string, object>
        {
            [FolderSectionPrefs.FlattenPrefix + "c:/old"] = true,
            [FolderSectionPrefs.FlattenPrefix + "deadbeefdeadbeef"] = true,
            [FolderSectionPrefs.FlattenKey(@"c:\new")] = true,
            ["Unrelated"] = 1
        };
        Assert.Equal(2, FolderSectionPrefs.PruneLegacySettingsKeys(values));
        Assert.False(values.ContainsKey(FolderSectionPrefs.FlattenPrefix + "c:/old"));
        Assert.False(values.ContainsKey(FolderSectionPrefs.FlattenPrefix + "deadbeefdeadbeef"));
        Assert.True(values.ContainsKey(FolderSectionPrefs.FlattenKey(@"c:\new")));
    }

    [Fact]
    public void EnsureLegacyKeysPruned_RunsOnceBehindFlag()
    {
        var values = new Dictionary<string, object>
        {
            [FolderSectionPrefs.FlattenPrefix + "c:/old"] = true
        };
        Assert.Equal(1, FolderSectionPrefs.EnsureLegacyKeysPruned(values, (k, v) => values[k] = v));
        Assert.True(FolderSectionPrefs.ParseBool(values[FolderSectionPrefs.LegacyPrunedFlagKey]));
        Assert.Equal(0, FolderSectionPrefs.EnsureLegacyKeysPruned(values, (k, v) => values[k] = v));
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
    public void PruneCollapsed_DropsStaleSectionPaths()
    {
        var collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            FolderSectionPrefs.NormalizePath(@"C:\Photos\Bunny"),
            FolderSectionPrefs.NormalizePath(@"C:\Photos\Gone")
        };
        var live = new[] { @"C:\Photos\Bunny", @"C:\Photos\Cave" };
        var pruned = FolderSectionPrefs.PruneCollapsed(collapsed, live);
        Assert.Single(pruned);
        Assert.True(FolderSectionPrefs.IsCollapsed(pruned, @"C:\Photos\Bunny"));
        Assert.False(FolderSectionPrefs.IsCollapsed(pruned, @"C:\Photos\Gone"));
    }

    [Fact]
    public void TrySave_ReturnsFalseOnThrow()
    {
        Assert.True(FolderSectionPrefs.TrySave(() => { }));
        Assert.False(FolderSectionPrefs.TrySave(() => throw new InvalidOperationException("disk")));
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

    [Fact]
    public void FilterCollapsedDisplay_OmitsTilesUnderCollapsedHeaders()
    {
        var ordered = new[]
        {
            H("A", collapsed: false),
            T("a1"),
            H("B", collapsed: true),
            T("b1"),
            T("b2"),
            H("C", collapsed: false),
            T("c1")
        };
        var display = FolderSectionPrefs.FilterCollapsedDisplay(
            ordered,
            item => item.Header,
            item => item.Collapsed);
        Assert.Equal(["A", "a1", "B", "C", "c1"], display.Select(i => i.Name).ToArray());
        // Full ordered list still has collapsed-section tiles for Review / select-all.
        Assert.Equal(7, ordered.Length);
        Assert.Equal(2, ordered.Count(i => !i.Header && i.Name.StartsWith('b')));
    }

    [Fact]
    public void ReviewAndSelectAll_CountCollapsedSectionTiles()
    {
        var ordered = new[]
        {
            H("A", collapsed: false),
            T("a1"),
            H("B", collapsed: true),
            T("b1"),
            T("b2"),
            H("C", collapsed: false),
            T("c1")
        };
        var display = FolderSectionPrefs.FilterCollapsedDisplay(
            ordered, i => i.Header, i => i.Collapsed);
        var displayAssets = display.Count(i => !i.Header);
        var orderedAssets = ordered.Count(i => !i.Header);
        Assert.Equal(2, displayAssets); // a1, c1
        Assert.Equal(4, orderedAssets); // a1, b1, b2, c1 — Review / select-all
        Assert.True(orderedAssets > displayAssets);
    }

    private static Item H(string name, bool collapsed) => new(name, true, collapsed);

    private static Item T(string name) => new(name, false, false);

    private sealed record Item(string Name, bool Header, bool Collapsed);
}
