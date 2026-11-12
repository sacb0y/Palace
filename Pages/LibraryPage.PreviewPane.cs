using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Palace.Helpers;
using Palace.Services;
using Windows.Foundation;
using Windows.System;
using Windows.UI.ViewManagement;

namespace Palace.Pages;

public sealed partial class LibraryPage
{
    private bool _suppressPreviewToggle;
    private bool _previewResizeDragging;
    private double _previewResizeStartWidth;
    private double _previewResizeStartX;
    private uint _previewResizePointerId;
    private bool _previewBodyInOverlay;
    private PaneMode _previewVisualMode = PaneMode.Hidden;
    private UISettings? _previewUiSettings;
    private bool? _previewAnimationsEnabled;
    private bool _previewDockProbeHooked;
    private long _previewDockProbeLastTick;

    private void HookPreviewPaneSettings()
    {
        PreviewPaneSettings.Changed -= PreviewPaneSettings_Changed;
        PreviewPaneSettings.Changed += PreviewPaneSettings_Changed;
        AppServices.LoadLibrarySettings();
        // Keep session Overlay/Pinned across NavigationCacheMode reuse; prefs only.
        _previewPane = _previewPane with
        {
            UserShow = PreviewPaneSettings.UserShow,
            UserWidth = PreviewPaneSettings.UserWidth
        };
        HookPreviewDockLayoutProbe();
        RefreshLibraryLayout();
    }

    private void UnhookPreviewPaneSettings()
    {
        PreviewPaneSettings.Changed -= PreviewPaneSettings_Changed;
        UnhookPreviewDockLayoutProbe();
        if (_previewUiSettings is not null)
        {
            _previewUiSettings.AnimationsEnabledChanged -= PreviewUiSettings_AnimationsEnabledChanged;
            _previewUiSettings = null;
        }

        _previewAnimationsEnabled = null;
    }

