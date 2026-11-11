using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagPanelFilterShortcutTests
{
    [Fact]
    public void CtrlF_InTagBrowse_Handles() =>
        Assert.True(TagPanelFilterShortcut.ShouldHandle(
            TagPanelFilterShortcut.KeyF,
            controlDown: true,
            menuDown: false,
            shiftDown: false,
            galleryOverlayOpen: false,
            isTagBrowse: true));

    [Theory]
    [InlineData(false, true, false, false, true)] // no Ctrl
    [InlineData(true, true, false, false, false)] // Alt
    [InlineData(true, false, true, false, true)] // Shift
    [InlineData(true, false, false, true, true)] // overlay
    [InlineData(true, false, false, false, false)] // folders mode
    public void CtrlF_Gates(
        bool control,
        bool menu,
        bool shift,
        bool overlay,
        bool tagBrowse) =>
        Assert.False(TagPanelFilterShortcut.ShouldHandle(
            TagPanelFilterShortcut.KeyF,
            control,
            menu,
            shift,
            overlay,
            tagBrowse));

    [Fact]
    public void OtherKey_Ignored() =>
        Assert.False(TagPanelFilterShortcut.ShouldHandle(
            LibraryAddTagsShortcut.KeyT,
            controlDown: true,
            menuDown: false,
            shiftDown: false,
            galleryOverlayOpen: false,
            isTagBrowse: true));
}
