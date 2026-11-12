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
        Assert.True(double.IsNaN(PreviewPane.DockHostWidth(layout)));
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
        Assert.True(double.IsNaN(PreviewPane.DockHostWidth(docked)));
        Assert.Equal(userWidth, PreviewPane.GridPreviewColumnWidth(docked), 0.01);
        var dockShown = PreviewPane.DockChrome(docked);
        Assert.True(double.IsNaN(dockShown.Width));
        Assert.Equal(1, dockShown.Opacity);
        Assert.True(dockShown.HitTestVisible);
        Assert.True(docked.Mosaic >= LibraryColumnLayout.MosaicMinWidth - 0.01);

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
            // Docked→Docked must still invalidate (height-only window resize).
            Assert.True(PreviewPane.NeedsDockColumnRelayout(PaneMode.Docked, PaneMode.Docked));
        }
    }

    [Fact]
    public void NeedsDockColumnRelayout_HeightOnlyWhileDocked_StillInvalidates()
    {
        Assert.True(PreviewPane.NeedsDockColumnRelayout(PaneMode.Docked, PaneMode.Docked));
        Assert.True(PreviewPane.NeedsDockColumnRelayout(PaneMode.Hidden, PaneMode.Docked));
        Assert.True(PreviewPane.NeedsDockColumnRelayout(PaneMode.Docked, PaneMode.Hidden));
        Assert.False(PreviewPane.NeedsDockColumnRelayout(PaneMode.Hidden, PaneMode.Hidden));
    }

    [Fact]
    public void Layout_SameContentWidth_IdempotentAcrossRepeatedPasses()
    {
        // Height-only resize: content width unchanged; every pass must yield the
        // same column state (no width-delta gating in the reducer).
        var width = PreviewPane.AutoShowAboveContentDip + 80;
        var s = PreviewPane.OnWidth(PreviewPane.Initial with { UserWidth = 360 }, width);
        var first = PreviewPane.Layout(PreviewPane.Effective(s, width));
        Assert.Equal(PaneMode.Docked, first.Mode);
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
            Assert.Equal(2, slot.Column);
            Assert.Equal(1, slot.ColumnSpan);
            Assert.Equal(1, slot.Row);
            Assert.True(slot.Stretch);
            Assert.True(double.IsNaN(slot.Width));
            Assert.Equal(1, slot.Opacity);
            Assert.True(slot.HitTestVisible);
            Assert.True(PreviewPane.NeedsDockColumnRelayout(pass.Mode, pass.Mode));
        }
    }

    [Fact]
    public void DockHostSlot_Hidden_ZeroWidthColumn2()
    {
        var wide = PreviewPane.AutoShowAboveContentDip + 100;
        var s = PreviewPane.OnWidth(PreviewPane.Initial with { UserShow = false }, wide);
        var layout = PreviewPane.Layout(PreviewPane.Effective(s, wide));
        var slot = PreviewPane.DockHostSlot(layout);
        Assert.Equal(PaneMode.Hidden, layout.Mode);
        Assert.Equal(2, slot.Column);
        Assert.Equal(1, slot.ColumnSpan);
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
