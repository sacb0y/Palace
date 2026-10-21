using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class PreviewPersistTests
{
    [Fact]
    public void ShouldWriteNotes_FalseWhileApplyingPreview()
    {
        Assert.False(PreviewPersist.ShouldWriteNotes(true, true, "a", "a"));
    }

    [Fact]
    public void ShouldWriteNotes_FalseWhenClearPreviewHasNoPreviewId()
    {
        Assert.False(PreviewPersist.ShouldWriteNotes(false, true, "a", null));
        Assert.False(PreviewPersist.ShouldWriteNotes(false, true, "a", ""));
    }

    [Fact]
    public void ShouldWriteNotes_FalseWhenOverlayPreviewIsAnotherAsset()
    {
        Assert.False(PreviewPersist.ShouldWriteNotes(false, true, "selected", "overlay"));
    }

    [Fact]
    public void ShouldWriteNotes_TrueForUserEditOfSelectedAsset()
    {
        Assert.True(PreviewPersist.ShouldWriteNotes(false, true, "a", "a"));
    }

    [Fact]
    public void ShouldWriteNotes_FalseWhenCannotEditOrNoSelection()
    {
        Assert.False(PreviewPersist.ShouldWriteNotes(false, false, "a", "a"));
        Assert.False(PreviewPersist.ShouldWriteNotes(false, true, null, "a"));
    }

    [Fact]
    public void ShouldWriteRating_AllowsMultiSelectWhenNotApplying()
    {
        Assert.True(PreviewPersist.ShouldWriteRating(false, true, 4));
        Assert.False(PreviewPersist.ShouldWriteRating(true, true, 4));
        Assert.False(PreviewPersist.ShouldWriteRating(false, false, 4));
        Assert.False(PreviewPersist.ShouldWriteRating(false, true, 0));
    }
}