    private void PreviewPaneSettings_Changed(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var width = PreviewPaneSettings.UserWidth;
            var show = PreviewPaneSettings.UserShow;
            _previewPane = _previewPane with
            {
                UserShow = show,
                UserWidth = width,
                OverlayOpen = false,
                Pinned = false
            };
            RefreshLibraryLayout();
        });
    }

    private void TglPreviewPane_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressPreviewToggle)
        {
            return;
        }

        ApplyPreviewToggleFromUser();
    }

    private void BtnPreviewPaneEdge_Click(object sender, RoutedEventArgs e) =>
        ApplyPreviewToggleFromUser();

    /// <summary>Alt+P on the Library page (no AccessKey P).</summary>
    private bool TryHandlePreviewPaneAccelerator(KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.P
            || !IsKeyDown(VirtualKey.Menu)
            || IsKeyDown(VirtualKey.Control)
            || IsKeyDown(VirtualKey.Shift)
            || ViewModel.IsGalleryOverlayOpen
            || FocusIsTextInput(e.OriginalSource))
        {
            return false;
        }

        ApplyPreviewToggleFromUser();
        return true;
    }

    private void BtnPreviewPaneEdge_PointerEntered(object sender, PointerRoutedEventArgs e) =>
        BtnPreviewPaneEdge.Opacity = 1;

    private void BtnPreviewPaneEdge_PointerExited(object sender, PointerRoutedEventArgs e) =>
        BtnPreviewPaneEdge.Opacity = 0.35;

    private void BtnPreviewOverlayClose_Click(object sender, RoutedEventArgs e)
    {
        // Same as Esc / empty-grid light-dismiss — toggle unchecked while auto-hidden.
        _previewPane = PreviewPane.CloseOverlay(_previewPane);
        PersistPreviewUserShowAndWidth();
        RefreshLibraryLayout();
        SyncPreviewToggleButton();
    }

    private void ApplyPreviewToggleFromUser()
    {
        _previewPane = PreviewPane.ApplyToggle(_previewPane);
        // Toolbar toggle under Hide/Show prefs: mirror into Automatic + UserShow when needed.
        if (string.Equals(PreviewPaneSettings.Mode, PreviewPaneSettings.ModeHide, StringComparison.Ordinal)
            && _previewPane.UserShow)
        {
            PersistPreviewMode(PreviewPaneSettings.ModeAutomatic, _previewPane.UserShow, _previewPane.UserWidth);
        }
        else if (string.Equals(PreviewPaneSettings.Mode, PreviewPaneSettings.ModeShow, StringComparison.Ordinal)
            && !_previewPane.UserShow)
        {
            PersistPreviewMode(PreviewPaneSettings.ModeAutomatic, false, _previewPane.UserWidth);
        }
        else
        {
            PersistPreviewUserShowAndWidth();
        }

        RefreshLibraryLayout();
    }

    private void PersistPreviewUserShowAndWidth()
    {
        var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
        values[PreviewPaneSettings.UserShowKey] = _previewPane.UserShow;
        values[PreviewPaneSettings.WidthKey] = _previewPane.UserWidth;
        PreviewPaneSettings.Apply(
            PreviewPaneSettings.Mode,
            _previewPane.UserShow,
            _previewPane.UserWidth);
    }

    private void PersistPreviewMode(string mode, bool userShow, double width)
    {
        var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
        values[PreviewPaneSettings.ModeKey] = mode;
        values[PreviewPaneSettings.UserShowKey] = userShow;
        values[PreviewPaneSettings.WidthKey] = width;
        PreviewPaneSettings.Apply(mode, userShow, width);
    }

    private bool TryDismissPreviewOverlayOnEscape()
    {
        if (!_previewPane.OverlayOpen)
        {
            return false;
        }

        _previewPane = PreviewPane.SetOverlayOpen(_previewPane, false);
        RefreshLibraryLayout();
        return true;
    }

    private bool TryDismissPreviewOverlayOnEmptyGrid(DependencyObject? source)
    {
        if (!_previewPane.OverlayOpen)
        {
            return false;
        }

        // Tile / chrome under the mosaic — leave overlay open so selection can update it.
        if (source is not null && (IsDescendantOf(source, GrdAssets) || ReferenceEquals(source, GrdAssets)))
        {
            var walk = source;
            while (walk is not null && !ReferenceEquals(walk, GrdAssets))
            {
                if (walk is ItemContainer or Image)
                {
                    return false;
                }

                walk = VisualTreeHelper.GetParent(walk);
            }

            // Empty ItemsView / scroll chrome click.
            if (ReferenceEquals(walk, GrdAssets) || source is ScrollViewer or ScrollView)
            {
                _previewPane = PreviewPane.SetOverlayOpen(_previewPane, false);
                RefreshLibraryLayout();
                return true;
            }
        }

        return false;
    }

    private void SyncPreviewChrome(PreviewPaneEffective effective)
    {
        var mode = effective.Mode;
        var fromMode = _previewVisualMode;
        var layout = PreviewPane.Layout(effective);
        HostPreviewBody(mode);
        ApplyPreviewOverlayWidth();
        UpdatePreviewEdgeTab(mode);
        SyncPreviewToggleButton();

        // Dock host: never Collapsed after load. FENRIR #177 — Collapsed→Visible left
        // ScrPreviewPane at the folder-tree X. Opacity + hit-test hide; ColPreview is the
        // real column (Grid owns X). Never write Composition Visual.Offset on the host.
        GrdPreviewDockHost.Visibility = Visibility.Visible;
        ScrPreviewPane.Visibility = Visibility.Visible;
        ApplyPreviewDockHostSlot(PreviewPane.DockHostSlot(layout));

        if (mode == PaneMode.Overlay)
        {
            BrdPreviewOverlay.Visibility = Visibility.Visible;
            // Ensure body is parented before first compose frame (ghost: Visible chrome, empty SV).
            if (!ReferenceEquals(ScrPreviewOverlay.Content, PnlPreviewBody))
            {
                HostPreviewBody(PaneMode.Overlay);
            }
        }

        if (mode == PaneMode.Overlay || fromMode == PaneMode.Overlay)
        {
            AnimatePreviewOverlayChange(fromMode, mode);
        }

        _previewVisualMode = mode;

        if (mode != PaneMode.Overlay)
        {
            BrdPreviewOverlay.Visibility = Visibility.Collapsed;
            ResetPreviewOverlayTranslation();
        }
        else if (fromMode == PaneMode.Overlay)
        {
            // Refresh while already open — keep Translation.Z for ThemeShadow (9c8f684).
            EnsurePreviewOverlayTranslationResting();
        }

        ProbePreviewDockLayout("SyncPreviewChrome");
    }

    /// <summary>
    /// Re-assert ColPreview dock slot every layout pass. Grid owns X (Column=2 Stretch).
    /// Idempotent from current size — no MarginLeft / width-delta gating.
    /// </summary>
    private void ApplyPreviewDockHostSlot(PreviewDockHostSlot slot)
    {
        Grid.SetColumn(GrdPreviewDockHost, slot.Column);
        Grid.SetColumnSpan(GrdPreviewDockHost, slot.ColumnSpan);
        Grid.SetRow(GrdPreviewDockHost, slot.Row);
        GrdPreviewDockHost.HorizontalAlignment = slot.Stretch
            ? HorizontalAlignment.Stretch
            : HorizontalAlignment.Left;
        GrdPreviewDockHost.VerticalAlignment = VerticalAlignment.Stretch;
        GrdPreviewDockHost.ClearValue(MarginProperty);
        if (double.IsNaN(slot.Width))
        {
            GrdPreviewDockHost.ClearValue(WidthProperty);
        }
        else
        {
            GrdPreviewDockHost.Width = slot.Width;
        }

        GrdPreviewDockHost.Opacity = slot.Opacity;
        GrdPreviewDockHost.IsHitTestVisible = slot.HitTestVisible;
    }

    private void HookPreviewDockLayoutProbe()
    {
        if (_previewDockProbeHooked || !PreviewDockLayoutProbe.IsEnabled)
        {
            return;
        }

        GrdPreviewDockHost.LayoutUpdated += PreviewDockHost_LayoutUpdated;
        _previewDockProbeHooked = true;
        LibraryLayoutRefreshProbe.LogEnabled = true;
        PreviewDockLayoutProbe.Log("Hook", "LayoutUpdated probe armed");
    }

    private void UnhookPreviewDockLayoutProbe()
    {
        if (!_previewDockProbeHooked)
        {
            return;
        }

        GrdPreviewDockHost.LayoutUpdated -= PreviewDockHost_LayoutUpdated;
        _previewDockProbeHooked = false;
    }

    private void PreviewDockHost_LayoutUpdated(object? sender, object e)
    {
        if (!PreviewDockLayoutProbe.IsEnabled)
        {
            return;
        }

        // Throttle: LayoutUpdated is extremely chatty.
        var now = Environment.TickCount64;
        if (now - _previewDockProbeLastTick < 50)
        {
            return;
        }

        _previewDockProbeLastTick = now;
        ProbePreviewDockLayout("LayoutUpdated");
    }

    private void ProbePreviewDockLayout(string source)
    {
        if (!PreviewDockLayoutProbe.IsEnabled)
        {
            return;
        }

        try
        {
            var windowDip = ResolveWindowClientDip();
            var appClientSizeW = ResolveAppWindowClientSizeWidth();
            var appDip = ResolveAppWindowClientDip();
            var rootDip = XamlRoot?.Size.Width ?? 0;
            var scale = ResolveRasterizationScale();
            var pageActualW = ActualWidth;
            var slotDip = ResolveShellContentSlotWidthDip();
            var navDip = ResolveShellNavPaneWidthDip();
            var pagePlusNav = pageActualW + navDip;
            var contentWidth = ResolveAvailableContentWidth(windowDip);
            var refreshRate = LibraryLayoutRefreshProbe.CallsPerSecond();
            var effective = PreviewPane.Effective(_previewPane, contentWidth);
            var layout = PreviewPane.Layout(effective);
            var slot = PreviewPane.DockHostSlot(layout);
            var hostVisual = ElementCompositionPreview.GetElementVisual(GrdPreviewDockHost);
            var states = CollectLibraryVisualStates();
            var snap = PreviewDockLayoutProbe.BuildSnapshot(
                windowW: windowDip,
                windowH: XamlRoot?.Size.Height ?? ActualHeight,
                layout: layout,
                slot: slot,
                colTree: ColFolderTree.Width.Value,
                colMosaic: ColMosaic.Width.Value,
                colPreview: ColPreview.Width.Value,
                hostOffsetX: GrdPreviewDockHost.ActualOffset.X,
                hostOffsetY: GrdPreviewDockHost.ActualOffset.Y,
                hostActualW: GrdPreviewDockHost.ActualWidth,
                hostActualH: GrdPreviewDockHost.ActualHeight,
                hostWidthProp: GrdPreviewDockHost.Width,
                hostColumn: Grid.GetColumn(GrdPreviewDockHost),
                hostColumnSpan: Grid.GetColumnSpan(GrdPreviewDockHost),
                hostAlign: GrdPreviewDockHost.HorizontalAlignment.ToString(),
                hostMargin: GrdPreviewDockHost.Margin.ToString(),
                paneOffsetX: ScrPreviewPane.ActualOffset.X,
                paneActualW: ScrPreviewPane.ActualWidth,
                visualOffsetX: hostVisual.Offset.X,
                visualOffsetY: hostVisual.Offset.Y,
                visualStates: states);

            var expected = PreviewPane.DockHostExpectedOffsetX(layout);
            var drift = GrdPreviewDockHost.ActualOffset.X - expected;
            // Read-only: ActualOffset vs Visual.Offset; also auto-hide inputs for Studio.
            PreviewDockLayoutProbe.Log(
                source,
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{snap} expectX={expected:0.#} driftX={drift:0.#} " +
                    $"autoHideEnabled={PreviewPaneSettings.AutoHideEnabled} " +
                    $"mode={PreviewPaneSettings.Mode} content={contentWidth:0.#} " +
                    $"autoHidden={_previewPane.AutoHidden} pinned={_previewPane.Pinned} " +
                    $"overlayOpen={_previewPane.OverlayOpen} eff={effective.Mode} " +
                    $"xamlRootW={rootDip:0.#} appClientSizeW={appClientSizeW:0.#} " +
                    $"appClientDip={appDip:0.#} pageActualW={pageActualW:0.#} " +
                    $"shellSlotW={slotDip:0.#} refreshRate={refreshRate:0.##}/s " +
                    $"nav={navDip:0.#} pagePlusNav={pagePlusNav:0.#} " +
                    $"windowDip={windowDip:0.#} scale={scale:0.###}"));
        }
        catch (Exception ex)
        {
            PreviewDockLayoutProbe.Log(source, "probe-failed: " + ex.GetType().Name);
        }
    }

    private static string CollectLibraryVisualStates()
    {
        // LibraryPage has no AdaptiveTrigger / VSM groups targeting the dock host
        // (grepped 2026-11-12). Keep an empty marker so Studio logs prove the scan ran.
        return "none-on-LibraryPage";
    }

    private void HostPreviewBody(PaneMode mode)
    {
        // Re-parent PnlPreviewBody between dock/overlay SVs. After leave ColPreview=0 /
        // Opacity=0, DesiredSize can stick at 0×0 — always InvalidateMeasure after assign.
        if (mode == PaneMode.Overlay)
        {
            if (!ReferenceEquals(ScrPreviewOverlay.Content, PnlPreviewBody))
            {
                if (ReferenceEquals(ScrPreviewPane.Content, PnlPreviewBody))
                {
                    ScrPreviewPane.Content = null;
                }

                ScrPreviewOverlay.Content = PnlPreviewBody;
            }

            _previewBodyInOverlay = true;
            InvalidatePreviewBodyLayout(ScrPreviewOverlay);
            return;
        }

        if (!ReferenceEquals(ScrPreviewPane.Content, PnlPreviewBody))
        {
            if (ReferenceEquals(ScrPreviewOverlay.Content, PnlPreviewBody))
            {
                ScrPreviewOverlay.Content = null;
            }

            ScrPreviewPane.Content = PnlPreviewBody;
        }

        _previewBodyInOverlay = false;
        InvalidatePreviewBodyLayout(ScrPreviewPane);
    }

    private void InvalidatePreviewBodyLayout(ScrollViewer host)
    {
        try
        {
            PnlPreviewBody.InvalidateMeasure();
            PnlPreviewBody.InvalidateArrange();
            host.InvalidateMeasure();
            host.InvalidateArrange();
        }
        catch
        {
            // Layout pass may not be ready.
        }
    }

    private void ApplyPreviewOverlayWidth()
    {
        // Visibility is applied in SyncPreviewChrome (Visible before motion; Collapsed after).
        // Width from UserWidth; top margin clears toolbar (sibling tree — not Grid.Row=1).
        var contentWidth = ResolveAvailableContentWidth(ResolveWindowClientDip());
        BrdPreviewOverlay.Width = PreviewPane.OverlayPanelWidth(_previewPane, contentWidth);
        BrdPreviewOverlay.Margin = new Thickness(
            0,
            ResolvePreviewOverlayTopMargin(),
            PreviewPane.OverlayOuterMarginRightDip,
            PreviewPane.OverlayOuterMarginBottomDip);
    }

    /// <summary>
    /// Toolbar bottom Y in page space → top margin for page-level sibling overlay.
    /// </summary>
    private double ResolvePreviewOverlayTopMargin()
    {
        try
        {
            if (GrdLibraryToolbarHost.ActualHeight > 0)
            {
                var transform = GrdLibraryToolbarHost.TransformToVisual(this);
                var bounds = transform.TransformBounds(
                    new Windows.Foundation.Rect(
                        0,
                        0,
                        GrdLibraryToolbarHost.ActualWidth,
                        GrdLibraryToolbarHost.ActualHeight));
                return PreviewPane.OverlayTopMarginBelowToolbar(bounds.Bottom);
            }
        }
        catch
        {
            // Transform unavailable before first layout.
        }

        return PreviewPane.OverlayTopMarginBelowToolbar(0);
    }

    private void UpdatePreviewEdgeTab(PaneMode mode)
    {
        // Gooey / Doktor: faint ›/‹ handle when docked or hidden; hide entirely while
        // overlay is open (no ‹ tab beside the panel).
        if (mode == PaneMode.Overlay)
        {
            BtnPreviewPaneEdge.Visibility = Visibility.Collapsed;
            return;
        }

        BtnPreviewPaneEdge.Visibility = Visibility.Visible;
        BtnPreviewPaneEdge.Opacity = 0.35;
        var open = mode == PaneMode.Docked;
        TxtPreviewPaneEdge.Text = open ? "‹" : "›";
        BtnPreviewPaneEdge.HorizontalAlignment = HorizontalAlignment.Right;
        if (mode == PaneMode.Docked)
        {
            // Sit on the pane's left edge (right edge of mosaic).
            var marginRight = _previewPane.UserWidth + 12; // page pad
            BtnPreviewPaneEdge.Margin = new Thickness(0, 0, marginRight, 0);
        }
        else
        {
            BtnPreviewPaneEdge.Margin = new Thickness(0);
        }
    }

    private void SyncPreviewToggleButton()
    {
        var check = PreviewPane.IsChromeChecked(_previewPane);
        var tip = PreviewPane.ChromeTooltip(_previewPane);
        _suppressPreviewToggle = true;
        try
        {
            TglPreviewPane.IsChecked = check;
            ToolTipService.SetToolTip(TglPreviewPane, tip);
            ToolTipService.SetToolTip(BtnPreviewPaneEdge, tip);
        }
        finally
        {
            _suppressPreviewToggle = false;
        }
    }

    private void AnimatePreviewOverlayChange(PaneMode fromMode, PaneMode toMode)
    {
        if (fromMode == toMode || !PreviewMotionEnabled())
        {
            return;
        }

        // Slide via Translation only — never Visual.Offset (hand-out Offset IS layout X).
        UIElement? target = null;
        var showing = false;
        if (toMode == PaneMode.Overlay && fromMode != PaneMode.Overlay)
        {
            target = BrdPreviewOverlay;
            showing = true;
        }
        else if (fromMode == PaneMode.Overlay && toMode != PaneMode.Overlay)
        {
            target = BrdPreviewOverlay;
            showing = false;
        }

        if (target is null)
        {
            return;
        }

        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(target, true);
            var visual = ElementCompositionPreview.GetElementVisual(target);
            visual.StopAnimation("Translation.X");
            var compositor = visual.Compositor;
            var ms = showing ? PreviewPane.MotionInMs : PreviewPane.MotionOutMs;
            var ease = showing
                ? compositor.CreateCubicBezierEasingFunction(
                    new System.Numerics.Vector2(0.1f, 0.9f),
                    new System.Numerics.Vector2(0.2f, 1f))
                : compositor.CreateCubicBezierEasingFunction(
                    new System.Numerics.Vector2(0.7f, 0f),
                    new System.Numerics.Vector2(1f, 0.5f));
            // Keep Z=32 for ThemeShadow (9c8f684 Translation="0,0,32"); only slide X.
            const float shadowZ = 32f;
            var from = showing ? 24f : 0f;
            var to = showing ? 0f : 24f;
            visual.Properties.InsertVector3("Translation", new System.Numerics.Vector3(from, 0f, shadowZ));
            var anim = compositor.CreateScalarKeyFrameAnimation();
            anim.InsertKeyFrame(0f, from, ease);
            anim.InsertKeyFrame(1f, to, ease);
            anim.Duration = TimeSpan.FromMilliseconds(ms);
            visual.StartAnimation("Translation.X", anim);
        }
        catch
        {
            // Composition unavailable — layout still correct.
            EnsurePreviewOverlayTranslationResting();
        }
    }

    private void ResetPreviewOverlayTranslation() =>
        EnsurePreviewOverlayTranslationResting();

    /// <summary>
    /// Resting overlay Translation: X/Y=0, Z=32 (ThemeShadow depth from 9c8f684).
    /// Never leave a Transparent shadow host with a non-resting slide.
    /// </summary>
    private void EnsurePreviewOverlayTranslationResting()
    {
        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(BrdPreviewOverlay, true);
            var visual = ElementCompositionPreview.GetElementVisual(BrdPreviewOverlay);
            visual.StopAnimation("Translation.X");
            visual.Properties.InsertVector3("Translation", new System.Numerics.Vector3(0f, 0f, 32f));
        }
        catch
        {
            // Composition unavailable.
        }
    }

    private bool PreviewMotionEnabled()
    {
        try
        {
            var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            if (!AppearanceSettings.ParseAnimations(values[AppearanceSettings.AnimationsKey]))
            {
                return false;
            }

            _previewUiSettings ??= new UISettings();
            _previewUiSettings.AnimationsEnabledChanged -= PreviewUiSettings_AnimationsEnabledChanged;
            _previewUiSettings.AnimationsEnabledChanged += PreviewUiSettings_AnimationsEnabledChanged;
            _previewAnimationsEnabled ??= _previewUiSettings.AnimationsEnabled;
            return _previewAnimationsEnabled.Value;
        }
        catch
        {
            return true;
        }
    }

    private void PreviewUiSettings_AnimationsEnabledChanged(UISettings sender, object args) =>
        _previewAnimationsEnabled = sender.AnimationsEnabled;

    private void BrdPreviewResize_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_previewPaneMode != PaneMode.Docked)
        {
            return;
        }

        _previewResizeDragging = true;
        _previewResizeStartWidth = _previewPane.UserWidth;
        _previewResizeStartX = e.GetCurrentPoint(this).Position.X;
        _previewResizePointerId = e.Pointer.PointerId;
        BrdPreviewResize.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void BrdPreviewResize_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_previewResizeDragging || e.Pointer.PointerId != _previewResizePointerId)
        {
            return;
        }

        var x = e.GetCurrentPoint(this).Position.X;
        // Dragging the left edge: move right → narrower pane.
        var delta = _previewResizeStartX - x;
        _previewPane = PreviewPane.SetUserWidth(_previewPane, _previewResizeStartWidth + delta);
        RefreshLibraryLayout();
        e.Handled = true;
    }

    private void BrdPreviewResize_PointerReleased(object sender, PointerRoutedEventArgs e) =>
        EndPreviewResize(e.Pointer);

    private void BrdPreviewResize_PointerCanceled(object sender, PointerRoutedEventArgs e) =>
        EndPreviewResize(e.Pointer);

    private void BrdPreviewResize_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        EndPreviewResize(e.Pointer);

    private void EndPreviewResize(Pointer pointer)
    {
        if (!_previewResizeDragging)
        {
            return;
        }

        _previewResizeDragging = false;
        try
        {
            BrdPreviewResize.ReleasePointerCapture(pointer);
        }
        catch
        {
            // Already released.
        }

        PersistPreviewUserShowAndWidth();
    }

    private void BrdPreviewResize_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _previewPane = PreviewPane.ResetUserWidth(_previewPane);
        PersistPreviewUserShowAndWidth();
        RefreshLibraryLayout();
        e.Handled = true;
    }

    /// <summary>Keep mosaic scroll anchored on the first visible tile across column reflow.</summary>
    private void AnchorMosaicOnFirstVisibleTile()
    {
        try
        {
            if (_mosaicScrollViewer is null && _mosaicScrollView is null)
            {
                return;
            }

            // ItemsRepeater / ItemsView keep offset when host width changes if we
            // restore VerticalOffset after layout. Capture now; restore next tick.
            var offset = _mosaicScrollViewer?.VerticalOffset
                ?? _mosaicScrollView?.VerticalOffset
                ?? 0;
            DispatcherQueue.TryEnqueue(() =>
            {
                _mosaicScrollViewer?.ChangeView(null, offset, null, disableAnimation: true);
                _mosaicScrollView?.ScrollTo(
                    0,
                    offset,
                    new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
            });
        }
        catch
        {
            // Scroll host may not be ready.
        }
    }
}
