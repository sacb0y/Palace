using System.Text.RegularExpressions;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Wimbly / Isiac: CapturePointer while CanDrag kills WinUI DnD (DragStarting never fires).
/// </summary>
public sealed class TagChipCapturePointerTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Palace.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Palace.csproj not found above the test output");
    }

    private static string TagChipCode() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "Controls", "TagChip.xaml.cs"));

    [Fact]
    public void ShouldCapturePointer_FalseWhenCanDrag()
    {
        Assert.False(TagDragUi.ShouldCapturePointerForPressChrome(canDrag: true));
        Assert.True(TagDragUi.ShouldCapturePointerForPressChrome(canDrag: false));
    }

    [Fact]
    public void TagChip_RootPointerPressed_GatesCaptureOnCanDrag()
    {
        var cs = TagChipCode();
        Assert.Contains("ShouldCapturePointerForPressChrome(CanDrag)", cs, StringComparison.Ordinal);
        Assert.Contains("CapturePointer(e.Pointer)", cs, StringComparison.Ordinal);

        // CapturePointer must sit inside the ShouldCapturePointerForPressChrome gate —
        // never an unconditional CapturePointer in Root_PointerPressed.
        var pressed = cs.IndexOf("Root_PointerPressed", StringComparison.Ordinal);
        Assert.True(pressed >= 0);
        var next = cs.IndexOf("Root_PointerReleased", pressed, StringComparison.Ordinal);
        Assert.True(next > pressed);
        var body = cs[pressed..next];
        Assert.True(
            Regex.IsMatch(
                body,
                @"ShouldCapturePointerForPressChrome\s*\(\s*CanDrag\s*\)[\s\S]*CapturePointer\s*\(\s*e\.Pointer\s*\)",
                RegexOptions.CultureInvariant),
            "CapturePointer must be gated by ShouldCapturePointerForPressChrome(CanDrag)");
    }

    [Fact]
    public void TagChip_NoUnhandledCapturePointerOutsideGate()
    {
        var cs = TagChipCode();
        // Only one CapturePointer call site in TagChip.
        Assert.Single(Regex.Matches(cs, @"CapturePointer\s*\("));
    }

    [Fact]
    public void Probe_LogDragStarting_Increments()
    {
        TagDragUi.ResetProbe();
        var lines = new List<string>();
        TagDragUi.LogSink = lines.Add;
        try
        {
            TagDragUi.LogDragStarting("knight", 1);
            TagDragUi.LogDragOver("character", "Character", allow: true, caption: "Add to Character");
            TagDragUi.LogDrop("character", "Character", added: 1);
            Assert.Equal(1, TagDragUi.DragStartingCount);
            Assert.Equal(1, TagDragUi.DragOverCount);
            Assert.Equal(1, TagDragUi.DropCount);
            Assert.Contains(lines, l => l.Contains("Palace.TagDrag DragStarting", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Contains("Palace.TagDrag DragOver", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Contains("Palace.TagDrag Drop", StringComparison.Ordinal));
        }
        finally
        {
            TagDragUi.LogSink = null;
            TagDragUi.ResetProbe();
        }
    }
}
