using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// Single-selection keys for the Settings background picker. Multiple GridView
/// groups (chrome / Vivid / Creator / Custom) must share one active tile —
/// per-group <c>SelectedItem</c> cannot. Shared contract with the blur PR (0.0.13).
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
