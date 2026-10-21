using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// Single-selection keys for the Settings background picker. Multiple GridView
/// groups (chrome / Vivid / Creator / Custom) share one active tile via
/// <see cref="ShellPresetItem.IsSelected"/> / these keys. Each GridView still
/// uses <c>SelectionMode=Single</c> so UIA Select fires SelectionChanged and
/// applies the background; SettingsPage clears sibling SelectedItems.
/// </summary>
public static class ShellPresetSelection
{
    public static string KeyFor(ShellPresetKind kind) =>
        ShellBackground.PresetId(ShellBackground.NormalizePreset(kind));

    /// <summary>
    /// Recent custom tile key. Prefer a stable entry id so renames / path moves
    /// do not deselect the tile. Path is accepted for blur-PR call sites.
    /// </summary>
    public static string KeyForRecent(string? idOrPath) =>
        "recent:" + (idOrPath ?? "").Trim();

    public static bool Matches(string? itemKey, string? activeKey)
    {
        var want = string.IsNullOrWhiteSpace(activeKey)
            ? KeyFor(ShellPresetKind.Default)
            : activeKey.Trim();
        var have = string.IsNullOrWhiteSpace(itemKey) ? "" : itemKey.Trim();
        return string.Equals(have, want, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>How many keys match — assert single-select across flattened groups.</summary>
    public static int MatchCount(IEnumerable<string> itemKeys, string? activeKey)
    {
        var n = 0;
        foreach (var key in itemKeys)
        {
            if (Matches(key, activeKey))
            {
                n++;
            }
        }

        return n;
    }
}
