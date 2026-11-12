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
    public void ApplyToggle_AutoHiddenPinsDock_ReflowsBesideOpaqueColumn()
    {
        var narrow = PreviewPane.AutoHideBelowContentDip - 1;
        var s = PreviewPane.OnWidth(PreviewPane.Initial, narrow);
        Assert.True(s.AutoHidden);
        Assert.True(s.UserShow);
        Assert.False(PreviewPane.IsChromeChecked(s));
        Assert.Equal(PreviewPane.AutoHiddenTooltip, PreviewPane.ChromeTooltip(s));
        Assert.Contains("docks beside", PreviewPane.AutoHiddenTooltip, StringComparison.OrdinalIgnoreCase);

        var without = PreviewPane.Effective(s, narrow);
        s = PreviewPane.ApplyToggle(s);
        Assert.True(s.UserShow);
        Assert.True(s.Pinned);
        Assert.False(s.OverlayOpen);
        Assert.True(PreviewPane.IsChromeChecked(s));
        var docked = PreviewPane.Effective(s, narrow);
        Assert.Equal(PaneMode.Docked, docked.Mode);
        Assert.True(docked.Widths.Preview > 0);
        Assert.True(docked.Widths.Mosaic < without.Widths.Mosaic);
        var layout = PreviewPane.Layout(docked);
        Assert.Equal(docked.Widths.Preview, PreviewPane.GridPreviewColumnWidth(layout), 0.01);
        Assert.Equal(docked.Widths.Preview, PreviewPane.DockHostWidth(layout), 0.01);
        var slot = PreviewPane.DockHostSlot(layout);
        Assert.True(slot.AlignRight);
        Assert.Equal(3, slot.ColumnSpan);
        Assert.Equal(docked.Widths.Preview, slot.Width, 0.01);
        Assert.False(layout.OverlayVisible);

        s = PreviewPane.ApplyToggle(s);
        Assert.False(s.Pinned);
        Assert.False(s.UserShow);
        Assert.False(s.OverlayOpen);
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
    public void Pin_DocksWhileAutoHidden_UntilCleared()
    {
        var narrow = PreviewPane.AutoHideBelowContentDip - 1;
        var s = PreviewPane.OnWidth(PreviewPane.Initial, narrow);
        s = PreviewPane.Pin(s);
        Assert.True(s.Pinned);
        Assert.False(s.OverlayOpen);
        Assert.True(s.AutoHidden);
        Assert.Equal(PaneMode.Docked, PreviewPane.Effective(s, narrow).Mode);
    }

    [Fact]
    public void Pin_DoesNotTouchAutoOrUserShow()
    {
        var s = PreviewPane.OnWidth(PreviewPane.Initial, PreviewPane.AutoHideBelowContentDip - 1);
        s = PreviewPane.Pin(s);
        Assert.True(s.Pinned);
        Assert.True(s.AutoHidden);
        Assert.True(s.UserShow);
        s = PreviewPane.Pin(s, pinned: false);
        Assert.False(s.Pinned);
        Assert.True(s.AutoHidden);
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
    public void Effective_LegacyOverlayOpen_PromotesToDockedReflow()
    {
        var wide = PreviewPane.AutoShowAboveContentDip + 100;
        var s = PreviewPane.OnWidth(PreviewPane.Initial, wide);
        var withoutOverlay = PreviewPane.Effective(s, wide);
        s = PreviewPane.SetOverlayOpen(s, true);
        var eff = PreviewPane.Effective(s, wide);
        Assert.Equal(PaneMode.Docked, eff.Mode);
        Assert.Equal(withoutOverlay.Widths.Preview, eff.Widths.Preview, 0.01);
        Assert.Equal(withoutOverlay.Widths.Mosaic, eff.Widths.Mosaic, 0.01);
        Assert.False(eff.Widths.PreviewHidden);
    }

    [Fact]
    public void Effective_NarrowToggle_ReflowsGrid_NotAcrylicOverlay()
    {
        // ~1024 client → content below AutoHideBelow; toggle must dock+reflow.
        var narrow = PreviewPane.AutoHideBelowContentDip - 1;
        Assert.True(narrow < 1024);
        var s = PreviewPane.OnWidth(PreviewPane.Initial, narrow);
        var without = PreviewPane.Effective(s, narrow);
        s = PreviewPane.ApplyToggle(s);
        var shown = PreviewPane.Effective(s, narrow);
        Assert.Equal(PaneMode.Docked, shown.Mode);
        Assert.True(shown.Widths.Preview >= LibraryColumnLayout.PreviewMinWidth - 0.01);
        Assert.True(shown.Widths.Mosaic < without.Widths.Mosaic);
        Assert.False(PreviewPane.Layout(shown).OverlayVisible);
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
        Assert.Equal(userWidth, PreviewPane.DockHostWidth(docked), 0.01);
        Assert.Equal(userWidth, PreviewPane.GridPreviewColumnWidth(docked), 0.01);
        var dockShown = PreviewPane.DockChrome(docked);
        Assert.Equal(userWidth, dockShown.Width, 0.01);
        Assert.Equal(1, dockShown.Opacity);
        Assert.True(dockShown.HitTestVisible);
        Assert.True(docked.Mosaic >= LibraryColumnLayout.MosaicMinWidth - 0.01);
        var slotDocked = PreviewPane.DockHostSlot(docked);
        Assert.Equal(0, slotDocked.Column);
        Assert.Equal(3, slotDocked.ColumnSpan);
        Assert.True(slotDocked.AlignRight);
        Assert.Equal(userWidth, slotDocked.Width, 0.01);

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
            Assert.Equal(userWidth, PreviewPane.DockHostWidth(shown), 0.01);
            Assert.Equal(userWidth, PreviewPane.GridPreviewColumnWidth(shown), 0.01);
            var chrome = PreviewPane.DockChrome(shown);
            Assert.Equal(userWidth, chrome.Width, 0.01);
            Assert.Equal(1, chrome.Opacity);
            Assert.True(chrome.HitTestVisible);
            Assert.Equal(docked.Mosaic, shown.Mosaic, 0.01);
            Assert.Equal(docked.Tree, shown.Tree, 0.01);
            Assert.True(PreviewPane.NeedsDockColumnRelayout(PaneMode.Hidden, shown.Mode));
            Assert.True(PreviewPane.NeedsDockColumnRelayout(shown.Mode, PaneMode.Hidden));
            // Height-only: column widths unchanged — no column invalidate required;
            // host X is right-align + explicit width (not ColPreview stretch arrange).
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
            // Simulate SizeChanged with unchanged content width (height-only).
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
            Assert.Equal(0, slot.Column);
            Assert.Equal(3, slot.ColumnSpan);
            Assert.Equal(1, slot.Row);
            Assert.True(slot.AlignRight);
            Assert.Equal(first.Preview, slot.Width, 0.01);
            Assert.Equal(firstSlot.Width, slot.Width, 0.01);
            Assert.Equal(1, slot.Opacity);
            Assert.True(slot.HitTestVisible);
            Assert.False(PreviewPane.NeedsDockColumnRelayout(pass.Mode, pass.Mode));
        }
    }

    [Fact]
    public void DockHostSlot_HeightOnlyClientSizeChange_KeepsRightAlignedGeometry()
    {
        // Studio FAIL: height-only SetWindowPos left prev.x≈tree.x while prev.w and
        // grid.w unchanged. Slot from current content width alone must stay right-
        // aligned with explicit Preview width (ColPreview still reserves the same DIP).
        var contentWidth = PreviewPane.AutoShowAboveContentDip + 120;
        var s = PreviewPane.OnWidth(PreviewPane.Initial with { UserWidth = 340 }, contentWidth);
        var before = PreviewPane.Layout(PreviewPane.Effective(s, contentWidth));
        var beforeSlot = PreviewPane.DockHostSlot(before);

        // Height-only: same content width, many layout passes (no width delta).
        for (var i = 0; i < 5; i++)
        {
            s = PreviewPane.OnWidth(s, contentWidth);
            var after = PreviewPane.Layout(PreviewPane.Effective(s, contentWidth));
            var afterSlot = PreviewPane.DockHostSlot(after);
            Assert.Equal(before.Preview, after.Preview, 0.01);
            Assert.Equal(before.Mosaic, after.Mosaic, 0.01);
            Assert.Equal(beforeSlot.ColumnSpan, afterSlot.ColumnSpan);
            Assert.Equal(beforeSlot.AlignRight, afterSlot.AlignRight);
            Assert.Equal(beforeSlot.Width, afterSlot.Width, 0.01);
            Assert.True(afterSlot.AlignRight);
            Assert.Equal(3, afterSlot.ColumnSpan);
            Assert.Equal(after.Preview, afterSlot.Width, 0.01);
            Assert.Equal(after.Preview, PreviewPane.GridPreviewColumnWidth(after), 0.01);
            Assert.False(double.IsNaN(afterSlot.Width));
        }
    }

    [Fact]
    public void DockHostSlot_Hidden_ZeroWidthRightAlignedSpan()
    {
        var wide = PreviewPane.AutoShowAboveContentDip + 100;
        var s = PreviewPane.OnWidth(PreviewPane.Initial with { UserShow = false }, wide);
        var layout = PreviewPane.Layout(PreviewPane.Effective(s, wide));
        var slot = PreviewPane.DockHostSlot(layout);
        Assert.Equal(PaneMode.Hidden, layout.Mode);
        Assert.Equal(0, slot.Column);
        Assert.Equal(3, slot.ColumnSpan);
        Assert.True(slot.AlignRight);
        Assert.Equal(0, slot.Width);
        Assert.Equal(0, slot.Opacity);
        Assert.False(slot.HitTestVisible);
    }

    [Fact]
    public void Layout_NarrowOpenNeverClaimsOverlayChrome()
    {
        var narrow = PreviewPane.AutoHideBelowContentDip - 1;
        var s = PreviewPane.OnWidth(PreviewPane.Initial, narrow);
        s = PreviewPane.ApplyToggle(s);
        var layout = PreviewPane.Layout(PreviewPane.Effective(s, narrow));
        Assert.Equal(PaneMode.Docked, layout.Mode);
        Assert.True(layout.DockHostVisible);
        Assert.False(layout.OverlayVisible);
        Assert.True(layout.Preview > 0);
        Assert.Equal(layout.Preview, PreviewPane.GridPreviewColumnWidth(layout), 0.01);
        Assert.True(PreviewPane.NeedsDockColumnRelayout(PaneMode.Hidden, PaneMode.Docked));
    }
}
