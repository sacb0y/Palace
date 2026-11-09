using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class LibraryAddTagsShortcutTests
{
    [Fact]
    public void ShouldHandle_T_WithSelection_NotTyping() =>
        Assert.True(LibraryAddTagsShortcut.ShouldHandle(
            LibraryAddTagsShortcut.KeyT,
            galleryOverlayOpen: false,
            controlDown: false,
            menuDown: false,
            shiftDown: false,
            focusIsTextInput: false,
            hasSelection: true));

    [Fact]
    public void ShouldHandle_SkipsTextBoxAndAutoSuggest() =>
        Assert.False(LibraryAddTagsShortcut.ShouldHandle(
            LibraryAddTagsShortcut.KeyT,
            galleryOverlayOpen: false,
            controlDown: false,
            menuDown: false,
            shiftDown: false,
            focusIsTextInput: true,
            hasSelection: true));

    [Fact]
    public void ShouldHandle_SkipsModifiersAndOverlayAndNoSelection()
    {
        Assert.False(LibraryAddTagsShortcut.ShouldHandle(
            LibraryAddTagsShortcut.KeyT, false, controlDown: true, false, false, false, true));
        Assert.False(LibraryAddTagsShortcut.ShouldHandle(
            LibraryAddTagsShortcut.KeyT, false, false, menuDown: true, false, false, true));
        Assert.False(LibraryAddTagsShortcut.ShouldHandle(
            LibraryAddTagsShortcut.KeyT, false, false, false, shiftDown: true, false, true));
        Assert.False(LibraryAddTagsShortcut.ShouldHandle(
            LibraryAddTagsShortcut.KeyT, galleryOverlayOpen: true, false, false, false, false, true));
        Assert.False(LibraryAddTagsShortcut.ShouldHandle(
            LibraryAddTagsShortcut.KeyT, false, false, false, false, false, hasSelection: false));
    }

    [Fact]
    public void ShouldHandle_OtherKeys_False() =>
        Assert.False(LibraryAddTagsShortcut.ShouldHandle(
            LibraryAddTagsShortcut.KeyM, false, false, false, false, false, true));
}
