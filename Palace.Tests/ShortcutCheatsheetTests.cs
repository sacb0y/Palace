using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ShortcutCheatsheetTests
{
    [Fact]
    public void Entries_CoverViewerShortcuts()
    {
        Assert.Equal(
            ["Ctrl+1", "Ctrl+2", "Ctrl+3", "Ctrl+D", "I", "Alt+Enter", "Ctrl+H", "M", "Up / Down", "F6", "Space", "Esc"],
            ShortcutCheatsheet.Entries.Select(entry => entry.Keys));
        Assert.Equal("1:1", ShortcutCheatsheet.Find("Ctrl+1")?.Action);
        Assert.Equal("Fit", ShortcutCheatsheet.Find("Ctrl+2")?.Action);
        Assert.Equal("Fill", ShortcutCheatsheet.Find("Ctrl+3")?.Action);
        Assert.Equal("Details (Info)", ShortcutCheatsheet.Find("Ctrl+D")?.Action);
        Assert.Equal("Details (Info)", ShortcutCheatsheet.Find("I")?.Action);
        Assert.Equal("Details (Info)", ShortcutCheatsheet.Find("Alt+Enter")?.Action);
        Assert.Contains("heatmap", ShortcutCheatsheet.Find("Ctrl+H")!.Action, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mute", ShortcutCheatsheet.Find("M")!.Action, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("volume", ShortcutCheatsheet.Find("Up / Down")!.Action, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cycle", ShortcutCheatsheet.Find("F6")!.Action, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overlay", ShortcutCheatsheet.Find("Space")!.Action, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Details", ShortcutCheatsheet.Find("Esc")!.Action, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Lines_AndAutomationIds_AreStable()
    {
        var ids = ShortcutCheatsheet.Entries.Select(entry => entry.AutomationId).ToList();
        Assert.Equal(
            [
                "TxtShortcutActual",
                "TxtShortcutFit",
                "TxtShortcutFill",
                "TxtShortcutInfo",
                "TxtShortcutDetails",
                "TxtShortcutDetailsAlt",
                "TxtShortcutHeatmap",
                "TxtShortcutMute",
                "TxtShortcutVolume",
                "TxtShortcutFocusCycle",
                "TxtShortcutSpace",
                "TxtShortcutEsc"
            ],
            ids);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ShortcutCheatsheet.Entries, entry =>
        {
            Assert.StartsWith("TxtShortcut", entry.AutomationId, StringComparison.Ordinal);
            Assert.Equal($"{entry.Keys}  {entry.Action}", entry.Line);
        });
        Assert.Null(ShortcutCheatsheet.Find("Ctrl+9"));
    }
}
