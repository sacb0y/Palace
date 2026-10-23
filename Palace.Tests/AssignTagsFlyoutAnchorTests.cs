using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class AssignTagsFlyoutAnchorTests
{
    [Fact]
    public void Resolve_PreferCtaWhenShown_UsesTagSelected()
    {
        Assert.Equal(
            AssignTagsFlyoutAnchor.Kind.TagSelectedCta,
            AssignTagsFlyoutAnchor.Resolve(
                preferTagSelectedCta: true,
                showTagSelectedCta: true,
                previewPaneVisible: false));
    }

    [Fact]
    public void Resolve_PreviewVisible_UsesBrowseTags()
    {
        Assert.Equal(
            AssignTagsFlyoutAnchor.Kind.BrowseTagsInPreview,
            AssignTagsFlyoutAnchor.Resolve(
                preferTagSelectedCta: true,
                showTagSelectedCta: false,
                previewPaneVisible: true));
        Assert.Equal(
            AssignTagsFlyoutAnchor.Kind.BrowseTagsInPreview,
            AssignTagsFlyoutAnchor.Resolve(
                preferTagSelectedCta: false,
                showTagSelectedCta: false,
                previewPaneVisible: true));
    }

    [Fact]
    public void Resolve_PreviewCollapsed_UsesMosaicFallback()
    {
        // Default 1200 DIP window hides Preview — tile / mosaic host must anchor.
        Assert.Equal(
            AssignTagsFlyoutAnchor.Kind.MosaicFallback,
            AssignTagsFlyoutAnchor.Resolve(
                preferTagSelectedCta: true,
                showTagSelectedCta: false,
                previewPaneVisible: false));
        Assert.Equal(
            AssignTagsFlyoutAnchor.Kind.MosaicFallback,
            AssignTagsFlyoutAnchor.Resolve(
                preferTagSelectedCta: false,
                showTagSelectedCta: true,
                previewPaneVisible: false));
    }
}
