namespace Palace.Helpers;

/// <summary>
/// Overlay / gallery shortcut copy for Settings → About. Off WinUI so the list is testable.
/// </summary>
public sealed class ShortcutHint
{
    public ShortcutHint(string keys, string action, string automationId)
    {
        Keys = keys;
        Action = action;
        AutomationId = automationId;
        Line = $"{keys}  {action}";
    }

    public string Keys { get; }

    public string Action { get; }

    public string AutomationId { get; }

    public string Line { get; }
}

public static class ShortcutCheatsheet
{
    public static IReadOnlyList<ShortcutHint> Entries { get; } =
    [
        new("Ctrl+1", "1:1", "TxtShortcutActual"),
        new("Ctrl+2", "Fit", "TxtShortcutFit"),
        new("Ctrl+3", "Fill", "TxtShortcutFill"),
        new("Ctrl+D", "Image information", "TxtShortcutInfo"),
        new("Ctrl+I", "Details", "TxtShortcutDetails"),
        new("Space", "Open overlay on the focused or selected tile", "TxtShortcutSpace"),
        new("Esc", "Close overlay", "TxtShortcutEsc")
    ];

    public static ShortcutHint? Find(string keys) =>
        Entries.FirstOrDefault(entry =>
            string.Equals(entry.Keys, keys, StringComparison.OrdinalIgnoreCase));
}
