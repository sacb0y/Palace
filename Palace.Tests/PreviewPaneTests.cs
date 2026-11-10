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
    public void ApplyToggle_AutoHiddenOpensOverlay_DoesNotFlipUserShowOff()
    {
        var s = PreviewPane.OnWidth(PreviewPane.Initial, PreviewPane.AutoHideBelowContentDip - 1);
        Assert.True(s.AutoHidden);
        Assert.True(s.UserShow);
        Assert.False(PreviewPane.IsChromeChecked(s));
        Assert.Equal(PreviewPane.AutoHiddenTooltip, PreviewPane.ChromeTooltip(s));

        s = PreviewPane.ApplyToggle(s);
        Assert.True(s.UserShow);
        Assert.True(s.OverlayOpen);
        Assert.True(PreviewPane.IsChromeChecked(s));
        Assert.Equal(PaneMode.Overlay, PreviewPane.Effective(s, PreviewPane.AutoHideBelowContentDip - 1).Mode);

        s = PreviewPane.ApplyToggle(s);
        Assert.False(s.OverlayOpen);
        Assert.True(s.UserShow);
        Assert.False(PreviewPane.IsChromeChecked(s));
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
        s = PreviewPane.ApplyToggle(s);
        Assert.Equal(PaneMode.Overlay, PreviewPane.Effective(s, narrow).Mode);

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
    public void Effective_OverlayAllocatesPreviewZero_EvenWhenWide()
    {
        var wide = PreviewPane.AutoShowAboveContentDip + 100;
        var s = PreviewPane.OnWidth(PreviewPane.Initial, wide);
        s = PreviewPane.SetOverlayOpen(s, true);
        var eff = PreviewPane.Effective(s, wide);
        Assert.Equal(PaneMode.Overlay, eff.Mode);
        Assert.Equal(0, eff.Widths.Preview);
        Assert.True(eff.Widths.PreviewHidden);
    }

    [Fact]
    public void Effective_OverlayWhileAutoHidden_DoesNotReflowGrid()
    {
        var narrow = PreviewPane.AutoHideBelowContentDip - 1;
        var s = PreviewPane.OnWidth(PreviewPane.Initial, narrow);
        var without = PreviewPane.Effective(s, narrow);
        s = PreviewPane.ApplyToggle(s);
        var overlay = PreviewPane.Effective(s, narrow);
        Assert.Equal(PaneMode.Overlay, overlay.Mode);
        Assert.Equal(without.Widths.Mosaic, overlay.Widths.Mosaic, 0.01);
        Assert.Equal(0, overlay.Widths.Preview);
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
    public void Layout_DockedHideShowRestoresDockHostWidth_GridPreviewColumnStaysZero()
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
        Assert.Equal(0, PreviewPane.GridPreviewColumnWidth(docked));
        Assert.True(docked.Mosaic >= LibraryColumnLayout.MosaicMinWidth - 0.01);

        // Hide → show three times: dock host width + mosaic reservation restore;
        // Grid ColPreview stays 0 (right-aligned host — no Width 0↔N cycle).
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
            Assert.True(hidden.Mosaic > docked.Mosaic);

            s = PreviewPane.ApplyToggle(s);
            var shown = PreviewPane.Layout(PreviewPane.Effective(s, wide));
            Assert.Equal(PaneMode.Docked, shown.Mode);
            Assert.True(shown.DockHostVisible);
            Assert.False(shown.OverlayVisible);
            Assert.Equal(userWidth, shown.Preview, 0.01);
            Assert.Equal(userWidth, PreviewPane.DockHostWidth(shown), 0.01);
            Assert.Equal(0, PreviewPane.GridPreviewColumnWidth(shown));
            Assert.Equal(docked.Mosaic, shown.Mosaic, 0.01);
            Assert.Equal(docked.Tree, shown.Tree, 0.01);
            Assert.True(PreviewPane.NeedsDockColumnRelayout(PaneMode.Hidden, shown.Mode));
            Assert.True(PreviewPane.NeedsDockColumnRelayout(shown.Mode, PaneMode.Hidden));
            Assert.False(PreviewPane.NeedsDockColumnRelayout(PaneMode.Docked, PaneMode.Docked));
        }
    }

    [Fact]
    public void Layout_OverlayDoesNotClaimPreviewColumn()
    {
        var narrow = PreviewPane.AutoHideBelowContentDip - 1;
        var s = PreviewPane.OnWidth(PreviewPane.Initial, narrow);
        s = PreviewPane.ApplyToggle(s);
        var overlay = PreviewPane.Layout(PreviewPane.Effective(s, narrow));
        Assert.Equal(PaneMode.Overlay, overlay.Mode);
        Assert.False(overlay.DockHostVisible);
        Assert.True(overlay.OverlayVisible);
        Assert.Equal(0, overlay.Preview);
        Assert.Equal(0, PreviewPane.DockHostWidth(overlay));
        Assert.Equal(0, PreviewPane.GridPreviewColumnWidth(overlay));
        Assert.False(PreviewPane.NeedsDockColumnRelayout(PaneMode.Hidden, PaneMode.Overlay));
    }
}
