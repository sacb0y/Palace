namespace Palace.Helpers;

/// <summary>
/// Overlay / gallery shortcut copy for Settings → Input. Off WinUI so the list is testable.
/// </summary>
public sealed class ShortcutHint
{
    public ShortcutHint(string keys, string action, string automationId)
    {
        Keys = keys;
        Action = action;
        AutomationId = automationId;
        Line = $"{keys}  {action}";
        KeyParts = SplitKeyParts(keys);
    }

    public string Keys { get; }

    public string Action { get; }

    public string AutomationId { get; }

    public string Line { get; }

    /// <summary>Chord segments for key-chip UI (<c>Ctrl+1</c> → Ctrl, 1).</summary>
    public IReadOnlyList<string> KeyParts { get; }

    public static IReadOnlyList<string> SplitKeyParts(string keys)
    {
        if (string.IsNullOrWhiteSpace(keys))
        {
            return [];
        }

        // Keep "Up / Down" as one chip; split chords on '+'.
        if (keys.Contains('/', StringComparison.Ordinal))
        {
            return [keys.Trim()];
        }

        return keys
            .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
    }
}

public static class ShortcutCheatsheet
{
    public static IReadOnlyList<ShortcutHint> Entries { get; } =
    [
        new("Ctrl+1", "1:1", "TxtShortcutActual"),
        new("Ctrl+2", "Fit", "TxtShortcutFit"),
        new("Ctrl+3", "Fill", "TxtShortcutFill"),
        new("Ctrl+D", "Details (Info)", "TxtShortcutInfo"),
        new("I", "Details (Info)", "TxtShortcutDetails"),
        new("Alt+Enter", "Details (Info)", "TxtShortcutDetailsAlt"),
        new("Ctrl+H", "Luminance heatmap (HDR)", "TxtShortcutHeatmap"),
        new("M", "Mute / unmute video", "TxtShortcutMute"),
        new("Up / Down", "Video volume (±5, Shift ±1)", "TxtShortcutVolume"),
        new("F6", "Cycle image / controls / Details", "TxtShortcutFocusCycle"),
        new("Space", "Open overlay on the focused or selected tile", "TxtShortcutSpace"),
        new("Esc", "Close Details, then overlay", "TxtShortcutEsc")
    ];

    public static ShortcutHint? Find(string keys) =>
        Entries.FirstOrDefault(entry =>
            string.Equals(entry.Keys, keys, StringComparison.OrdinalIgnoreCase));
}
