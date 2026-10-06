using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ShortcutCheatsheetTests
{
    [Fact]
    public void Entries_CoverViewerShortcuts()
    {
        Assert.Equal(
            ["Ctrl+1", "Ctrl+2", "Ctrl+3", "Ctrl+D", "Ctrl+I", "Space", "Esc"],
            ShortcutCheatsheet.Entries.Select(entry => entry.Keys));
        Assert.Equal("1:1", ShortcutCheatsheet.Find("Ctrl+1")?.Action);
        Assert.Equal("Fit", ShortcutCheatsheet.Find("Ctrl+2")?.Action);
        Assert.Equal("Fill", ShortcutCheatsheet.Find("Ctrl+3")?.Action);
        Assert.Equal("Image information", ShortcutCheatsheet.Find("Ctrl+D")?.Action);
        Assert.Equal("Details", ShortcutCheatsheet.Find("Ctrl+I")?.Action);
        Assert.Contains("overlay", ShortcutCheatsheet.Find("Space")!.Action, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Close", ShortcutCheatsheet.Find("Esc")!.Action, StringComparison.OrdinalIgnoreCase);
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
