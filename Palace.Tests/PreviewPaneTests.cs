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
    public void Effective_OverlayAllocatesPreviewZero()
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
    public void ClampUserWidth_Bounds280To560()
    {
        Assert.Equal(280, PreviewPane.ClampUserWidth(100));
        Assert.Equal(560, PreviewPane.ClampUserWidth(900));
        Assert.Equal(340, PreviewPane.ClampUserWidth(double.NaN));
    }
}
