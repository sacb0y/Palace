namespace Palace.Helpers;

/// <summary>
/// Where to <c>ShowAt</c> the Library assign-tags flyout.
/// When Preview auto-hides, <c>BtnBrowseTags</c> lives inside a Collapsed pane and
/// must not be the anchor (WinUI places badly or skips the flyout). Prefer the
/// select-mode CTA (toolbar) or a mosaic host that stays laid out.
/// </summary>
public static class AssignTagsFlyoutAnchor
{
    public enum Kind
    {
        /// <summary>Select-mode CTA in the mosaic toolbar (laid out when visible).</summary>
        TagSelectedCta,

        /// <summary>Preview pane + Add tag — only when Preview is Visible.</summary>
        BrowseTagsInPreview,

        /// <summary>Selected tile / mosaic host when Preview is collapsed.</summary>
        MosaicFallback,
    }

    /// <summary>
    /// Resolve the flyout anchor kind. Pure — map to a FrameworkElement in the page.
    /// </summary>
    public static Kind Resolve(
        bool preferTagSelectedCta,
        bool showTagSelectedCta,
        bool previewPaneVisible)
    {
        if (preferTagSelectedCta && showTagSelectedCta)
        {
            return Kind.TagSelectedCta;
        }

        if (previewPaneVisible)
        {
            return Kind.BrowseTagsInPreview;
        }

        return Kind.MosaicFallback;
    }
}
