using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class PreviewPaneTests
{
    [Fact]
    public void Initial_UserShowTrue_NoOverlayOrPin()
    {
        var s = PreviewPane.Initial;
        Assert.True(s.UserShow);
        Assert.False(s.AutoHidden);
        Assert.False(s.OverlayOpen);
        Assert.False(s.Pinned);
        Assert.Equal(LibraryColumnLayout.PreviewDefaultWidth, s.UserWidth);
    }

    [Fact]
    public void AutoHideBelow_IsTreeMinPlusMosaicMinPlusPreviewMinPlusGaps()
    {
        Assert.Equal(
            LibraryColumnLayout.TreeMinWidth
                + LibraryColumnLayout.MosaicMinWidth
                + LibraryColumnLayout.PreviewMinWidth
                + LibraryColumnLayout.GapsWidth,
            PreviewPane.AutoHideBelowContentDip,
            0.01);
        Assert.Equal(40, PreviewPane.AutoHideHysteresisDip);
        Assert.Equal(
            PreviewPane.AutoHideBelowContentDip + 40,
            PreviewPane.AutoShowAboveContentDip,
            0.01);
        Assert.Equal(360, PreviewPane.OverlayWidthDip);
    }

    [Fact]
    public void OnWidth_Hysteresis_HidesBelowAndShowsOnlyAboveBand()
    {
        var hide = PreviewPane.AutoHideBelowContentDip;
        var show = PreviewPane.AutoShowAboveContentDip;
        var s = PreviewPane.Initial;

        s = PreviewPane.OnWidth(s, hide);
        Assert.False(s.AutoHidden); // still at threshold
        s = PreviewPane.OnWidth(s, hide - 1);
        Assert.True(s.AutoHidden);

        // Inside hysteresis band — stay hidden.
        s = PreviewPane.OnWidth(s, hide + 10);
        Assert.True(s.AutoHidden);
        s = PreviewPane.OnWidth(s, show - 1);
        Assert.True(s.AutoHidden);

        s = PreviewPane.OnWidth(s, show);
        Assert.False(s.AutoHidden);
        Assert.True(s.UserShow); // never overwritten
    }

    [Fact]
    public void OnWidth_AutoHideDisabled_NeverHides()
    {
        var s = PreviewPane.OnWidth(
            PreviewPane.Initial,
            PreviewPane.AutoHideBelowContentDip - 1,
            autoHideEnabled: false);
        Assert.False(s.AutoHidden);
        Assert.Equal(PaneMode.Docked, PreviewPane.Effective(s, PreviewPane.AutoHideBelowContentDip - 1).Mode);
    }

    [Fact]
    public void OnWidth_CrossingClearsPinnedAndOverlay()
    {
        var hide = PreviewPane.AutoHideBelowContentDip;
        var show = PreviewPane.AutoShowAboveContentDip;
        var s = PreviewPane.Initial with { Pinned = true, OverlayOpen = true };

        s = PreviewPane.OnWidth(s, hide - 1);
        Assert.True(s.AutoHidden);
        Assert.False(s.Pinned);
        Assert.False(s.OverlayOpen);
        Assert.True(s.UserShow);

        s = s with { Pinned = true, OverlayOpen = true };
        s = PreviewPane.OnWidth(s, show);
        Assert.False(s.AutoHidden);
        Assert.False(s.Pinned);
        Assert.False(s.OverlayOpen);
    }

    [Fact]
    public void Toggle_FlipsUserShow_DoesNotTouchAutoHidden()
    {
        var s = PreviewPane.OnWidth(PreviewPane.Initial, PreviewPane.AutoHideBelowContentDip - 1);
        Assert.True(s.AutoHidden);
        s = PreviewPane.Toggle(s);
        Assert.False(s.UserShow);
        Assert.True(s.AutoHidden);
        s = PreviewPane.Toggle(s);
        Assert.True(s.UserShow);
        Assert.True(s.AutoHidden);
    }

    [Fact]
    public void ApplyToggle_AutoHiddenOpensOpaqueOverlay_NotPinDock()
    {
        // Gooey narrow-window: below auto-hide, Preview ON = overlay over mosaic.
        var narrow = PreviewPane.AutoHideBelowContentDip - 1;
        var s = PreviewPane.OnWidth(PreviewPane.Initial, narrow);
        Assert.True(s.AutoHidden);
        Assert.True(s.UserShow);
        Assert.False(PreviewPane.IsChromeChecked(s));
        Assert.Equal(PreviewPane.AutoHiddenTooltip, PreviewPane.ChromeTooltip(s));
        Assert.Contains("over the grid", PreviewPane.AutoHiddenTooltip, StringComparison.OrdinalIgnoreCase);

        var without = PreviewPane.Effective(s, narrow);
        Assert.Equal(PaneMode.Hidden, without.Mode);
        s = PreviewPane.ApplyToggle(s);
        Assert.True(s.UserShow);
        Assert.True(s.OverlayOpen);
        Assert.False(s.Pinned);
        Assert.True(PreviewPane.IsChromeChecked(s));
        var overlay = PreviewPane.Effective(s, narrow);
        Assert.Equal(PaneMode.Overlay, overlay.Mode);
        Assert.Equal(0, overlay.Widths.Preview);
        Assert.Equal(without.Widths.Mosaic, overlay.Widths.Mosaic, 0.01);
        var layout = PreviewPane.Layout(overlay);
        Assert.True(layout.OverlayVisible);
        Assert.False(layout.DockHostVisible);
        Assert.Equal(0, PreviewPane.GridPreviewColumnWidth(layout));

        s = PreviewPane.ApplyToggle(s);
        Assert.False(s.OverlayOpen);
        Assert.True(s.UserShow);
        Assert.False(PreviewPane.IsChromeChecked(s));
        Assert.Equal(PaneMode.Hidden, PreviewPane.Effective(s, narrow).Mode);
    }

    [Fact]
    public void ApplyToggle_DockedFlipsUserShow()
    {
        var wide = PreviewPane.AutoShowAboveContentDip + 100;
        var s = PreviewPane.OnWidth(PreviewPane.Initial, wide);
        Assert.True(PreviewPane.IsChromeChecked(s));
        s = PreviewPane.ApplyToggle(s);
        Assert.False(s.UserShow);
        Assert.Equal(PaneMode.Hidden, PreviewPane.Effective(s, wide).Mode);
        s = PreviewPane.ApplyToggle(s);
        Assert.True(s.UserShow);
        Assert.Equal(PaneMode.Docked, PreviewPane.Effective(s, wide).Mode);
    }

    [Fact]
    public void CloseOverlay_DismissesWithoutPinDock()
    {
        // Isiac: ✕ / Esc closes overlay; docking is automatic when wide only.
        var narrow = PreviewPane.AutoHideBelowContentDip - 1;
        var s = PreviewPane.OnWidth(PreviewPane.Initial, narrow);
        s = PreviewPane.ApplyToggle(s);
        Assert.True(s.OverlayOpen);
        Assert.Equal(PaneMode.Overlay, PreviewPane.Effective(s, narrow).Mode);

        s = PreviewPane.CloseOverlay(s);
        Assert.False(s.OverlayOpen);
        Assert.False(s.Pinned);
        Assert.True(s.UserShow);
        Assert.True(s.AutoHidden);
        Assert.False(PreviewPane.IsChromeChecked(s));
        Assert.Equal(PaneMode.Hidden, PreviewPane.Effective(s, narrow).Mode);
    }

    [Fact]
    public void Pin_LegacyNoLongerDocksWhileAutoHidden()
    {
        var s = PreviewPane.OnWidth(PreviewPane.Initial, PreviewPane.AutoHideBelowContentDip - 1);
        s = PreviewPane.Pin(s);
        Assert.False(s.Pinned);
        Assert.True(s.AutoHidden);
        Assert.Equal(PaneMode.Hidden, PreviewPane.Effective(s, PreviewPane.AutoHideBelowContentDip - 1).Mode);
    }

    [Fact]
    public void Effective_HiddenWhenAutoOrUserHide_DockedWhenShown()
    {
        var wide = PreviewPane.AutoShowAboveContentDip + 100;
        var s = PreviewPane.OnWidth(PreviewPane.Initial, wide);
        var docked = PreviewPane.Effective(s, wide);
        Assert.Equal(PaneMode.Docked, docked.Mode);
        Assert.False(docked.Widths.PreviewHidden);
        Assert.True(docked.Widths.Preview > 0);

        s = PreviewPane.OnWidth(s, PreviewPane.AutoHideBelowContentDip - 1);
        var hidden = PreviewPane.Effective(s, PreviewPane.AutoHideBelowContentDip - 1);
        Assert.Equal(PaneMode.Hidden, hidden.Mode);
        Assert.True(hidden.Widths.PreviewHidden);
        Assert.Equal(0, hidden.Widths.Preview);

        s = PreviewPane.Toggle(PreviewPane.OnWidth(PreviewPane.Initial, wide));
        Assert.False(s.UserShow);
        var userHidden = PreviewPane.Effective(s, wide);
        Assert.Equal(PaneMode.Hidden, userHidden.Mode);
    }

    [Fact]
    public void Effective_OverlayOpen_IsOverlayWithoutReflow()
    {
        var wide = PreviewPane.AutoShowAboveContentDip + 100;
        var s = PreviewPane.OnWidth(PreviewPane.Initial, wide);
        var docked = PreviewPane.Effective(s, wide);
        s = PreviewPane.SetOverlayOpen(s, true);
        var eff = PreviewPane.Effective(s, wide);
        Assert.Equal(PaneMode.Overlay, eff.Mode);
        Assert.Equal(0, eff.Widths.Preview);
        Assert.True(eff.Widths.Mosaic > docked.Widths.Mosaic);
        Assert.True(PreviewPane.Layout(eff).OverlayVisible);
    }

    [Fact]
    public void OverlayPanelWidth_UsesUserWidthClampedToAvailable()
    {
        var s = PreviewPane.Initial with { UserWidth = 400 };
        Assert.Equal(400, PreviewPane.OverlayPanelWidth(s, 2000), 0.01);
        Assert.Equal(
            LibraryColumnLayout.PreviewMinWidth,
            PreviewPane.OverlayPanelWidth(s, PreviewPane.OverlayMinLeadingMarginDip + 100),
            0.01);
    }

    [Fact]
    public void ClampUserWidth_Bounds280To560()
    {
        Assert.Equal(280, PreviewPane.ClampUserWidth(100));
        Assert.Equal(560, PreviewPane.ClampUserWidth(900));
        Assert.Equal(340, PreviewPane.ClampUserWidth(double.NaN));
        Assert.Equal(340, PreviewPane.ResetUserWidth(PreviewPane.Initial with { UserWidth = 500 }).UserWidth);
    }

    [Fact]
    public void Layout_DockedHideShowRestoresColPreview_OpacityHideNoCollapse()
    {
        var wide = PreviewPane.AutoShowAboveContentDip + 200;
        var userWidth = 400d;
        var s = PreviewPane.OnWidth(
            PreviewPane.Initial with { UserWidth = userWidth },
            wide);
        var docked = PreviewPane.Layout(PreviewPane.Effective(s, wide));
        Assert.Equal(PaneMode.Docked, docked.Mode);
        Assert.True(docked.DockHostVisible);
        Assert.False(docked.OverlayVisible);
        Assert.Equal(userWidth, docked.Preview, 0.01);
        Assert.True(double.IsNaN(PreviewPane.DockHostWidth(docked)));
        Assert.Equal(userWidth, PreviewPane.GridPreviewColumnWidth(docked), 0.01);
        var dockShown = PreviewPane.DockChrome(docked);
        Assert.True(double.IsNaN(dockShown.Width));
        Assert.Equal(1, dockShown.Opacity);
        Assert.True(dockShown.HitTestVisible);
        Assert.True(docked.Mosaic >= LibraryColumnLayout.MosaicMinWidth - 0.01);
        var slotDocked = PreviewPane.DockHostSlot(docked);
        Assert.Equal(2, slotDocked.Column);
        Assert.Equal(1, slotDocked.ColumnSpan);
        Assert.True(slotDocked.Stretch);
        Assert.True(double.IsNaN(slotDocked.Width));

        // Hide → show three times: ColPreview restores; hide is Opacity=0 (not Collapse).
        for (var i = 0; i < 3; i++)
        {
            s = PreviewPane.ApplyToggle(s);
            var hidden = PreviewPane.Layout(PreviewPane.Effective(s, wide));
            Assert.Equal(PaneMode.Hidden, hidden.Mode);
            Assert.False(hidden.DockHostVisible);
            Assert.False(hidden.OverlayVisible);
            Assert.Equal(0, hidden.Preview);
            Assert.Equal(0, PreviewPane.DockHostWidth(hidden));
            Assert.Equal(0, PreviewPane.GridPreviewColumnWidth(hidden));
            var dockHidden = PreviewPane.DockChrome(hidden);
            Assert.Equal(0, dockHidden.Width);
            Assert.Equal(0, dockHidden.Opacity);
            Assert.False(dockHidden.HitTestVisible);
            Assert.True(hidden.Mosaic > docked.Mosaic);

            s = PreviewPane.ApplyToggle(s);
            var shown = PreviewPane.Layout(PreviewPane.Effective(s, wide));
            Assert.Equal(PaneMode.Docked, shown.Mode);
            Assert.True(shown.DockHostVisible);
            Assert.False(shown.OverlayVisible);
            Assert.Equal(userWidth, shown.Preview, 0.01);
            Assert.True(double.IsNaN(PreviewPane.DockHostWidth(shown)));
            Assert.Equal(userWidth, PreviewPane.GridPreviewColumnWidth(shown), 0.01);
            var chrome = PreviewPane.DockChrome(shown);
            Assert.True(double.IsNaN(chrome.Width));
            Assert.Equal(1, chrome.Opacity);
            Assert.True(chrome.HitTestVisible);
            Assert.Equal(docked.Mosaic, shown.Mosaic, 0.01);
            Assert.Equal(docked.Tree, shown.Tree, 0.01);
            Assert.True(PreviewPane.NeedsDockColumnRelayout(PaneMode.Hidden, shown.Mode));
            Assert.True(PreviewPane.NeedsDockColumnRelayout(shown.Mode, PaneMode.Hidden));
            // Height-only: Grid owns X — no column invalidate required.
            Assert.False(PreviewPane.NeedsDockColumnRelayout(PaneMode.Docked, PaneMode.Docked));
        }
    }

    [Fact]
    public void NeedsDockColumnRelayout_HeightOnlyWhileDocked_DoesNotInvalidateColumns()
    {
        Assert.False(PreviewPane.NeedsDockColumnRelayout(PaneMode.Docked, PaneMode.Docked));
        Assert.True(PreviewPane.NeedsDockColumnRelayout(PaneMode.Hidden, PaneMode.Docked));
        Assert.True(PreviewPane.NeedsDockColumnRelayout(PaneMode.Docked, PaneMode.Hidden));
        Assert.False(PreviewPane.NeedsDockColumnRelayout(PaneMode.Hidden, PaneMode.Hidden));
    }

    [Fact]
    public void Layout_SameContentWidth_IdempotentAcrossRepeatedPasses()
    {
        // Height-only resize: content width unchanged; every pass must yield the
        // same column + host slot (no width-delta gating in the reducer).
        var width = PreviewPane.AutoShowAboveContentDip + 80;
        var s = PreviewPane.OnWidth(PreviewPane.Initial with { UserWidth = 360 }, width);
        var first = PreviewPane.Layout(PreviewPane.Effective(s, width));
        Assert.Equal(PaneMode.Docked, first.Mode);
        var firstSlot = PreviewPane.DockHostSlot(first);
        for (var i = 0; i < 8; i++)
        {
            s = PreviewPane.OnWidth(s, width);
            var pass = PreviewPane.Layout(PreviewPane.Effective(s, width));
            Assert.Equal(first.Mode, pass.Mode);
            Assert.Equal(first.Tree, pass.Tree, 0.01);
            Assert.Equal(first.Mosaic, pass.Mosaic, 0.01);
            Assert.Equal(first.Preview, pass.Preview, 0.01);
            Assert.Equal(
                PreviewPane.GridPreviewColumnWidth(first),
                PreviewPane.GridPreviewColumnWidth(pass),
                0.01);
            var slot = PreviewPane.DockHostSlot(pass);
            Assert.Equal(2, slot.Column);
            Assert.Equal(1, slot.ColumnSpan);
            Assert.Equal(1, slot.Row);
            Assert.True(slot.Stretch);
            Assert.True(double.IsNaN(slot.Width));
            Assert.Equal(1, slot.Opacity);
            Assert.True(slot.HitTestVisible);
            Assert.False(PreviewPane.NeedsDockColumnRelayout(pass.Mode, pass.Mode));
            Assert.Equal(firstSlot.Column, slot.Column);
        }
    }

    [Fact]
    public void DockHostSlot_HeightOnlyClientSizeChange_KeepsCol2Stretch()
    {
        // Root cause was Composition Visual.Offset=Zero on the hand-out visual every
        // layout pass — not Grid Stretch. Slot stays Column=2 Span=1 Stretch NaN.
        var contentWidth = PreviewPane.AutoShowAboveContentDip + 120;
        var s = PreviewPane.OnWidth(PreviewPane.Initial with { UserWidth = 340 }, contentWidth);
        var before = PreviewPane.Layout(PreviewPane.Effective(s, contentWidth));
        var beforeSlot = PreviewPane.DockHostSlot(before);
        Assert.Equal(2, beforeSlot.Column);
        Assert.Equal(1, beforeSlot.ColumnSpan);
        Assert.True(beforeSlot.Stretch);
        Assert.True(double.IsNaN(beforeSlot.Width));
        Assert.Equal(
            LibraryColumnLayout.PagePaddingEdgeDip
                + before.Tree
                + LibraryColumnLayout.ColumnSpacing
                + before.Mosaic
                + LibraryColumnLayout.ColumnSpacing,
            PreviewPane.DockHostExpectedOffsetX(before),
            0.01);

        for (var i = 0; i < 5; i++)
        {
            s = PreviewPane.OnWidth(s, contentWidth);
            var after = PreviewPane.Layout(PreviewPane.Effective(s, contentWidth));
            var afterSlot = PreviewPane.DockHostSlot(after);
            Assert.Equal(before.Preview, after.Preview, 0.01);
            Assert.Equal(before.Mosaic, after.Mosaic, 0.01);
            Assert.Equal(before.Tree, after.Tree, 0.01);
            Assert.Equal(2, afterSlot.Column);
            Assert.Equal(1, afterSlot.ColumnSpan);
            Assert.True(afterSlot.Stretch);
            Assert.True(double.IsNaN(afterSlot.Width));
            Assert.Equal(after.Preview, PreviewPane.GridPreviewColumnWidth(after), 0.01);
            Assert.False(PreviewPane.NeedsDockColumnRelayout(PaneMode.Docked, PaneMode.Docked));
        }
    }

    [Fact]
    public void DockHostSlot_Hidden_ZeroWidthCol2Stretch()
    {
        var wide = PreviewPane.AutoShowAboveContentDip + 100;
        var s = PreviewPane.OnWidth(PreviewPane.Initial with { UserShow = false }, wide);
        var layout = PreviewPane.Layout(PreviewPane.Effective(s, wide));
        var slot = PreviewPane.DockHostSlot(layout);
        Assert.Equal(PaneMode.Hidden, layout.Mode);
        Assert.Equal(2, slot.Column);
        Assert.Equal(1, slot.ColumnSpan);
        Assert.True(slot.Stretch);
        Assert.Equal(0, slot.Width);
        Assert.Equal(0, slot.Opacity);
        Assert.False(slot.HitTestVisible);
    }

    [Fact]
    public void Layout_NarrowToggle_OpensOverlayChrome()
    {
        var narrow = PreviewPane.AutoHideBelowContentDip - 1;
        var s = PreviewPane.OnWidth(PreviewPane.Initial, narrow);
        s = PreviewPane.ApplyToggle(s);
        var layout = PreviewPane.Layout(PreviewPane.Effective(s, narrow));
        Assert.Equal(PaneMode.Overlay, layout.Mode);
        Assert.False(layout.DockHostVisible);
        Assert.True(layout.OverlayVisible);
        Assert.Equal(0, layout.Preview);
        Assert.Equal(0, PreviewPane.GridPreviewColumnWidth(layout));
    }

    /// <summary>
    /// Studio FAIL 0.3.68: window ~909 / ~1003 DIP reported AutoHidden=false / Docked.
    /// With correct content (nav+pad subtracted) those clients are far below 1024 —
    /// OnWidth must AutoHidden=true and toggle → Overlay. Threshold unchanged.
    /// </summary>
    [Theory]
    [InlineData(909, LibraryColumnLayout.ShellNavPaneWidthDip)]
    [InlineData(909, LibraryColumnLayout.ShellNavCompactPaneWidthDip)]
    [InlineData(909, 0)]
    [InlineData(1003, LibraryColumnLayout.ShellNavPaneWidthDip)]
    [InlineData(1003, LibraryColumnLayout.ShellNavCompactPaneWidthDip)]
    [InlineData(1003, 0)]
    public void OnWidth_StudioOverlayWindowClients_AutoHidden_ToggleOpensOverlay(
        double windowClientDip,
        double navPaneDip)
    {
        var content = LibraryColumnLayout.ContentWidthFromClientDip(windowClientDip, navPaneDip);
        Assert.True(
            content < PreviewPane.AutoHideBelowContentDip,
            $"content {content} must stay below AutoHideBelow {PreviewPane.AutoHideBelowContentDip}");

        var s = PreviewPane.OnWidth(PreviewPane.Initial, content);
        Assert.True(s.AutoHidden);
        Assert.False(s.Pinned);
        Assert.Equal(PaneMode.Hidden, PreviewPane.Effective(s, content).Mode);

        s = PreviewPane.ApplyToggle(s);
        Assert.True(s.OverlayOpen);
        Assert.False(s.Pinned);
        Assert.Equal(PaneMode.Overlay, PreviewPane.Effective(s, content).Mode);
    }

    [Fact]
    public void OnWidth_AutoHideDisabled_WrongInput_StaysDockedEvenWhenNarrow()
    {
        // Hypothesis (1): Settings Preview=Show → AutoHideEnabled false.
        // Documented wrong input — no threshold change; Studio must use Automatic.
        var content = LibraryColumnLayout.ContentWidthFromClientDip(1003);
        Assert.True(content < PreviewPane.AutoHideBelowContentDip);
        var s = PreviewPane.OnWidth(PreviewPane.Initial, content, autoHideEnabled: false);
        Assert.False(s.AutoHidden);
        Assert.Equal(PaneMode.Docked, PreviewPane.Effective(s, content).Mode);
        s = PreviewPane.ApplyToggle(s);
        Assert.False(s.OverlayOpen);
        Assert.Equal(PaneMode.Hidden, PreviewPane.Effective(s, content).Mode);
    }

    [Fact]
    public void OnWidth_InflatedXamlRootLikeStudioScale_WouldSkipAutoHide_PageHostDoesNot()
    {
        // Studio @150%: XamlRoot.Size=1352 while HWND≈911 DIP. Layout must use page
        // ActualWidth (host DIP), not the inflated root. Do not divide root by scale.
        const double hwndDip = 911;
        const double inflatedRoot = 1352; // ≈911×1.5 — lying input when treated as DIP
        var pageHost = hwndDip - LibraryColumnLayout.ShellNavPaneWidthDip;
        var fromPage = LibraryColumnLayout.ContentWidthFromHostDip(pageHost);
        var fromRoot = LibraryColumnLayout.ContentWidthFromClientDip(inflatedRoot);

        Assert.True(fromPage < PreviewPane.AutoHideBelowContentDip);
        Assert.True(PreviewPane.OnWidth(PreviewPane.Initial, fromPage).AutoHidden);

        Assert.True(fromRoot >= PreviewPane.AutoHideBelowContentDip);
        Assert.False(PreviewPane.OnWidth(PreviewPane.Initial, fromRoot).AutoHidden);
    }

    [Fact]
    public void OnWidth_CrossingIntoAutoHide_ClearsOverlay_NoPinDock()
    {
        var wide = LibraryColumnLayout.ContentWidthFromClientDip(1400);
        var narrow = LibraryColumnLayout.ContentWidthFromClientDip(1003);
        Assert.True(wide >= PreviewPane.AutoShowAboveContentDip);
        Assert.True(narrow < PreviewPane.AutoHideBelowContentDip);

        var s = PreviewPane.OnWidth(PreviewPane.Initial, wide);
        Assert.False(s.AutoHidden);
        s = s with { OverlayOpen = true };

        s = PreviewPane.OnWidth(s, narrow);
        Assert.True(s.AutoHidden);
        Assert.False(s.Pinned);
        Assert.False(s.OverlayOpen);
        Assert.Equal(PaneMode.Hidden, PreviewPane.Effective(s, narrow).Mode);
    }
}
