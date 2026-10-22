using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Display;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Palace.Helpers;
using Palace.Services;
using Palace.ViewModels;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Palace.Controls;

public sealed partial class GalleryStillSurface : UserControl
{
    private GalleryViewModel? _gallery;
    private HdrSwapchainPresenter? _presenter;
    private HdrFrame? _hdrFrame;
    private string? _hdrFramePath;
    private float _hdrMapMaxNits;
    private string? _sdrTonePath;
    private float _sdrToneExposure = 1f;
    private float _sdrToneWhite;
    private bool? _sdrToneDisplayHdr;
    private bool _advancedColorHooked;
    private int _advancedColorHookAttempts;
    private const int AdvancedColorHookMaxAttempts = 8;
    private DisplayInformation? _displayInfo;
    private HdrProbe _hdrFrameProbe = HdrProbe.None;
    private int _epoch;
    private int _histEpoch;
    private CancellationTokenSource? _hdrLoadCts;
    private CancellationTokenSource? _presentCts;
    private CancellationTokenSource? _histCts;
    private readonly HdrPresentCoalescer _presentQueue = new();
    private bool _panning;
    private double _panLastX;
    private double _panLastY;
    private double _pinchZoom = 1;
    private bool _preservePinchPan;
    private double _pinchOriginX;
    private double _pinchOriginY;
    private double _coalescedPanX;
    private double _coalescedPanY;
    private PointerDeviceType _lastPointerDevice = PointerDeviceType.Mouse;
    private bool _afterPinchAwaitingLift;
    private bool _reviewSwipeActive;
    private double _reviewSwipeDx;
    private double _reviewSwipeDy;
    private double _reviewSwipeVx;
    private double _reviewSwipeVy;
    private bool _pagerHandoff;
    private double _pagerPanBeforeHandoff;
    private bool _pagerSettling;
    private Storyboard? _pagerStoryboard;
    private Action? _pagerOnDone;
    private readonly SwipeZoomRestore _zoomRestore = new();
    private bool _pendingAdvanceSlide;
    private int _pendingAdvanceSlot; // -1 previous / +1 next
    private bool _pagerAwaitingContent;
    private int _awaitingEpoch;
    private int _pagerSettleGeneration;
    private double _pagerX;
    private string? _neighborPrevPath;
    private string? _neighborNextPath;
    private SwipeZoomRestore.Snapshot? _pendingZoomRestore;
    private UISettings? _uiSettings;
    private bool? _cachedAnimationsEnabled;
    private CompositionPropertySet? _pagerProps;
    private bool _pagerExpressionsReady;
    private SolidColorBrush? _rejectTintBrush;
    private SolidColorBrush? _keepTintBrush;

    private bool _isReviewMode;

    /// <summary>
    /// When false (normal viewer), swipe is browse prev/next only.
    /// When true (Review mode, #129), left=reject, right=keep, up=skip.
    /// Review never reveals the neighbor during drag (docs/specs/swipe-nav.md).
    /// </summary>
    public bool IsReviewMode
    {
        get => _isReviewMode;
        set
        {
            if (_isReviewMode == value)
            {
                return;
            }

            _isReviewMode = value;
            RefreshNeighbors();
        }
    }

    /// <summary>
    /// Fired when a touch swipe commits. Browse: previous/next.
    /// Review (IsReviewMode): reject/keep/skip.
    /// </summary>
    public event Action<TouchGestures.SwipeAction>? ReviewSwipeCommitted;

    /// <summary>
    /// Fired at browse/Review commit start so hosts can cross-fade the
    /// name/position chip (docs/specs/swipe-nav.md B4).
    /// </summary>
    public event Action? ChipCrossFadeRequested;

    /// <summary>
    /// Touch long-press on the still (500ms / &lt;slop). Holding on the overlay
    /// is canceled by Manipulation Translate — this pointer timer is the
    /// reliable path for the viewer select badge.
    /// </summary>
    public event Action? LongPressCommitted;

    private ImageScaling CurrentDisplayScaling() =>
        _gallery?.DisplayScaling ?? ImageScaling.Fit;


    private CancellationTokenSource? _holdCts;
    private double _holdStartX;
    private double _holdStartY;
    private uint _holdPointerId;
    private ImageScaling _scrollScaling;
    private int _scrollRevision = int.MinValue;
    private double _scrollContentW;
    private double _scrollContentH;
    private double _scrollViewW;
    private double _scrollViewH;
    private bool _peakHooked;
    private string? _gifCachePath;
    private string? _gifLoadingPath;
    private IReadOnlyList<GifFrames.Raster>? _gifRasters;
    private WriteableBitmap?[]? _gifBitmaps;
    private CancellationTokenSource? _gifLoadCts;

    private readonly PointerEventHandler _wheelHandler;

    public GalleryStillSurface()
    {
        _wheelHandler = ScrStill_PointerWheelChanged;
        InitializeComponent();
        // Hidden ScrollViewer marks wheel handled (native pan/zoom no-op with
        // ZoomMode Disabled). Learn trackpad pinch is this event — listen even
        // after it is handled so Ctrl+wheel still zooms.
        ScrStill.AddHandler(UIElement.PointerWheelChangedEvent, _wheelHandler, handledEventsToo: true);
        // DPI / composition scale changes resize physical swapchain buffers.
        ScpHdr.CompositionScaleChanged += ScpHdr_CompositionScaleChanged;
        // AppWindowId can be 0 before the island is ready — retry on Loaded.
        Loaded += (_, _) => HookAdvancedColor();
        Unloaded += (_, _) =>
        {
            ScrStill.RemoveHandler(UIElement.PointerWheelChangedEvent, _wheelHandler);
            ScpHdr.CompositionScaleChanged -= ScpHdr_CompositionScaleChanged;
            UnhookPeak();
            UnhookAdvancedColor();
            UnhookUiSettings();
            _gallery?.StopGifPlayback();
            CancelInFlight();
            ClearHdrCache();
            ClearGifCache();
            _presenter?.Dispose();
            _presenter = null;
        };
        Loaded += (_, _) => EnsurePagerExpressions();
    }

    private void ScpHdr_CompositionScaleChanged(SwapChainPanel sender, object args)
    {
        if (_hdrFrame is null)
        {
            return;
        }

        ApplyScaleLayout();
        PresentCached(HdrPresentCoalescer.SettleMs);
    }

    private void HookAdvancedColor()
    {
        if (_advancedColorHooked
            || _advancedColorHookAttempts >= AdvancedColorHookMaxAttempts
            || !OperatingSystem.IsWindows())
        {
            return;
        }

        _advancedColorHookAttempts++;
        try
        {
            // WinUI 3 desktop: GetForCurrentView throws. WASDK DisplayInformation
            // is CreateForWindowId — keep the instance so Unloaded can Dispose it.
            // Leave _advancedColorHooked false when AppWindowId is still 0 so
            // Loaded / RefreshAsync can attach once the island has an id — but
            // cap attempts so Next/Prev cannot retry CreateForWindowId forever.
            var windowId = XamlRoot?.ContentIslandEnvironment?.AppWindowId
                ?? App.Window?.AppWindow?.Id
                ?? default;
            if (windowId.Value == 0)
            {
                return;
            }

            _displayInfo = DisplayInformation.CreateForWindowId(windowId);
            _displayInfo.AdvancedColorInfoChanged += Display_AdvancedColorInfoChanged;
            _advancedColorHooked = true;
        }
        catch (Exception ex)
        {
            ErrorReporter.LogOnly("GalleryStillSurface AdvancedColor hook", ex);
            _displayInfo = null;
            _advancedColorHooked = false;
        }
    }

    private void UnhookAdvancedColor()
    {
        if (_displayInfo is null)
        {
            _advancedColorHooked = false;
            _advancedColorHookAttempts = 0;
            return;
        }

        try
        {
            _displayInfo.AdvancedColorInfoChanged -= Display_AdvancedColorInfoChanged;
            _displayInfo.Dispose();
        }
        catch (Exception ex)
        {
            ErrorReporter.LogOnly("GalleryStillSurface AdvancedColor unhook", ex);
        }

        _displayInfo = null;
        _advancedColorHooked = false;
        _advancedColorHookAttempts = 0;
    }

    private void Display_AdvancedColorInfoChanged(DisplayInformation sender, object args)
    {
        // HDR toggle / SDR white slider / monitor move — re-probe on UI thread.
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_hdrFrame is null)
            {
                return;
            }

            PresentCached(HdrPresentCoalescer.SettleMs);
        });
    }

    public void Bind(GalleryViewModel? gallery)
    {
        if (ReferenceEquals(_gallery, gallery))
        {
            return;
        }

        if (_gallery is not null)
        {
            _gallery.PropertyChanged -= Gallery_PropertyChanged;
            _gallery.StopGifPlayback();
            _gallery.ClearHistogram();
        }

        CancelInFlight();
        ClearHdrCache();
        ClearGifCache();
        ResetPagerVisuals();
        ClearNeighbors();
        ResetView();
        _gallery = gallery;
        if (_gallery is not null)
        {
            HookPeak();
            HookAdvancedColor();
            _gallery.PropertyChanged += Gallery_PropertyChanged;
            _ = RefreshAsync();
            RefreshNeighbors();
            return;
        }

        UnhookPeak();
        HideHdr();
        ClearGifCache();
        ImgStill.Source = null;
        _zoomRestore.Clear();
    }

    private void HookPeak()
    {
        if (_peakHooked)
        {
            return;
        }

        GalleryPeak.Changed += GalleryPeak_Changed;
        _peakHooked = true;
    }

    private void UnhookPeak()
    {
        if (!_peakHooked)
        {
            return;
        }

        GalleryPeak.Changed -= GalleryPeak_Changed;
        _peakHooked = false;
    }

    private void GalleryPeak_Changed(object? sender, EventArgs e) => PresentCached(HdrPresentCoalescer.SettleMs);

    private void CancelInFlight()
    {
        Interlocked.Increment(ref _epoch);
        Interlocked.Increment(ref _histEpoch);
        _hdrLoadCts?.Cancel();
        _presentCts?.Cancel();
        _histCts?.Cancel();
        _gifLoadCts?.Cancel();
    }

    private void Gallery_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryViewModel.VizMode))
        {
            // Heatmap on/off over a presented HDR still: re-raster the cached
            // frame, no new decode. A new still resets CanShowHeatmap first;
            // its own present picks the mode up.
            // VizMode is raised on every CanShowHeatmap change too (each new
            // HDR still), so only re-present when the mode actually differs
            // from the one the last present used.
            if (_gallery is { CanShowHeatmap: true } vg && WantedViz(vg) != _lastPresentedViz)
            {
                PresentCached(HdrPresentCoalescer.ImmediateMs);
            }

            return;
        }

        if (e.PropertyName is nameof(GalleryViewModel.Scaling)
            or nameof(GalleryViewModel.DisplayScaling))
        {
            if (GalleryScale.ShouldResetView(stillChanged: false, scalingChanged: true, viewportChanged: false))
            {
                ResetView();
            }

            ApplyScaleLayout();
            if (_hdrFrame is not null && _gallery is { } g)
            {
                var raster = XamlRoot?.RasterizationScale ?? 1.0;
                var (dipW, dipH) = HdrPanelDips();
                var want = GalleryPresent.PresentDecodeSize(
                    _hdrFrame.NativeWidth,
                    _hdrFrame.NativeHeight,
                    (int)Math.Round(Math.Max(dipW, 0) * raster),
                    (int)Math.Round(Math.Max(dipH, 0) * raster),
                    g.DisplayScaling);
                if (GalleryPresent.NeedsBetterDecode(
                        _hdrFrame.Width, _hdrFrame.Height, want.Width, want.Height))
                {
                    _ = RefreshAsync();
                    return;
                }
            }

            PresentCached(HdrPresentCoalescer.ImmediateMs);
            return;
        }

        if (e.PropertyName is nameof(GalleryViewModel.GifFrameIndex)
            or nameof(GalleryViewModel.GifPlaying))
        {
            if (_gallery is { CanScrubGif: true } g
                && GifFrames.ShouldApplyGifTick(
                    true,
                    GifFrames.CacheMatchesPath(_gifCachePath, g.Current?.Path)
                    && _gifRasters is { Count: > 0 }))
            {
                ApplyCachedGifFrame(g);
            }

            return;
        }

        if (e.PropertyName is nameof(GalleryViewModel.StillRevision)
            or nameof(GalleryViewModel.CanScrubGif))
        {
            if (GalleryScale.ShouldResetView(
                    stillChanged: e.PropertyName == nameof(GalleryViewModel.StillRevision),
                    scalingChanged: false,
                    viewportChanged: false))
            {
                ResetView();
            }
            else
            {
                ResetPinchTracking();
            }

            if (e.PropertyName == nameof(GalleryViewModel.StillRevision))
            {
                // B6 / N2: external navigate (arrows/buttons) stops settle without onDone.
                if (_pagerSettling && !_pagerAwaitingContent)
                {
                    StopPagerAnimations(invokeOnDone: false);
                    ResetPagerVisuals();
                    _pendingAdvanceSlide = false;
                }

                QueueZoomRestoreForCurrent();
                if (_pagerAwaitingContent)
                {
                    if (_pendingAdvanceSlide)
                    {
                        // Review: auto-advance slide-in from ±stride (not neighbor jump).
                        ClearAwaitingContent();
                        HideNeighborSurfaces();
                        RefreshNeighbors();
                        PlayAdvanceSlideIn(_pendingAdvanceSlot);
                        _pendingAdvanceSlide = false;
                    }
                    else
                    {
                        // N3 browse: jump new still to where the neighbor was, settle to 0.
                        FinishAwaitingContentSettle();
                    }
                }
                else
                {
                    HideNeighborSurfaces();
                    RefreshNeighbors();
                }
            }

            _ = RefreshAsync();
        }

        if (e.PropertyName is nameof(GalleryViewModel.CanGoPrevious)
            or nameof(GalleryViewModel.CanGoNext)
            or nameof(GalleryViewModel.CurrentIndex))
        {
            RefreshNeighbors();
        }
    }

    private void GrdStillHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var viewportChanged = !NearlyEqual(e.PreviousSize.Width, e.NewSize.Width)
            || !NearlyEqual(e.PreviousSize.Height, e.NewSize.Height);
        if (GalleryScale.ShouldResetView(stillChanged: false, scalingChanged: false, viewportChanged))
        {
            ResetView();
        }

        ApplyScaleLayout();
        // Nit: keep Composition stride in sync with viewport width.
        if (_pagerProps is not null)
        {
            _pagerProps.InsertScalar(
                "Stride",
                (float)SwipeNav.PageStride(Math.Max(1, ScrStill.ActualWidth)));
        }

        if (_gallery is { IsImage: true } gallery && _hdrFrame is not null)
        {
            var raster = XamlRoot?.RasterizationScale ?? 1.0;
            var (dipW, dipH) = HdrPanelDips();
            var want = GalleryPresent.PresentDecodeSize(
                _hdrFrame.NativeWidth,
                _hdrFrame.NativeHeight,
                (int)Math.Round(Math.Max(dipW, 0) * raster),
                (int)Math.Round(Math.Max(dipH, 0) * raster),
                gallery.DisplayScaling);
            if (GalleryPresent.NeedsBetterDecode(
                    _hdrFrame.Width, _hdrFrame.Height, want.Width, want.Height))
            {
                _ = RefreshAsync();
                return;
            }

            PresentCached(HdrPresentCoalescer.SettleMs);
        }
    }

    private async Task RefreshAsync()
    {
        var gallery = _gallery;
        ApplyScaleLayout();
        // Bind may have run before AppWindowId was ready — retry the ACE hook.
        HookAdvancedColor();
        if (gallery is { IsImage: true, CanScrubGif: true })
        {
            _hdrLoadCts?.Cancel();
            _presentCts?.Cancel();
            if (GifFrames.ShouldBumpHdrEpochOnGifStillRefresh(true))
            {
                Interlocked.Increment(ref _epoch);
            }

            Interlocked.Increment(ref _histEpoch);
            ClearHdrCache();
            HideHdr();
            gallery.SetHdrPresentResult(false, false);
            await ShowGifScrubAsync(gallery);
            return;
        }

        _hdrLoadCts?.Cancel();
        _presentCts?.Cancel();
        var epoch = Interlocked.Increment(ref _epoch);
        Interlocked.Increment(ref _histEpoch);
        if (gallery is not { IsImage: true })
        {
            ClearHdrCache();
            ClearGifCache();
            HideHdr();
            ImgStill.Source = null;
            gallery?.ClearHistogram();
            return;
        }

        ClearGifCache();

        var still = gallery.PreviewImageUri ?? gallery.CurrentPath;
        var item = gallery.Current;
        var pathMatches = still is not null
            && item is not null
            && string.Equals(still, item.Path, StringComparison.OrdinalIgnoreCase);
        var attempt = pathMatches
            && GalleryPresent.ShouldAttemptHdrPresent(
                item!.Kind,
                item.IsOrphan,
                item.IsOnlineOnly,
                AssetItemMapper.IsApiOnly(item),
                CloudFile.Exists(item.Path) && !CloudFile.IsOnlineOnly(item.Path),
                gallery.CurrentProbe);

        // HDR attempt: mosaic thumb until scRGB present wins — never BitmapImage
        // the HDR original (JXR unorm is darker; JXL WIC washes PQ).
        // Non-attempt (JXL wide / await-preview): still first (DisplayPathFor
        // large-preview or null), then mosaic thumb.
        BitmapImage? sdrImage;
        var usedThumb = false;
        if (attempt)
        {
            sdrImage = ToStillImage(item?.ThumbPath, animateGif: true);
            usedThumb = sdrImage is not null;
            sdrImage ??= ToStillImage(still, animateGif: true);
        }
        else
        {
            sdrImage = ToStillImage(still, animateGif: true);
            if (sdrImage is null && item?.ThumbPath is { } thumbPath)
            {
                sdrImage = ToStillImage(thumbPath, animateGif: true);
                usedThumb = sdrImage is not null;
            }
        }

        ImgStill.Source = sdrImage;
        LogSdrStillSurface(
            still,
            usedThumb ? item?.ThumbPath : still,
            usedThumb ? "mosaic-thumb" : (attempt ? "hdr-attempt-still" : null),
            gallery.CurrentProbe);

        if (!pathMatches)
        {
            ClearHdrCache();
            HideHdr();
            gallery.SetHdrPresentResult(false, false);
            gallery.ClearHistogram();
            return;
        }

        if (still is null)
        {
            ClearHdrCache();
            HideHdr();
            gallery.SetHdrPresentResult(false, false);
            gallery.ClearHistogram();
            return;
        }

        if (!attempt)
        {
            ClearHdrCache();
            HideHdr();
            gallery.SetHdrPresentResult(false, false);
            QueueFileHistogram(gallery, still, hdr: false);
            return;
        }

        SdrTonemapLog.TryAppendViewer(
            still,
            "hdr-attempt",
            ProbeBufferLabel(gallery.CurrentProbe));

        var raster = XamlRoot?.RasterizationScale ?? 1.0;
        var (dipW, dipH) = HdrPanelDips();
        var viewPxW = (int)Math.Round(Math.Max(dipW, 0) * raster);
        var viewPxH = (int)Math.Round(Math.Max(dipH, 0) * raster);
        HdrFrame? frame = CachedFrame(still, gallery.CurrentProbe);
        if (frame is not null)
        {
            var want = GalleryPresent.PresentDecodeSize(
                frame.NativeWidth, frame.NativeHeight, viewPxW, viewPxH, gallery.DisplayScaling);
            if (GalleryPresent.NeedsBetterDecode(frame.Width, frame.Height, want.Width, want.Height))
            {
                frame = null;
            }
        }

        if (frame is null)
        {
            // Drop a *different* still's float buffer before the next native
            // decode (fast next/prev must not stack ~1 GB HDR JXL frames).
            // Same-path Fit→1:1 / better-size re-decode keeps the cached frame
            // so cancel/fail does not blank the image still on screen.
            if (_hdrFrame is not null
                && !string.Equals(_hdrFramePath, still, StringComparison.OrdinalIgnoreCase))
            {
                _hdrFrame = null;
            }

            _hdrLoadCts = GalleryPresent.LiveTokenSource(_hdrLoadCts);
            string? loadError = null;
            try
            {
                var loaded = await HdrWicDecode.TryLoadAsync(
                    still,
                    gallery.CurrentProbe,
                    viewPxW,
                    viewPxH,
                    gallery.DisplayScaling,
                    _hdrLoadCts.Token);
                frame = loaded.Frame;
                loadError = loaded.Error;
            }
            catch (OperationCanceledException)
            {
                // Quick Next/Prev cancels the in-flight decode — not an error.
                return;
            }
            catch (Exception ex)
            {
                ErrorReporter.LogOnly("GalleryStillSurface HDR load", ex);
                frame = null;
                loadError = ex.GetType().Name;
            }

            if (epoch != _epoch || !ReferenceEquals(_gallery, gallery))
            {
                return;
            }

            await FinishHdrLoadAsync(
                gallery, still, item, frame, loadError, viewPxW, viewPxH, epoch);
            return;
        }

        if (epoch != _epoch || !ReferenceEquals(_gallery, gallery))
        {
            return;
        }

        await FinishHdrLoadAsync(
            gallery, still, item, frame, loadError: null, viewPxW, viewPxH, epoch);
    }

    private async Task FinishHdrLoadAsync(
        GalleryViewModel gallery,
        string still,
        AssetItem? item,
        HdrFrame? frame,
        string? loadError,
        int viewPxW,
        int viewPxH,
        int epoch)
    {
        await UiDispatch.RunAsync(() =>
        {
            if (epoch != _epoch || !ReferenceEquals(_gallery, gallery))
            {
                return;
            }

            if (frame is null)
            {
                ClearHdrCache();
                HideHdr();
                // Pipeline preview only (mosaic thumb / large-preview) — never
                // BitmapImage the HDR original (JXR unorm → hdr-fail+bitmap-original).
                var failPreview = item?.ThumbPath;
                if (string.IsNullOrEmpty(failPreview)
                    && AppServices.Thumbnails is { } failThumbs)
                {
                    failPreview = failThumbs.ExistingLargePreviewForPath(still);
                }

                var failImage = ToStillImage(failPreview);
                ImgStill.Source = failImage;
                // One hdr-fail line (S4): error returned with the decode result.
                SdrTonemapLog.TryAppendViewer(
                    still,
                    failImage is not null ? "hdr-fail+pipeline-preview" : "hdr-fail",
                    ProbeBufferLabel(gallery.CurrentProbe),
                    detail: loadError);

                gallery.SetHdrPresentResult(false, false);
                QueueFileHistogram(gallery, still, hdr: false);
                return;
            }

            // Highest pixel / header CLL wins on this still (Fit viewport then
            // 1:1 native). A new path (next/prev) drops the prior peak so a
            // dimmer HDR still is not crushed.
            _hdrMapMaxNits = GalleryPresent.StickyContentMaxNitsForStill(
                still,
                _hdrFramePath,
                frame.MaxScrgb,
                frame.MaxNits,
                gallery.CurrentProbe.MaxCllNits,
                _hdrMapMaxNits);

            _hdrFrame = frame;
            _hdrFramePath = still;
            _hdrFrameProbe = gallery.CurrentProbe;
            ApplyScaleLayout();
            QueueHistogram(gallery, frame, gallery.CurrentProbe, hdr: true);
            RequestPresent(HdrPresentCoalescer.ImmediateMs);
            if (!GalleryPresent.IsNativeDecode(frame.Width, frame.Height, frame.NativeWidth, frame.NativeHeight))
            {
                // Prefer CIE Y / MaxCLL already on the viewport frame. A
                // separate measure uses the same downscale caps — never native
                // 16384².
                if (frame.MaxNits > 0 || frame.MaxScrgb > 0)
                {
                    gallery.SetHdrStats(
                        frame.MaxNits, frame.AvgNits, frame.MinNits, frame.MaxScrgb, frame.MaxScrgbChannel);
                }
                else
                {
                    _hdrLoadCts = GalleryPresent.LiveTokenSource(_hdrLoadCts);
                    _ = MeasureStatsAsync(
                        gallery, still, gallery.CurrentProbe, viewPxW, viewPxH, epoch, _hdrLoadCts.Token);
                }
            }
        });
    }

    private async Task MeasureStatsAsync(
        GalleryViewModel gallery,
        string path,
        HdrProbe probe,
        int viewportPixelWidth,
        int viewportPixelHeight,
        int epoch,
        CancellationToken cancellation)
    {
        HdrStats? stats = null;
        try
        {
            stats = await HdrWicDecode.TryMeasureAsync(
                path, probe, viewportPixelWidth, viewportPixelHeight, cancellation);
        }
        catch (OperationCanceledException)
        {
            // Viewport/nav cancel — not an error.
            return;
        }
        catch (Exception ex)
        {
            ErrorReporter.LogOnly("GalleryStillSurface HDR measure", ex);
            stats = null;
        }

        if (epoch != _epoch || !ReferenceEquals(_gallery, gallery) || stats is null)
        {
            return;
        }

        await UiDispatch.RunAsync(() =>
        {
            if (epoch != _epoch || !ReferenceEquals(_gallery, gallery))
            {
                return;
            }

            gallery.SetHdrStats(stats.Value.MaxNits, stats.Value.AvgNits, stats.Value.MinNits, stats.Value.MaxScrgb);
        });
    }

    private HdrFrame? CachedFrame(string path, HdrProbe probe) =>
        _hdrFrame is not null
        && string.Equals(_hdrFramePath, path, StringComparison.OrdinalIgnoreCase)
        && _hdrFrameProbe.Equals(probe)
            ? _hdrFrame
            : null;

    private void PresentCached(int delayMs) => RequestPresent(delayMs);

    /// <summary>Viz mode the last present started with (UI thread only).</summary>
    private HdrVizMode _lastPresentedViz = HdrVizMode.None;

    private static HdrVizMode WantedViz(GalleryViewModel g) =>
        g.ShowHeatmap && g.CanShowHeatmap ? HdrVizMode.Heatmap : HdrVizMode.None;

    private void RequestPresent(int delayMs)
    {
        if (_gallery is not { IsImage: true } || _hdrFrame is null)
        {
            return;
        }

        if (delayMs <= HdrPresentCoalescer.ImmediateMs)
        {
            // Discrete change (new still, scale mode): a raster for the old
            // parameters is wasted work.
            _presentCts?.Cancel();
        }

        if (_presentQueue.Request(delayMs))
        {
            _ = RunPresentLoopAsync();
        }
    }

    /// <summary>
    /// One loop at a time. Rasterize / half-float runs off the UI thread in
    /// <see cref="HdrSwapchainPresenter"/>; resize / peak ticks settle first
    /// (<see cref="HdrPresentCoalescer"/>), and a result for a stale epoch,
    /// frame, or closed overlay is dropped before upload.
    /// </summary>
    private async Task RunPresentLoopAsync()
    {
        var waited = 0;
        try
        {
            while (true)
            {
                var delayMs = 0;
                var have = false;
                await UiDispatch.RunAsync(() => have = _presentQueue.TryTake(out delayMs));
                if (!have)
                {
                    return;
                }

                if (delayMs > 0)
                {
                    await Task.Delay(delayMs).ConfigureAwait(false);
                    waited += delayMs;
                    var restart = false;
                    await UiDispatch.RunAsync(() => restart = _presentQueue.ShouldRestartDebounce(waited));
                    if (restart)
                    {
                        continue;
                    }
                }

                waited = 0;
                await PresentOnceAsync();
            }
        }
        catch
        {
            await UiDispatch.RunAsync(() => _presentQueue.Abort());
        }
    }

    private async Task PresentOnceAsync()
    {
        GalleryViewModel? gallery = null;
        HdrFrame? frame = null;
        HdrSwapchainPresenter? presenter = null;
        var epoch = 0;
        var dipW = 0.0;
        var dipH = 0.0;
        var scale = 1.0;
        float? peakOverride = null;
        var mapMaxNits = 0f;
        var viz = HdrVizMode.None;
        int? vizPrimaries = null;
        ImageScaling scaling = ImageScaling.Fit;
        var contentProbe = HdrProbe.None;
        await UiDispatch.RunAsync(() =>
        {
            if (_gallery is not { IsImage: true } g || _hdrFrame is not { } f)
            {
                return;
            }

            gallery = g;
            frame = f;
            contentProbe = _hdrFrameProbe;
            scaling = g.DisplayScaling;
            // Heatmap applies once an HDR present exists; the first present of
            // a still is normal, and VizMode re-presents when it turns on.
            viz = WantedViz(g);
            _lastPresentedViz = viz;
            vizPrimaries = GalleryPresent.AnalysisPrimaries(_hdrFrameProbe);
            epoch = Volatile.Read(ref _epoch);
            _presenter ??= new HdrSwapchainPresenter(ScpHdr);
            presenter = _presenter;
            ApplyScaleLayout();
            // Collapsed panels report ActualWidth 0. Show the swapchain under the
            // SDR image so layout/composition can run, then hide the image on success.
            ScpHdr.Visibility = Visibility.Visible;
            ImgStill.Visibility = Visibility.Visible;
            (dipW, dipH) = HdrPanelDips();
            scale = XamlRoot?.RasterizationScale ?? 1.0;
            peakOverride = GalleryPeak.PresentOverrideNits;
            mapMaxNits = GalleryPresent.StickyContentMaxNitsForStill(
                _hdrFramePath,
                g.PreviewImageUri ?? g.CurrentPath,
                f.MaxScrgb,
                f.MaxNits,
                g.CurrentProbe.MaxCllNits,
                GalleryPresent.StickyContentMaxNits(
                    GalleryPresent.ContentMaxNits(g.ContentMaxScrgb, g.ContentMaxNits),
                    _hdrMapMaxNits));
            _hdrMapMaxNits = mapMaxNits;
        });

        if (gallery is null || frame is null || presenter is null)
        {
            return;
        }

        var galleryRef = gallery;
        var frameRef = frame;
        var presenterRef = presenter;
        bool StillCurrent() =>
            epoch == Volatile.Read(ref _epoch)
            && ReferenceEquals(_gallery, galleryRef)
            && ReferenceEquals(_hdrFrame, frameRef)
            && ReferenceEquals(_presenter, presenterRef);

        using var cts = new CancellationTokenSource();
        await UiDispatch.RunAsync(() => _presentCts = cts);
        // Sticky thumb ToneMap.Analyze stats per path: first Fit; 1:1 / resize reuse.
        // S5: presenter also drops sticky when DisplayIsHdr flips vs last present.
        float? stickyExposure = null;
        float? stickyWhite = null;
        await UiDispatch.RunAsync(() =>
        {
            if (!string.IsNullOrEmpty(_hdrFramePath)
                && string.Equals(_sdrTonePath, _hdrFramePath, StringComparison.OrdinalIgnoreCase)
                && _sdrToneExposure > 0
                && _sdrToneWhite > 0f
                && !GalleryPresent.StickyThumbToneInvalidated(
                    _sdrToneDisplayHdr, presenterRef.DisplayIsHdr)
                && _sdrToneDisplayHdr is not null)
            {
                stickyExposure = _sdrToneExposure;
                stickyWhite = _sdrToneWhite;
            }
        });
        HdrPresentOutcome outcome;
        try
        {
            outcome = await presenterRef.TryPresentAsync(
                frameRef, scaling, (float)scale, dipW, dipH, peakOverride, mapMaxNits, StillCurrent, cts.Token,
                contentProbe: contentProbe,
                viz: viz, vizPrimaries: vizPrimaries,
                sdrExposure: stickyExposure,
                sdrReinhardWhite: stickyWhite);
        }
        finally
        {
            await UiDispatch.RunAsync(() =>
            {
                if (ReferenceEquals(_presentCts, cts))
                {
                    _presentCts = null;
                }
            });
        }

        if (outcome == HdrPresentOutcome.Cancelled || !StillCurrent())
        {
            return;
        }

        await UiDispatch.RunAsync(() =>
        {
            if (!StillCurrent())
            {
                return;
            }

            if (outcome == HdrPresentOutcome.Presented)
            {
                // Cache thumb-match stats for SDR and hdr-display+linear (LastSdr*>0).
                if (presenterRef.LastSdrExposure > 0
                    && !string.IsNullOrEmpty(_hdrFramePath))
                {
                    _sdrTonePath = _hdrFramePath;
                    _sdrToneExposure = presenterRef.LastSdrExposure;
                    _sdrToneWhite = presenterRef.LastSdrReinhardWhite;
                    _sdrToneDisplayHdr = presenterRef.DisplayIsHdr;
                }

                ImgStill.Visibility = Visibility.Collapsed;
                galleryRef.SetHdrPresentResult(
                    true,
                    presenterRef.DisplayIsHdr,
                    presenterRef.DisplayPeakNits,
                    frameRef.MaxNits,
                    frameRef.AvgNits,
                    frameRef.MinNits,
                    frameRef.MaxScrgb,
                    frameRef.NativeWidth,
                    frameRef.NativeHeight,
                    GalleryPresent.IsNativeDecode(
                        frameRef.Width, frameRef.Height, frameRef.NativeWidth, frameRef.NativeHeight),
                    maxScrgbChannel: frameRef.MaxScrgbChannel);
                return;
            }

            HideHdr();
            galleryRef.SetHdrPresentResult(false, presenterRef.DisplayIsHdr);
        });
    }
    private (double Width, double Height) HdrPanelDips()
    {
        if (ScpHdr.ActualWidth >= 2 && ScpHdr.ActualHeight >= 2)
        {
            return (ScpHdr.ActualWidth, ScpHdr.ActualHeight);
        }

        if (!double.IsNaN(ScpHdr.Width) && !double.IsNaN(ScpHdr.Height)
            && ScpHdr.Width >= 2 && ScpHdr.Height >= 2)
        {
            return (ScpHdr.Width, ScpHdr.Height);
        }

        if (GrdStillContent.ActualWidth >= 2 && GrdStillContent.ActualHeight >= 2)
        {
            return (GrdStillContent.ActualWidth, GrdStillContent.ActualHeight);
        }

        return (ScrStill.ActualWidth, ScrStill.ActualHeight);
    }

    private void QueueHistogram(GalleryViewModel gallery, HdrFrame frame, HdrProbe probe, bool hdr)
    {
        var item = gallery.Current;
        var original = item is not null
            && string.Equals(gallery.CurrentPath, item.Path, StringComparison.OrdinalIgnoreCase);
        var onlineOnly = item is null
            || GalleryMedia.IsLiveOnlineOnly(
                item.IsOnlineOnly,
                CloudFile.IsOnlineOnly(item.Path),
                AssetItemMapper.IsApiOnly(item),
                GalleryMedia.CanShowPreview(item.Path));
        if (!GalleryHistogram.ShouldBuild(
                gallery.IsImage,
                onlineOnly,
                item is not null && AssetItemMapper.IsApiOnly(item),
                item?.IsOrphan ?? true,
                original))
        {
            Interlocked.Increment(ref _histEpoch);
            gallery.ClearHistogram();
            return;
        }

        var epoch = Interlocked.Increment(ref _histEpoch);
        var rgba = frame.ScrgbRgba;
        var width = frame.Width;
        var height = frame.Height;
        var primaries = GalleryPresent.AnalysisPrimaries(probe);
        _ = Task.Run(() =>
        {
            HdrAnalysisResult bins;
            try
            {
                // One shared pass: histogram + light stats + gamut + CIE.
                bins = HdrAnalysis.Analyze(rgba, width, height, primaries, hdr);
            }
            catch
            {
                bins = HdrAnalysisResult.Empty;
            }

            return UiDispatch.RunAsync(() =>
            {
                if (epoch != Volatile.Read(ref _histEpoch) || !ReferenceEquals(_gallery, gallery))
                {
                    return;
                }

                gallery.SetAnalysis(bins);
            });
        });
    }

    private void QueueFileHistogram(GalleryViewModel gallery, string path, bool hdr)
    {
        var item = gallery.Current;
        var original = item is not null
            && string.Equals(path, item.Path, StringComparison.OrdinalIgnoreCase);
        var onlineOnly = item is null
            || GalleryMedia.IsLiveOnlineOnly(
                item.IsOnlineOnly,
                CloudFile.IsOnlineOnly(item.Path),
                AssetItemMapper.IsApiOnly(item),
                GalleryMedia.CanShowPreview(item.Path));
        if (!GalleryHistogram.ShouldBuild(
                gallery.IsImage,
                onlineOnly,
                item is not null && AssetItemMapper.IsApiOnly(item),
                item?.IsOrphan ?? true,
                original)
            || !CloudFile.Exists(path)
            || CloudFile.IsOnlineOnly(path))
        {
            Interlocked.Increment(ref _histEpoch);
            gallery.ClearHistogram();
            return;
        }

        var epoch = Interlocked.Increment(ref _histEpoch);
        _histCts?.Cancel();
        _histCts = new CancellationTokenSource();
        var token = _histCts.Token;
        var probe = hdr ? gallery.CurrentProbe : GalleryHistogram.SdrDecodeProbe(gallery.CurrentProbe);
        var raster = XamlRoot?.RasterizationScale ?? 1.0;
        var (dipW, dipH) = HdrPanelDips();
        var viewPxW = (int)Math.Round(Math.Max(dipW, 0) * raster);
        var viewPxH = (int)Math.Round(Math.Max(dipH, 0) * raster);
        var scaling = gallery.DisplayScaling;
        _ = Task.Run(async () =>
        {
            HdrAnalysisResult bins;
            try
            {
                // R2: histogram only reads ScrgbRgba — skip Working.
                var loaded = await HdrWicDecode.TryLoadAsync(
                    path,
                    probe,
                    viewPxW,
                    viewPxH,
                    scaling,
                    token,
                    buildWorking: false).ConfigureAwait(false);
                var frame = loaded.Frame;
                bins = frame is null
                    ? HdrAnalysisResult.Empty
                    : HdrAnalysis.Analyze(
                        frame.ScrgbRgba,
                        frame.Width,
                        frame.Height,
                        GalleryPresent.AnalysisPrimaries(probe),
                        probe.CanPresentHdr);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                bins = HdrAnalysisResult.Empty;
            }

            await UiDispatch.RunAsync(() =>
            {
                if (epoch != Volatile.Read(ref _histEpoch) || !ReferenceEquals(_gallery, gallery))
                {
                    return;
                }

                gallery.SetAnalysis(bins);
            });
        }, token);
    }

    private void QueuePackedHistogram(GalleryViewModel gallery, GifFrames.Raster raster)
    {
        var item = gallery.Current;
        var original = item is not null
            && string.Equals(gallery.CurrentPath, item.Path, StringComparison.OrdinalIgnoreCase);
        var onlineOnly = item is null
            || GalleryMedia.IsLiveOnlineOnly(
                item.IsOnlineOnly,
                CloudFile.IsOnlineOnly(item.Path),
                AssetItemMapper.IsApiOnly(item),
                GalleryMedia.CanShowPreview(item.Path));
        if (!GalleryHistogram.ShouldBuild(
                gallery.IsImage,
                onlineOnly,
                item is not null && AssetItemMapper.IsApiOnly(item),
                item?.IsOrphan ?? true,
                original))
        {
            Interlocked.Increment(ref _histEpoch);
            gallery.ClearHistogram();
            return;
        }

        var epoch = Interlocked.Increment(ref _histEpoch);
        var bgra = raster.Bgra;
        var width = raster.Width;
        var height = raster.Height;
        _ = Task.Run(() =>
        {
            HdrAnalysisResult bins;
            try
            {
                bins = HdrAnalysis.AnalyzePacked8(bgra, width, height, HdrPackedFormat.Bgra8);
            }
            catch
            {
                bins = HdrAnalysisResult.Empty;
            }

            return UiDispatch.RunAsync(() =>
            {
                if (epoch != Volatile.Read(ref _histEpoch) || !ReferenceEquals(_gallery, gallery))
                {
                    return;
                }

                gallery.SetAnalysis(bins);
            });
        });
    }

    private void HideHdr()
    {
        ScpHdr.Visibility = Visibility.Collapsed;
        ImgStill.Visibility = Visibility.Visible;
    }

    private void ClearHdrCache()
    {
        _hdrFrame = null;
        _hdrFramePath = null;
        _hdrMapMaxNits = 0;
        _sdrTonePath = null;
        _sdrToneExposure = 1f;
        _sdrToneWhite = 0;
        _sdrToneDisplayHdr = null;
        _hdrFrameProbe = HdrProbe.None;
    }

    private void ApplyScaleLayout()
    {
        var scaling = CurrentDisplayScaling();
        var scrolls = GalleryScale.Scrolls(scaling, _pinchZoom);
        var bars = GalleryScale.ShowsScrollBars()
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Hidden;
        ScrStill.HorizontalScrollBarVisibility = bars;
        ScrStill.VerticalScrollBarVisibility = bars;
        ImgStill.Stretch = BindHelpers.ImageStretch(scaling);

        // When content ≤ viewport: Center (letterbox margins are outside the
        // panel — not part of pinch/pan). When zoomed/scrolling: Left/Top so
        // scroll offsets match ZoomAroundPoint / ClampPan.
        var align = scrolls ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        var valign = scrolls ? VerticalAlignment.Top : VerticalAlignment.Center;
        GrdStillContent.HorizontalAlignment = align;
        GrdStillContent.VerticalAlignment = valign;
        // Image / swapchain fill the content panel (panel IS the image rect).
        ImgStill.HorizontalAlignment = HorizontalAlignment.Stretch;
        ImgStill.VerticalAlignment = VerticalAlignment.Stretch;
        ScpHdr.HorizontalAlignment = HorizontalAlignment.Stretch;
        ScpHdr.VerticalAlignment = VerticalAlignment.Stretch;

        double width;
        double height;
        var viewW = ScrStill.ActualWidth;
        var viewH = ScrStill.ActualHeight;
        var (iw, ih) = StillPixelSize();
        if (scaling == ImageScaling.Actual)
        {
            var raster = XamlRoot?.RasterizationScale ?? 1.0;
            (width, height) = GalleryScale.ActualDipSize(iw, ih, raster);
        }
        else if (scaling == ImageScaling.Fill)
        {
            (width, height) = GalleryScale.FillCoverDipSize(iw, ih, viewW, viewH);
        }
        else
        {
            // Fit: letterboxed image content rect — NOT the full viewport.
            // Sizing ScpHdr to the viewport made HDR's opaque black backdrop
            // part of pinch/pan bounds (drift to bottom-right).
            (width, height) = GalleryScale.FitContainDipSize(iw, ih, viewW, viewH);
            if (width <= 1 || height <= 1)
            {
                width = double.NaN;
                height = double.NaN;
            }
        }

        (width, height) = GalleryScale.ApplyPinchZoom(width, height, _pinchZoom);
        // Cap layout DIPs so DXGI ResizeBuffers cannot fail at extreme zoom.
        (width, height) = TouchGestures.CapStillPanelSize(width, height);

        GrdStillContent.Width = width;
        GrdStillContent.Height = height;
        ImgStill.Width = width;
        ImgStill.Height = height;
        ScpHdr.Width = width;
        ScpHdr.Height = height;
        SyncScrollOffset(scaling, width, height, viewW, viewH);
    }

    private void SyncScrollOffset(
        ImageScaling scaling,
        double contentW,
        double contentH,
        double viewW,
        double viewH)
    {
        if (double.IsNaN(contentW) || double.IsNaN(contentH) || contentW <= 1 || contentH <= 1
            || viewW <= 1 || viewH <= 1)
        {
            ResetPinchPanIntent();
            return;
        }

        var revision = _gallery?.StillRevision ?? 0;
        var preserve = _preservePinchPan;
        var originX = _pinchOriginX;
        var originY = _pinchOriginY;
        var panX = _coalescedPanX;
        var panY = _coalescedPanY;
        // Prior content size for midpoint math (before we overwrite tracking).
        // Use actual size delta — CapStillPanelSize can diverge from zoom ratio.
        var oldContentW = _scrollContentW > 1 ? _scrollContentW : contentW;
        var oldContentH = _scrollContentH > 1 ? _scrollContentH : contentH;
        var oldOffH = ScrStill.HorizontalOffset;
        var oldOffV = ScrStill.VerticalOffset;
        ResetPinchPanIntent();

        if (!preserve
            && scaling == _scrollScaling
            && revision == _scrollRevision
            && NearlyEqual(contentW, _scrollContentW)
            && NearlyEqual(contentH, _scrollContentH)
            && NearlyEqual(viewW, _scrollViewW)
            && NearlyEqual(viewH, _scrollViewH))
        {
            return;
        }

        _scrollScaling = scaling;
        _scrollRevision = revision;
        _scrollContentW = contentW;
        _scrollContentH = contentH;
        _scrollViewW = viewW;
        _scrollViewH = viewH;

        ScrStill.UpdateLayout();
        // Adopt laid-out Actual only when it is at least the assigned size.
        // Arranged Actual can under-report (short of Height/Width) inside the
        // ScrollViewer and would shrink tall pan max before the image bottom.
        if (GrdStillContent.ActualWidth > 1 && GrdStillContent.ActualHeight > 1)
        {
            contentW = Math.Max(contentW, GrdStillContent.ActualWidth);
            contentH = Math.Max(contentH, GrdStillContent.ActualHeight);
            _scrollContentW = contentW;
            _scrollContentH = contentH;
        }

        // Clamp against the true viewport (not a larger control Actual).
        var clampW = TouchGestures.ResolvePanViewport(viewW, ScrStill.ViewportWidth);
        var clampH = TouchGestures.ResolvePanViewport(viewH, ScrStill.ViewportHeight);

        double horizontal;
        double vertical;
        if (preserve)
        {
            // originX/Y must be ScrStill viewport coords (not GrdStillContent).
            // ZoomAroundPoint keeps the pinch midpoint on-screen; DragPanContent
            // clamps so the image cannot leave the viewport.
            (horizontal, vertical) = TouchGestures.ZoomAroundPoint(
                originX,
                originY,
                oldContentW,
                oldContentH,
                oldOffH,
                oldOffV,
                contentW,
                contentH,
                clampW,
                clampH);
            if (panX != 0 || panY != 0)
            {
                (horizontal, vertical) = GalleryScale.DragPanContent(
                    horizontal,
                    vertical,
                    panX,
                    panY,
                    contentW,
                    contentH,
                    clampW,
                    clampH);
            }
        }
        else
        {
            (horizontal, vertical) = GalleryScale.InitialScrollOffset(
                scaling, contentW, contentH, clampW, clampH);
        }

        ScrStill.ChangeView(horizontal, vertical, null, true);
    }

    private void ResetView()
    {
        _panning = false;
        CancelTouchHold();
        ResetScrollTracking();
        ResetPinchTracking();
        // Fit / scale-mode / still change must clear translation too — a prior
        // pinch can leave HorizontalOffset/VerticalOffset far off-screen.
        ScrStill.ChangeView(0, 0, null, true);
    }

    private void ResetScrollTracking()
    {
        _scrollRevision = int.MinValue;
        _scrollContentW = 0;
        _scrollContentH = 0;
        _scrollViewW = 0;
        _scrollViewH = 0;
        ResetPinchPanIntent();
    }

    private void ResetPinchTracking()
    {
        _pinchZoom = 1;
        _pinchOriginX = 0;
        _pinchOriginY = 0;
        ResetPinchPanIntent();
    }

    private void ResetPinchPanIntent()
    {
        _preservePinchPan = false;
        _coalescedPanX = 0;
        _coalescedPanY = 0;
    }

    private static bool NearlyEqual(double a, double b) =>
        Math.Abs(a - b) < 0.5;

    private (int Width, int Height) StillPixelSize()
    {
        // Oriented WIC / BitmapImage size. Catalog Width/Height are unoriented
        // headers — 90°/270° EXIF would get the wrong 1:1 aspect.
        if (_hdrFrame is { NativeWidth: > 0, NativeHeight: > 0 } frame)
        {
            return (frame.NativeWidth, frame.NativeHeight);
        }

        if (ImgStill.Source is BitmapImage bmp && bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
        {
            return (bmp.PixelWidth, bmp.PixelHeight);
        }

        if (ImgStill.Source is WriteableBitmap writeable
            && writeable.PixelWidth > 0
            && writeable.PixelHeight > 0)
        {
            return (writeable.PixelWidth, writeable.PixelHeight);
        }

        return (0, 0);
    }

    /// <summary>
    /// Manipulation events are on <see cref="GrdStillContent"/> so
    /// <c>e.Position</c> is content-local. Pinch midpoint math needs
    /// <see cref="ScrStill"/> viewport coordinates — content-space origins
    /// throw the still off-screen (HDR black bg looks like a black image).
    /// </summary>
    private Windows.Foundation.Point ToViewport(Windows.Foundation.Point contentLocal)
    {
        try
        {
            return GrdStillContent.TransformToVisual(ScrStill).TransformPoint(contentLocal);
        }
        catch
        {
            return new Windows.Foundation.Point(
                contentLocal.X - ScrStill.HorizontalOffset,
                contentLocal.Y - ScrStill.VerticalOffset);
        }
    }

    private void ApplyPinchFrame(
        double originViewportX,
        double originViewportY,
        double nextZoom,
        double panX,
        double panY)
    {
        _preservePinchPan = true;
        _pinchOriginX = originViewportX;
        _pinchOriginY = originViewportY;
        _pinchZoom = nextZoom;
        _coalescedPanX = panX;
        _coalescedPanY = panY;
        ApplyScaleLayout();
        // Re-present HDR after layout so the swapchain matches the new size.
        if (_hdrFrame is not null)
        {
            PresentCached(HdrPresentCoalescer.SettleMs);
        }
    }

    private void ImgStill_ImageOpened(object sender, RoutedEventArgs e)
    {
        // Fit also needs a re-layout once pixels are known — FitContain with
        // 0×0 still size falls back to the viewport (SDR letterbox / wrong pan).
        if (CurrentDisplayScaling() is ImageScaling.Fit or ImageScaling.Actual or ImageScaling.Fill)
        {
            ApplyScaleLayout();
        }

        // S6: apply zoom restore after layout knows pixel size.
        ApplyPendingZoomRestore();

        // A file the scan flagged undecodable shows no resolution until the gallery
        // itself decodes the original (not a thumb / large-preview substitute).
        if (_gallery is { Current.Undecodable: true } gallery
            && ImgStill.Source is BitmapImage { PixelWidth: > 0, PixelHeight: > 0 } bmp
            && bmp.UriSource is { IsFile: true } uri
            && GalleryPresent.IsOriginalStill(uri.LocalPath, gallery.CurrentPath))
        {
            gallery.SetDecodedPixelSize(bmp.PixelWidth, bmp.PixelHeight);
        }
    }

    private void ScrStill_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var type = e.Pointer.PointerDeviceType;
        _lastPointerDevice = type;
        var point = e.GetCurrentPoint(ScrStill);
        if (type == PointerDeviceType.Touch && e.Pointer.IsInContact)
        {
            // Two fingers resting: cancel long-press; do not start a second hold.
            if (_holdCts is not null && _holdPointerId != point.PointerId)
            {
                CancelTouchHold();
            }
            else
            {
                BeginTouchHold(point.PointerId, point.Position.X, point.Position.Y);
            }
        }

        CancelScrollViewerDirectManipulation();
        if (!GalleryScale.UsesPointerCapturePan(
                GalleryScale.Scrolls(CurrentDisplayScaling(), _pinchZoom),
                type == PointerDeviceType.Mouse,
                point.Properties.IsLeftButtonPressed))
        {
            return;
        }

        _panning = true;
        _panLastX = point.Position.X;
        _panLastY = point.Position.Y;
        ScrStill.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ScrStill_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ScrStill);
        if (_holdCts is not null && point.PointerId == _holdPointerId)
        {
            var dx = point.Position.X - _holdStartX;
            var dy = point.Position.Y - _holdStartY;
            if (TouchGestures.ExceedsSlop(dx, dy))
            {
                CancelTouchHold();
            }
        }

        if (!_panning)
        {
            return;
        }

        var type = e.Pointer.PointerDeviceType;
        if (type == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)
        {
            EndPan(e.Pointer);
            return;
        }

        // Assigned panel size wins over short Actual/Extent (tall bottom reach).
        var contentW = PanContentWidth();
        var contentH = PanContentHeight();
        var (horizontal, vertical) = GalleryScale.DragPanContent(
            ScrStill.HorizontalOffset,
            ScrStill.VerticalOffset,
            point.Position.X - _panLastX,
            point.Position.Y - _panLastY,
            contentW,
            contentH,
            PanViewportWidth(),
            PanViewportHeight());
        _panLastX = point.Position.X;
        _panLastY = point.Position.Y;
        ScrStill.ChangeView(horizontal, vertical, null, true);
        e.Handled = true;
    }

    private void ScrStill_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(ScrStill).PointerId == _holdPointerId)
        {
            CancelTouchHold();
        }

        EndPan(e.Pointer);
    }

    private void ScrStill_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        CancelTouchHold();
        _panning = false;
    }

    private void EndPan(Pointer pointer)
    {
        if (!_panning)
        {
            return;
        }

        _panning = false;
        ScrStill.ReleasePointerCapture(pointer);
    }

    private double PanContentWidth() =>
        TouchGestures.ResolvePanContentExtent(
            GrdStillContent.Width,
            GrdStillContent.ActualWidth,
            ScrStill.ExtentWidth,
            _scrollContentW);

    private double PanContentHeight() =>
        TouchGestures.ResolvePanContentExtent(
            GrdStillContent.Height,
            GrdStillContent.ActualHeight,
            ScrStill.ExtentHeight,
            _scrollContentH);

    private double PanViewportWidth() =>
        TouchGestures.ResolvePanViewport(ScrStill.ActualWidth, ScrStill.ViewportWidth);

    private double PanViewportHeight() =>
        TouchGestures.ResolvePanViewport(ScrStill.ActualHeight, ScrStill.ViewportHeight);

    private void BeginTouchHold(uint pointerId, double x, double y)
    {
        if (!ViewerSettings.TouchGesturesEnabled)
        {
            return;
        }

        CancelTouchHold();
        _holdPointerId = pointerId;
        _holdStartX = x;
        _holdStartY = y;
        var cts = new CancellationTokenSource();
        _holdCts = cts;
        var token = cts.Token;
        _ = CompleteTouchHoldAsync(token);
    }

    private async Task CompleteTouchHoldAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TouchGestures.LongPressMs, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        await UiDispatch.RunAsync(() =>
        {
            if (_holdCts is null || token.IsCancellationRequested)
            {
                return;
            }

            CancelTouchHold();
            LongPressCommitted?.Invoke();
        });
    }

    private void CancelTouchHold()
    {
        _holdCts?.Cancel();
        _holdCts?.Dispose();
        _holdCts = null;
        _holdPointerId = 0;
    }

    private void CancelScrollViewerDirectManipulation()
    {
        // Learn: pointer events inside a ScrollViewer are swallowed by
        // DirectManipulation unless the child cancels it.
        GrdStillContent.CancelDirectManipulations();
        ImgStill.CancelDirectManipulations();
        ScpHdr.CancelDirectManipulations();
    }

    private void GrdStillContent_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        CancelScrollViewerDirectManipulation();
        var isTouch = e.PointerDeviceType == PointerDeviceType.Touch;
        var isPen = e.PointerDeviceType == PointerDeviceType.Pen;
        var isMouse = e.PointerDeviceType == PointerDeviceType.Mouse;
        _lastPointerDevice = e.PointerDeviceType;
        if (!GalleryScale.HandlesManipulationDelta(
                e.IsInertial, isTouch, isPen, isMouse, leftButton: _panning))
        {
            return;
        }

        // Spec: PointerDeviceType==Touch only for these new gesture paths.
        // Pen / trackpad keep the prior Manipulation pan/pinch behavior.
        var scaling = CurrentDisplayScaling();
        var scaleDelta = e.Delta.Scale;
        var baseLong = PinchBaseLongEdgeDip();
        var nextZoom = isTouch
            ? TouchGestures.ClampPinchSoft(
                _pinchZoom * (scaleDelta > 0 ? scaleDelta : 1.0),
                FitToActualRatio(),
                baseLong)
            : GalleryScale.PinchZoom(_pinchZoom, scaleDelta);
        if (isTouch)
        {
            nextZoom = TouchGestures.ClampPinchDuringGesture(nextZoom);
        }

        var didPinch = scaleDelta > 0 && Math.Abs(nextZoom - _pinchZoom) > 0.0001;
        var deltaX = e.Delta.Translation.X;
        var deltaY = e.Delta.Translation.Y;
        var hasPan = deltaX != 0 || deltaY != 0;

        if (didPinch)
        {
            if (isTouch)
            {
                _afterPinchAwaitingLift = true;
                CancelTouchHold();
                // B1: pinch mid-swipe cancels the pager — snap back, no stale classify.
                if (_reviewSwipeActive || _pagerHandoff || _pagerSettling || _pagerAwaitingContent)
                {
                    CancelPagerForPinch();
                }
            }

            var mid = ToViewport(e.Position);
            ApplyPinchFrame(mid.X, mid.Y, nextZoom, deltaX, deltaY);
            e.Handled = true;
            return;
        }

        if (!GalleryScale.Scrolls(scaling, _pinchZoom))
        {
            // Not zoomed: visual pager / Review classify (touch only).
            if (isTouch
                && hasPan
                && ViewerSettings.TouchGesturesEnabled
                && TouchGestures.AllowsSwipe(
                    zoomed: false,
                    fingerCount: 1,
                    afterPinchAwaitingLift: _afterPinchAwaitingLift))
            {
                CancelTouchHold();
                // B6 / should-fix: interrupt settle or stuck awaiting; continue from offset.
                if (_pagerSettling || _pagerAwaitingContent)
                {
                    InterruptPagerSettleForDrag();
                }

                _reviewSwipeActive = true;
                _reviewSwipeDx += deltaX;
                _reviewSwipeDy += deltaY;
                // ManipulationVelocities.Linear is already DIPs per millisecond.
                _reviewSwipeVx = e.Velocities.Linear.X;
                _reviewSwipeVy = e.Velocities.Linear.Y;
                ApplyPagerDragVisual(_reviewSwipeDx, _reviewSwipeDy);
                e.Handled = true;
            }

            return;
        }

        if (!hasPan)
        {
            return;
        }

        // Touch pan with Photos-style edge handoff into the pager (browse only).
        if (isTouch)
        {
            CancelTouchHold();
            if (_pagerSettling || _pagerAwaitingContent)
            {
                InterruptPagerSettleForDrag();
            }

            if (ViewerSettings.TouchGesturesEnabled
                && TryPagerHandoffFromZoom(deltaX, deltaY, e))
            {
                e.Handled = true;
                return;
            }

            var (horizontal, vertical) = GalleryScale.DragPanContent(
                ScrStill.HorizontalOffset,
                ScrStill.VerticalOffset,
                deltaX,
                deltaY,
                PanContentWidth(),
                PanContentHeight(),
                PanViewportWidth(),
                PanViewportHeight());
            ScrStill.ChangeView(horizontal, vertical, null, true);
            e.Handled = true;
            return;
        }

        var (h, v) = GalleryScale.DragPanContent(
            ScrStill.HorizontalOffset,
            ScrStill.VerticalOffset,
            deltaX,
            deltaY,
            PanContentWidth(),
            PanContentHeight(),
            PanViewportWidth(),
            PanViewportHeight());
        ScrStill.ChangeView(h, v, null, true);
        e.Handled = true;
    }

    private void GrdStillContent_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        if (e.PointerDeviceType != PointerDeviceType.Touch)
        {
            return;
        }

        if (_reviewSwipeActive)
        {
            CompletePagerGesture();
            _afterPinchAwaitingLift = false;
            return;
        }

        // Snap soft under-zoom back to Fit on release.
        var mid = ToViewport(e.Position);
        var snapped = TouchGestures.SnapPinchOnRelease(_pinchZoom);
        if (Math.Abs(snapped - _pinchZoom) > 0.0001)
        {
            ApplyPinchFrame(mid.X, mid.Y, snapped, 0, 0);
        }

        var hard = TouchGestures.ClampPinchHard(
            _pinchZoom, FitToActualRatio(), PinchBaseLongEdgeDip());
        if (Math.Abs(hard - _pinchZoom) > 0.0001)
        {
            ApplyPinchFrame(mid.X, mid.Y, hard, 0, 0);
        }

        // Fit at pinch 1: clear translation. Actual/Fill keep pan (incl. 1:1).
        var scaling = CurrentDisplayScaling();
        if (GalleryScale.ClearsScrollOnPinchRelease(scaling, _pinchZoom))
        {
            ScrStill.ChangeView(0, 0, null, true);
        }

        _afterPinchAwaitingLift = false;
    }

    private void GrdStillContent_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // Touch only — mouse / pen double-tap stays unchanged.
        if (!TouchGestures.IsTouchOnly(_lastPointerDevice == PointerDeviceType.Touch)
            || !ViewerSettings.TouchGesturesEnabled
            || _gallery is null)
        {
            return;
        }

        var tap = e.GetPosition(ScrStill);
        var viewW = ScrStill.ActualWidth;
        var viewH = ScrStill.ActualHeight;
        if (viewW <= 1 || viewH <= 1)
        {
            return;
        }

        var scaling = CurrentDisplayScaling();
        var currentlyFit = scaling == ImageScaling.Fit && _pinchZoom <= 1.0001;
        var step = TouchGestures.NextDoubleTapStep(currentlyFit);
        if (step == TouchGestures.DoubleTapZoomStep.Fit)
        {
            ResetPinchTracking();
            _gallery.Scaling = ImageScaling.Fit;
            ApplyScaleLayout();
            // Fit must reset translation too (pinch can leave offsets off-screen).
            ScrStill.ChangeView(0, 0, null, true);
            e.Handled = true;
            return;
        }

        var actualAsFit = FitToActualRatio();
        var targetFit = TouchGestures.DoubleTapZoomFitFactor(actualAsFit);
        if (Math.Abs(targetFit - actualAsFit) < 0.05 && actualAsFit > TouchGestures.DoubleTapActualMinFit)
        {
            // Jump to 1:1 at the tap point (from Fit letterbox content rect).
            _pinchZoom = 1;
            _gallery.Scaling = ImageScaling.Actual;
            ApplyScaleLayout();
            var (iw, ih) = StillPixelSize();
            var raster = XamlRoot?.RasterizationScale ?? 1.0;
            var (cw, ch) = GalleryScale.ActualDipSize(iw, ih, raster);
            var (fitW, fitH) = GalleryScale.FitContainDipSize(iw, ih, viewW, viewH);
            if (!double.IsNaN(cw) && !double.IsNaN(ch)
                && !double.IsNaN(fitW) && !double.IsNaN(fitH))
            {
                var (h, v) = TouchGestures.ZoomAroundPoint(
                    tap.X, tap.Y,
                    fitW, fitH, 0, 0,
                    cw, ch, viewW, viewH);
                ScrStill.ChangeView(h, v, null, true);
            }

            e.Handled = true;
            return;
        }

        // 2.5× Fit (or Actual-as-Fit) while staying in Fit mode + pinch.
        _gallery.Scaling = ImageScaling.Fit;
        ApplyPinchFrame(tap.X, tap.Y, targetFit, 0, 0);
        e.Handled = true;
    }

    /// <summary>Actual DIP size ÷ Fit DIP size (how many Fit-units = 1:1).</summary>
    private double FitToActualRatio()
    {
        var viewW = ScrStill.ActualWidth;
        var viewH = ScrStill.ActualHeight;
        if (viewW <= 1 || viewH <= 1)
        {
            return 1;
        }

        var (iw, ih) = StillPixelSize();
        if (iw <= 0 || ih <= 0)
        {
            return 1;
        }

        var raster = XamlRoot?.RasterizationScale ?? 1.0;
        var (aw, ah) = GalleryScale.ActualDipSize(iw, ih, raster);
        if (double.IsNaN(aw) || double.IsNaN(ah) || aw <= 0 || ah <= 0)
        {
            return 1;
        }

        // Fit letterbox: uniform scale into the viewport.
        var fitScale = Math.Min(viewW / aw, viewH / ah);
        var fitW = aw * fitScale;
        return fitW > 0 ? aw / fitW : 1;
    }

    /// <summary>
    /// Pre-pinch content long edge (DIPs) for panel-size zoom caps.
    /// Fit uses the letterboxed image rect; Actual/Fill use their base size.
    /// </summary>
    private double PinchBaseLongEdgeDip()
    {
        var viewW = ScrStill.ActualWidth;
        var viewH = ScrStill.ActualHeight;
        var scaling = CurrentDisplayScaling();
        var (iw, ih) = StillPixelSize();
        if (scaling == ImageScaling.Actual)
        {
            var raster = XamlRoot?.RasterizationScale ?? 1.0;
            var (aw, ah) = GalleryScale.ActualDipSize(iw, ih, raster);
            if (!double.IsNaN(aw) && !double.IsNaN(ah) && aw > 0 && ah > 0)
            {
                return Math.Max(aw, ah);
            }
        }
        else if (scaling == ImageScaling.Fill && viewW > 1 && viewH > 1)
        {
            var (fw, fh) = GalleryScale.FillCoverDipSize(iw, ih, viewW, viewH);
            if (!double.IsNaN(fw) && !double.IsNaN(fh) && fw > 0 && fh > 0)
            {
                return Math.Max(fw, fh);
            }
        }
        else if (viewW > 1 && viewH > 1)
        {
            var (cw, ch) = GalleryScale.FitContainDipSize(iw, ih, viewW, viewH);
            if (!double.IsNaN(cw) && !double.IsNaN(ch) && cw > 0 && ch > 0)
            {
                return Math.Max(cw, ch);
            }
        }

        return Math.Max(viewW, viewH);
    }

    private void ScrStill_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ScrStill);
        var delta = point.Properties.MouseWheelDelta;
        CancelScrollViewerDirectManipulation();
        var controlDown = e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control);
        var scaling = CurrentDisplayScaling();
        var scrolls = GalleryScale.Scrolls(scaling, _pinchZoom);

        if (GalleryScale.UsesWheelPinch(controlDown, delta))
        {
            var nextZoom = GalleryScale.PinchZoom(_pinchZoom, GalleryScale.WheelPinchFactor(delta));
            if (Math.Abs(nextZoom - _pinchZoom) > 0.0001)
            {
                ApplyPinchFrame(point.Position.X, point.Position.Y, nextZoom, 0, 0);
            }

            e.Handled = true;
            return;
        }

        if (!GalleryScale.UsesWheelPan(scrolls, controlDown, delta))
        {
            // Review mode: swallow non-pan wheel so it cannot browse next/prev.
            if (IsReviewMode)
            {
                e.Handled = true;
            }

            return;
        }

        var pan = GalleryScale.WheelToPanDelta(delta);
        var horizontalWheel = point.Properties.IsHorizontalMouseWheel;
        var (horizontal, vertical) = GalleryScale.DragPanContent(
            ScrStill.HorizontalOffset,
            ScrStill.VerticalOffset,
            horizontalWheel ? pan : 0,
            horizontalWheel ? 0 : pan,
            PanContentWidth(),
            PanContentHeight(),
            PanViewportWidth(),
            PanViewportHeight());
        ScrStill.ChangeView(horizontal, vertical, null, true);
        e.Handled = true;
    }

    private static BitmapImage? ToStillImage(string? path, bool animateGif = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            if (GalleryMedia.IsRemoteUri(path))
            {
                return new BitmapImage { UriSource = new Uri(path, UriKind.Absolute) };
            }

            if (!CloudFile.Exists(path) || CloudFile.IsOnlineOnly(path))
            {
                return null;
            }

            // EXR / Radiance / TGA / Clip / PSD / JXL / non-image: show Palace's
            // decoded large preview; BitmapImage would be blank or need a codec.
            var shown = AppServices.Thumbnails is { } thumbs
                ? thumbs.DisplayPathFor(path, out _)
                : path;
            if (!string.Equals(shown, path, StringComparison.OrdinalIgnoreCase))
            {
                return shown is null ? null : new BitmapImage { UriSource = new Uri(shown, UriKind.Absolute) };
            }

            return new BitmapImage
            {
                UriSource = new Uri(path, UriKind.Absolute),
                AutoPlay = animateGif || !PathSafe.GifExt.Contains(PathSafe.Extension(path))
            };
        }
        catch
        {
            return null;
        }
    }

    private static void LogSdrStillSurface(
        string? sourcePath,
        string? requestedDisplay,
        string? forcedDecoder,
        HdrProbe probe)
    {
        if (!SdrTonemapLog.IsEnabled)
        {
            return;
        }

        string decoder;
        if (forcedDecoder is not null
            && (forcedDecoder is "mosaic-thumb" or "hdr-fail")
            && requestedDisplay is not null
            && PathSafe.IsJxl(sourcePath)
            && string.Equals(requestedDisplay, sourcePath, StringComparison.OrdinalIgnoreCase))
        {
            // Thumb missing — DisplayPathFor will refuse WIC original.
            decoder = forcedDecoder + "+await-preview";
        }
        else if (forcedDecoder is "mosaic-thumb")
        {
            decoder = "mosaic-thumb";
        }
        else if (AppServices.Thumbnails is { } thumbs)
        {
            _ = thumbs.DisplayPathFor(requestedDisplay ?? sourcePath, out decoder);
            if (forcedDecoder is "hdr-fail")
            {
                decoder = "hdr-fail+" + decoder;
            }
        }
        else
        {
            decoder = forcedDecoder ?? "bitmap-original";
        }

        SdrTonemapLog.TryAppendViewer(sourcePath, decoder, ProbeBufferLabel(probe));
    }

    private static string ProbeBufferLabel(HdrProbe probe) =>
        probe.Kind switch
        {
            HdrKind.HdrJxl when probe.IsHlg => "hlg",
            HdrKind.HdrJxl when probe.IsPq => "pq",
            HdrKind.HdrJxl => "hdr-jxl",
            HdrKind.WideGamutJxl => "wide-jxl",
            _ when probe.IsPq => "pq",
            _ when probe.IsHlg => "hlg",
            _ => "n/a"
        };

    private async Task ShowGifScrubAsync(GalleryViewModel gallery)
    {
        var path = gallery.Current?.Path;
        if (string.IsNullOrEmpty(path)
            || !CloudFile.Exists(path)
            || CloudFile.IsOnlineOnly(path)
            || !ScanContent.MayReadOriginal(path))
        {
            ClearGifCache();
            ImgStill.Source = ToStillImage(gallery.PreviewImageUri ?? gallery.CurrentPath);
            gallery.ClearHistogram();
            return;
        }

        var cacheReady = GifFrames.CacheMatchesPath(_gifCachePath, path)
            && _gifRasters is { Count: > 0 };
        if (cacheReady)
        {
            if (GifFrames.ShouldCancelStaleGifLoad(true))
            {
                _gifLoadCts?.Cancel();
                _gifLoadingPath = null;
            }

            ApplyCachedGifFrame(gallery);
            gallery.NotifyGifCompositeReady();
            if (GifFrames.PickCached(_gifRasters!, GifFrames.ClampIndex((int)Math.Round(gallery.GifFrameIndex), _gifRasters!.Count)) is { } cached)
            {
                QueuePackedHistogram(gallery, cached);
            }

            return;
        }

        var loadInFlight = string.Equals(_gifLoadingPath, path, StringComparison.OrdinalIgnoreCase)
            && _gifLoadCts is { IsCancellationRequested: false };
        if (!GifFrames.ShouldStartGifCompositeLoad(cacheReady, loadInFlight))
        {
            return;
        }

        _gifLoadCts?.Cancel();
        var load = new CancellationTokenSource();
        _gifLoadCts = load;
        _gifLoadingPath = path;
        ImgStill.Source = ToStillImage(path);

        IReadOnlyList<GifFrames.Raster>? frames = null;
        try
        {
            frames = await Task.Run(() => GifFrames.TryRenderAll(path, load.Token), load.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            frames = null;
        }

        if (load.IsCancellationRequested
            || !ReferenceEquals(_gallery, gallery)
            || !string.Equals(_gifLoadingPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await UiDispatch.RunAsync(() =>
        {
            if (load.IsCancellationRequested
                || !ReferenceEquals(_gallery, gallery)
                || !string.Equals(_gifLoadingPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _gifLoadingPath = null;
            if (frames is { Count: > 0 })
            {
                _gifCachePath = path;
                _gifRasters = frames;
                _gifBitmaps = new WriteableBitmap?[frames.Count];
                ApplyCachedGifFrame(gallery);
                gallery.NotifyGifCompositeReady();
                if (GifFrames.PickCached(frames, GifFrames.ClampIndex((int)Math.Round(gallery.GifFrameIndex), frames.Count)) is { } loaded)
                {
                    QueuePackedHistogram(gallery, loaded);
                }

                return;
            }

            ClearGifCache();
            var abandon = GifFrames.ShouldAbandonScrubOnFailedComposite(
                gallery.CanScrubGif, hasCompositeFrames: false);
            if (abandon)
            {
                gallery.AbandonGifScrub();
            }

            ImgStill.Source = ToStillImage(path, animateGif: GifFrames.ShouldAnimateGifFallback(abandon));
            ApplyScaleLayout();
            QueueFileHistogram(gallery, path, hdr: false);
        });
    }

    private void ApplyCachedGifFrame(GalleryViewModel gallery)
    {
        var frames = _gifRasters;
        if (frames is null || frames.Count == 0)
        {
            return;
        }

        var index = GifFrames.ClampIndex((int)Math.Round(gallery.GifFrameIndex), frames.Count);
        WriteableBitmap? bmp = null;
        if (_gifBitmaps is { Length: > 0 } bitmaps && (uint)index < (uint)bitmaps.Length)
        {
            bmp = bitmaps[index];
            if (bmp is null && GifFrames.PickCached(frames, index) is { } raster)
            {
                bmp = ToWriteable(raster);
                bitmaps[index] = bmp;
            }
        }
        else if (GifFrames.PickCached(frames, index) is { } raster)
        {
            bmp = ToWriteable(raster);
        }

        ImgStill.Source = (ImageSource?)bmp ?? ToStillImage(gallery.Current?.Path);
        ApplyScaleLayout();
    }

    private void ClearGifCache()
    {
        _gifLoadCts?.Cancel();
        _gifCachePath = null;
        _gifLoadingPath = null;
        _gifRasters = null;
        _gifBitmaps = null;
    }

    private static WriteableBitmap? ToWriteable(GifFrames.Raster raster)
    {
        try
        {
            var bmp = new WriteableBitmap(raster.Width, raster.Height);
            using var pixels = bmp.PixelBuffer.AsStream();
            pixels.Write(raster.Bgra, 0, raster.Bgra.Length);
            bmp.Invalidate();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    // --- Visual pager (docs/specs/swipe-nav.md) ---

    private bool SystemAnimationsEnabled()
    {
        if (_cachedAnimationsEnabled is { } cached)
        {
            return cached;
        }

        try
        {
            HookUiSettings();
            _cachedAnimationsEnabled = _uiSettings!.AnimationsEnabled;
            return _cachedAnimationsEnabled.Value;
        }
        catch
        {
            return true;
        }
    }

    private void HookUiSettings()
    {
        if (_uiSettings is not null)
        {
            return;
        }

        _uiSettings = new UISettings();
        _uiSettings.AnimationsEnabledChanged += UiSettings_AnimationsEnabledChanged;
        _cachedAnimationsEnabled = _uiSettings.AnimationsEnabled;
    }

    private void UnhookUiSettings()
    {
        if (_uiSettings is null)
        {
            return;
        }

        _uiSettings.AnimationsEnabledChanged -= UiSettings_AnimationsEnabledChanged;
        _uiSettings = null;
        _cachedAnimationsEnabled = null;
    }

    private void UiSettings_AnimationsEnabledChanged(UISettings sender, object args) =>
        _cachedAnimationsEnabled = sender.AnimationsEnabled;

    private void CancelPagerForPinch()
    {
        // B1: clear swipe state and snap back — CompletePagerGesture must not
        // classify the stale dx/dy after lift.
        _reviewSwipeActive = false;
        _reviewSwipeDx = 0;
        _reviewSwipeDy = 0;
        _reviewSwipeVx = 0;
        _reviewSwipeVy = 0;
        _pagerHandoff = false;
        _pagerPanBeforeHandoff = 0;
        ClearAwaitingContent();
        _pendingAdvanceSlide = false;
        StopPagerAnimations(invokeOnDone: false);
        var duration = SwipeNav.SettleDurationMs(SystemAnimationsEnabled(), committing: false);
        AnimatePagerSettle(0, duration, overshoot: 0, onDone: ResetPagerVisuals);
    }

    private void InterruptPagerSettleForDrag()
    {
        // B6 / N2: stop Composition settle, resume from live X.
        var x = ReadPagerX();
        StopPagerAnimations(invokeOnDone: false);
        _pagerSettling = false;
        ClearAwaitingContent();
        _pagerX = x;
        _reviewSwipeDx = x;
        _reviewSwipeDy = 0;
        _pagerHandoff = false;
        _pagerPanBeforeHandoff = 0;
    }

    private void StopPagerAnimations(bool invokeOnDone)
    {
        // N2: bump generation so stale batch.Completed returns early.
        _pagerSettleGeneration++;
        var done = _pagerOnDone;
        _pagerOnDone = null;
        try
        {
            if (_pagerProps is not null)
            {
                _pagerProps.StopAnimation("X");
                if (_pagerProps.TryGetScalar("X", out var live)
                    == CompositionGetValueStatus.Succeeded)
                {
                    _pagerX = live;
                }
            }
        }
        catch
        {
            // Property set may already be stopped / torn down.
        }

        try
        {
            _pagerStoryboard?.Stop();
        }
        catch
        {
            // Storyboard may already be stopped.
        }

        _pagerStoryboard = null;
        _pagerSettling = false;
        if (invokeOnDone)
        {
            done?.Invoke();
        }
    }

    private double ReadPagerX()
    {
        if (_pagerProps is not null)
        {
            try
            {
                if (_pagerProps.TryGetScalar("X", out var live)
                    == CompositionGetValueStatus.Succeeded)
                {
                    _pagerX = live;
                    return live;
                }
            }
            catch
            {
                // Fall through to cached / XAML.
            }
        }

        return _pagerExpressionsReady ? _pagerX : XfCurrent.X;
    }

    private void ClearAwaitingContent()
    {
        _pagerAwaitingContent = false;
        _awaitingEpoch++;
    }

    private void BeginAwaitingContent()
    {
        _pagerAwaitingContent = true;
        var epoch = ++_awaitingEpoch;
        _ = SnapBackIfAwaitingStuckAsync(epoch);
    }

    private async System.Threading.Tasks.Task SnapBackIfAwaitingStuckAsync(int epoch)
    {
        try
        {
            await System.Threading.Tasks.Task.Delay(SwipeNav.AwaitingContentTimeoutMs);
        }
        catch
        {
            return;
        }

        await UiDispatch.RunAsync(() =>
        {
            if (epoch != _awaitingEpoch || !_pagerAwaitingContent)
            {
                return;
            }

            // Should-fix 1: stuck awaiting — snap back.
            ClearAwaitingContent();
            _pendingAdvanceSlide = false;
            StopPagerAnimations(invokeOnDone: false);
            ResetPagerVisuals();
            _pagerHandoff = false;
            _pagerPanBeforeHandoff = 0;
        });
    }

    private void FinishAwaitingContentSettle()
    {
        ClearAwaitingContent();
        var width = Math.Max(1, ScrStill.ActualWidth);
        var releaseX = ReadPagerX();
        var slot = _pendingAdvanceSlot;
        // N3: hide neighbors before refresh so n+2 does not flash.
        HideNeighborSurfaces();
        if (slot != 0)
        {
            var startX = SwipeNav.PostCommitStartX(releaseX, slot, width);
            SetPagerCompositionX(startX, width, 0, 0);
        }

        RefreshNeighbors();
        var duration = SwipeNav.SettleDurationMs(SystemAnimationsEnabled(), committing: true);
        AnimatePagerSettle(0, duration, overshoot: 0, onDone: () =>
        {
            ResetPagerVisuals();
            _pagerHandoff = false;
            _pagerPanBeforeHandoff = 0;
            _pendingAdvanceSlot = 0;
        });
    }

    private void HideNeighborSurfaces()
    {
        ImgNeighborPrev.Opacity = 0;
        ImgNeighborNext.Opacity = 0;
    }

    private bool TryPagerHandoffFromZoom(
        double deltaX,
        double deltaY,
        ManipulationDeltaRoutedEventArgs e)
    {
        // B2: Review never does zoom-edge handoff — pan only while zoomed.
        if (IsReviewMode)
        {
            return false;
        }

        // Already paging from a prior handoff in this gesture.
        if (_pagerHandoff || _reviewSwipeActive)
        {
            _reviewSwipeActive = true;
            _reviewSwipeDx += deltaX;
            _reviewSwipeDy += deltaY;
            _reviewSwipeVx = e.Velocities.Linear.X;
            _reviewSwipeVy = e.Velocities.Linear.Y;
            ApplyPagerDragVisual(
                SwipeNav.CommitTravel(_reviewSwipeDx, _pagerPanBeforeHandoff),
                _reviewSwipeDy);
            return true;
        }

        var maxH = TouchGestures.MaxScrollOffset(PanContentWidth(), PanViewportWidth());
        var atLeft = ScrStill.HorizontalOffset <= 0.5;
        var atRight = maxH <= 0.5 || ScrStill.HorizontalOffset >= maxH - 0.5;
        var outwardLeft = atLeft && deltaX > 0;
        var outwardRight = atRight && deltaX < 0;
        if (!outwardLeft && !outwardRight)
        {
            return false;
        }

        if (!TouchGestures.AllowsSwipeHandoff(
                zoomed: true,
                pinnedAtStartEdge: true,
                outwardDeltaPx: Math.Abs(deltaX)))
        {
            // Still inside the 24 px handoff slop — pan clamps at the edge.
            var (horizontal, vertical) = GalleryScale.DragPanContent(
                ScrStill.HorizontalOffset,
                ScrStill.VerticalOffset,
                deltaX,
                deltaY,
                PanContentWidth(),
                PanContentHeight(),
                PanViewportWidth(),
                PanViewportHeight());
            ScrStill.ChangeView(horizontal, vertical, null, true);
            return true;
        }

        _pagerHandoff = true;
        _reviewSwipeActive = true;
        _pagerPanBeforeHandoff = _reviewSwipeDx;
        _reviewSwipeDx += deltaX;
        _reviewSwipeDy += deltaY;
        _reviewSwipeVx = e.Velocities.Linear.X;
        _reviewSwipeVy = e.Velocities.Linear.Y;
        ApplyPagerDragVisual(
            SwipeNav.CommitTravel(_reviewSwipeDx, _pagerPanBeforeHandoff),
            _reviewSwipeDy);
        return true;
    }

    private void EnsurePagerExpressions()
    {
        if (_pagerExpressionsReady || XamlRoot is null)
        {
            return;
        }

        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(ScrStill, true);
            ElementCompositionPreview.SetIsTranslationEnabled(ImgNeighborPrev, true);
            ElementCompositionPreview.SetIsTranslationEnabled(ImgNeighborNext, true);
            ElementCompositionPreview.SetIsTranslationEnabled(BrdEdgeGlowLeft, true);
            ElementCompositionPreview.SetIsTranslationEnabled(BrdEdgeGlowRight, true);

            var current = ElementCompositionPreview.GetElementVisual(ScrStill);
            var prev = ElementCompositionPreview.GetElementVisual(ImgNeighborPrev);
            var next = ElementCompositionPreview.GetElementVisual(ImgNeighborNext);
            var leftGlow = ElementCompositionPreview.GetElementVisual(BrdEdgeGlowLeft);
            var rightGlow = ElementCompositionPreview.GetElementVisual(BrdEdgeGlowRight);
            var c = current.Compositor;
            _pagerProps = c.CreatePropertySet();
            _pagerProps.InsertScalar("X", 0f);
            _pagerProps.InsertScalar(
                "Stride",
                (float)SwipeNav.PageStride(Math.Max(1, ScrStill.ActualWidth)));
            _pagerProps.InsertScalar("LeftGlow", 0f);
            _pagerProps.InsertScalar("RightGlow", 0f);

            var curExpr = c.CreateExpressionAnimation("props.X");
            curExpr.SetReferenceParameter("props", _pagerProps);
            current.StartAnimation("Translation.X", curExpr);

            var prevExpr = c.CreateExpressionAnimation("props.X - props.Stride");
            prevExpr.SetReferenceParameter("props", _pagerProps);
            prev.StartAnimation("Translation.X", prevExpr);

            var nextExpr = c.CreateExpressionAnimation("props.X + props.Stride");
            nextExpr.SetReferenceParameter("props", _pagerProps);
            next.StartAnimation("Translation.X", nextExpr);

            var leftExpr = c.CreateExpressionAnimation("props.LeftGlow");
            leftExpr.SetReferenceParameter("props", _pagerProps);
            leftGlow.StartAnimation("Opacity", leftExpr);

            var rightExpr = c.CreateExpressionAnimation("props.RightGlow");
            rightExpr.SetReferenceParameter("props", _pagerProps);
            rightGlow.StartAnimation("Opacity", rightExpr);

            _pagerExpressionsReady = true;
            // N1: Composition drives Translation — keep XAML X at 0.
            XfCurrent.X = 0;
            XfNeighborPrev.X = 0;
            XfNeighborNext.X = 0;
            _pagerX = 0;
        }
        catch (Exception ex)
        {
            ErrorReporter.LogOnly("GalleryStillSurface EnsurePagerExpressions", ex);
            _pagerExpressionsReady = false;
            _pagerProps = null;
        }
    }

    private void SetPagerCompositionX(double visualX, double width, double leftGlow, double rightGlow)
    {
        EnsurePagerExpressions();
        _pagerX = visualX;
        if (_pagerProps is null)
        {
            XfCurrent.X = visualX;
            XfNeighborPrev.X = SwipeNav.NeighborTranslation(visualX, -1, width);
            XfNeighborNext.X = SwipeNav.NeighborTranslation(visualX, 1, width);
            BrdEdgeGlowLeft.Opacity = leftGlow;
            BrdEdgeGlowRight.Opacity = rightGlow;
            return;
        }

        _pagerProps.InsertScalar("X", (float)visualX);
        _pagerProps.InsertScalar("Stride", (float)SwipeNav.PageStride(width));
        _pagerProps.InsertScalar("LeftGlow", (float)leftGlow);
        _pagerProps.InsertScalar("RightGlow", (float)rightGlow);
        // N1: never mirror into XfCurrent when Composition owns Translation.X.
        XfCurrent.X = 0;
    }

    private void ApplyPagerDragVisual(double dragPx, double dragPy)
    {
        var width = Math.Max(1, ScrStill.ActualWidth);
        XfReviewTilt.CenterX = width / 2;
        XfReviewTilt.CenterY = Math.Max(1, ScrStill.ActualHeight) / 2;

        if (IsReviewMode)
        {
            // Review: current tracks finger with tilt + tint; never reveal neighbor.
            SetPagerCompositionX(dragPx, width, 0, 0);
            XfCurrent.Y = TouchGestures.IsVerticalAxis(dragPx, dragPy) ? dragPy : 0;
            XfReviewTilt.Angle = SwipeNav.ReviewTiltDegrees(dragPx, width);
            BrdReviewTint.Opacity = SwipeNav.ReviewTintOpacity(dragPx, width);
            BrdReviewTint.Background = SwipeNav.ReviewTintIsReject(dragPx)
                ? (_rejectTintBrush ??= new SolidColorBrush(Color.FromArgb(255, 220, 60, 60)))
                : (_keepTintBrush ??= new SolidColorBrush(Color.FromArgb(255, 60, 180, 90)));
            ImgNeighborPrev.Opacity = 0;
            ImgNeighborNext.Opacity = 0;
            TxtEdgeHint.Opacity = 0;
            TxtEdgeHint.Text = "";
            return;
        }

        var canPrev = _gallery?.CanGoPrevious == true;
        var canNext = _gallery?.CanGoNext == true;
        var visualX = SwipeNav.BrowseVisualOffset(dragPx, width, canPrev, canNext);
        XfCurrent.Y = 0;
        XfReviewTilt.Angle = 0;
        BrdReviewTint.Opacity = 0;

        var hint = SwipeNav.EdgeHintFor(dragPx, canPrev, canNext);
        var glow = hint == SwipeNav.EdgeHint.None
            ? 0
            : SwipeNav.EdgeGlowOpacity(visualX, width);
        var leftGlow = hint == SwipeNav.EdgeHint.StartOfFolder ? glow : 0;
        var rightGlow = hint == SwipeNav.EdgeHint.EndOfFolder ? glow : 0;
        SetPagerCompositionX(visualX, width, leftGlow, rightGlow);

        if (SwipeNav.RevealsNeighbor(reviewMode: false))
        {
            var slot = SwipeNav.NeighborSlot(visualX, reviewMode: false);
            ImgNeighborPrev.Opacity = slot < 0 && canPrev ? 1 : 0;
            ImgNeighborNext.Opacity = slot > 0 && canNext ? 1 : 0;
        }

        TxtEdgeHint.Text = SwipeNav.EdgeHintText(hint) ?? "";
        TxtEdgeHint.Opacity = hint == SwipeNav.EdgeHint.None ? 0 : Math.Min(1, glow + 0.35);
    }

    private void CompletePagerGesture()
    {
        // B1: pinch already cleared state — nothing to classify.
        if (!_reviewSwipeActive && !_pagerHandoff)
        {
            return;
        }

        var width = Math.Max(1, ScrStill.ActualWidth);
        var height = Math.Max(1, ScrStill.ActualHeight);
        var travel = _pagerHandoff
            ? SwipeNav.CommitTravel(_reviewSwipeDx, _pagerPanBeforeHandoff)
            : _reviewSwipeDx;
        var dx = _reviewSwipeDx;
        var dy = _reviewSwipeDy;
        var vx = _reviewSwipeVx;
        var vy = _reviewSwipeVy;
        _reviewSwipeActive = false;
        _reviewSwipeDx = 0;
        _reviewSwipeDy = 0;
        _reviewSwipeVx = 0;
        _reviewSwipeVy = 0;

        TouchGestures.SwipeAction action;
        if (IsReviewMode)
        {
            action = TouchGestures.ClassifyReviewSwipe(dx, dy, width, height, vx, vy);
        }
        else
        {
            action = SwipeNav.ClassifyBrowseCommit(
                travel,
                vx,
                width,
                canGoPrevious: _gallery?.CanGoPrevious == true,
                canGoNext: _gallery?.CanGoNext == true,
                travelPy: dy);
        }

        var committing = action is TouchGestures.SwipeAction.Reject
            or TouchGestures.SwipeAction.Keep
            or TouchGestures.SwipeAction.Skip
            or TouchGestures.SwipeAction.BrowsePrevious
            or TouchGestures.SwipeAction.BrowseNext;

        // B3: save snapshot for back-restore; reset local zoom only when KeepZoom off.
        // Never write GalleryViewModel.Scaling from the pager.
        if (committing
            && action is TouchGestures.SwipeAction.BrowsePrevious
                or TouchGestures.SwipeAction.BrowseNext)
        {
            SaveZoomSnapshotForLeave();
        }

        var animations = SystemAnimationsEnabled();
        var duration = SwipeNav.SettleDurationMs(animations, committing);
        if (IsReviewMode)
        {
            if (committing)
            {
                var canAdvance = action is TouchGestures.SwipeAction.Reject
                    or TouchGestures.SwipeAction.Keep
                    or TouchGestures.SwipeAction.Skip;
                // S7: only pending advance when a next item can arrive.
                _pendingAdvanceSlide = canAdvance;
                _pendingAdvanceSlot = canAdvance ? 1 : 0;
                // B4: navigate + chip cross-fade at start.
                if (canAdvance)
                {
                    BeginAwaitingContent();
                    ChipCrossFadeRequested?.Invoke();
                }

                ReviewSwipeCommitted?.Invoke(action);
                if (!canAdvance)
                {
                    _pendingAdvanceSlide = false;
                    AnimatePagerSettle(0, duration, overshoot: 0, onDone: () =>
                    {
                        ResetPagerVisuals();
                        _pagerHandoff = false;
                        _pagerPanBeforeHandoff = 0;
                    });
                }
            }
            else
            {
                _pendingAdvanceSlide = false;
                AnimatePagerSettle(0, duration, overshoot: SwipeNav.SnapOvershootPx(vx), onDone: () =>
                {
                    ResetPagerVisuals();
                    _pagerHandoff = false;
                    _pagerPanBeforeHandoff = 0;
                });
            }

            return;
        }

        if (!committing)
        {
            AnimatePagerSettle(
                0,
                duration,
                overshoot: SwipeNav.SnapOvershootPx(vx),
                onDone: () =>
                {
                    ResetPagerVisuals();
                    _pagerHandoff = false;
                    _pagerPanBeforeHandoff = 0;
                });
            return;
        }

        // B4: navigate + chip cross-fade at animation start; N3 settle after new still.
        if (SwipeNav.ResetsLocalZoomOnCommit(action, ViewerSettings.KeepZoomBetweenImages))
        {
            ResetView();
        }

        _pendingAdvanceSlot = SwipeNav.CommitSlot(action);
        BeginAwaitingContent();
        ChipCrossFadeRequested?.Invoke();
        ReviewSwipeCommitted?.Invoke(action);
    }

    private void AnimatePagerSettle(
        double targetX,
        int durationMs,
        double overshoot,
        Action? onDone)
    {
        StopPagerAnimations(invokeOnDone: false);
        _pagerOnDone = onDone;
        var width = Math.Max(1, ScrStill.ActualWidth);
        var from = ReadPagerX();
        if (durationMs <= 0 || !SystemAnimationsEnabled())
        {
            SetPagerCompositionX(targetX, width, 0, 0);
            if (Math.Abs(targetX) < 0.0001)
            {
                ResetPagerVisuals();
            }

            var done = _pagerOnDone;
            _pagerOnDone = null;
            done?.Invoke();
            return;
        }

        _pagerSettling = true;
        var generation = _pagerSettleGeneration;
        EnsurePagerExpressions();
        if (_pagerProps is not null)
        {
            try
            {
                var c = ElementCompositionPreview.GetElementVisual(ScrStill).Compositor;
                var anim = c.CreateScalarKeyFrameAnimation();
                anim.Duration = TimeSpan.FromMilliseconds(durationMs);
                anim.InsertKeyFrame(0, (float)from);
                if (overshoot > 0 && Math.Abs(targetX) < 0.0001)
                {
                    var peak = from + Math.Sign(from == 0 ? 1 : from) * -overshoot;
                    anim.InsertKeyFrame(0.55f, (float)peak);
                }

                var ease = c.CreateCubicBezierEasingFunction(
                    new System.Numerics.Vector2((float)SwipeNav.EaseX1, (float)SwipeNav.EaseY1),
                    new System.Numerics.Vector2((float)SwipeNav.EaseX2, (float)SwipeNav.EaseY2));
                anim.InsertKeyFrame(1, (float)targetX, ease);
                var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
                batch.Completed += (_, _) =>
                {
                    // N2: ignore stale completions after StopAnimation / interrupt.
                    if (generation != _pagerSettleGeneration)
                    {
                        return;
                    }

                    _pagerSettling = false;
                    SetPagerCompositionX(targetX, width, 0, 0);
                    var done = _pagerOnDone;
                    _pagerOnDone = null;
                    done?.Invoke();
                };
                _pagerProps.StartAnimation("X", anim);
                batch.End();
                return;
            }
            catch (Exception ex)
            {
                ErrorReporter.LogOnly("GalleryStillSurface AnimatePagerSettle composition", ex);
                // Fall through to Storyboard.
            }
        }

        var board = new Storyboard();
        var peakXaml = overshoot > 0 && Math.Abs(targetX) < 0.0001
            ? from + Math.Sign(from == 0 ? 1 : from) * -overshoot
            : targetX;

        void AddX(DependencyObject target, double fromValue, double to, TimeSpan start, TimeSpan length)
        {
            var frames = new DoubleAnimationUsingKeyFrames();
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(start),
                Value = fromValue
            });
            frames.KeyFrames.Add(new SplineDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(start + length),
                Value = to,
                KeySpline = new KeySpline
                {
                    ControlPoint1 = new Point(SwipeNav.EaseX1, SwipeNav.EaseY1),
                    ControlPoint2 = new Point(SwipeNav.EaseX2, SwipeNav.EaseY2)
                }
            });
            Storyboard.SetTarget(frames, target);
            Storyboard.SetTargetProperty(frames, "X");
            board.Children.Add(frames);
        }

        var half = TimeSpan.FromMilliseconds(durationMs * 0.55);
        var full = TimeSpan.FromMilliseconds(durationMs);
        if (overshoot > 0 && Math.Abs(targetX) < 0.0001)
        {
            AddX(XfCurrent, from, peakXaml, TimeSpan.Zero, half);
            AddX(XfCurrent, peakXaml, 0, half, full - half);
        }
        else
        {
            AddX(XfCurrent, from, targetX, TimeSpan.Zero, full);
            AddX(
                XfNeighborPrev,
                SwipeNav.NeighborTranslation(from, -1, width),
                SwipeNav.NeighborTranslation(targetX, -1, width),
                TimeSpan.Zero,
                full);
            AddX(
                XfNeighborNext,
                SwipeNav.NeighborTranslation(from, 1, width),
                SwipeNav.NeighborTranslation(targetX, 1, width),
                TimeSpan.Zero,
                full);
        }

        board.Completed += (_, _) =>
        {
            if (generation != _pagerSettleGeneration)
            {
                return;
            }

            _pagerSettling = false;
            _pagerStoryboard = null;
            SetPagerCompositionX(targetX, width, 0, 0);
            var done = _pagerOnDone;
            _pagerOnDone = null;
            done?.Invoke();
        };
        _pagerStoryboard = board;
        board.Begin();
    }

    private void PlayAdvanceSlideIn(int slot)
    {
        if (slot == 0 || !SystemAnimationsEnabled())
        {
            ResetPagerVisuals();
            return;
        }

        var width = Math.Max(1, ScrStill.ActualWidth);
        var start = slot > 0
            ? SwipeNav.PageStride(width)
            : -SwipeNav.PageStride(width);
        SetPagerCompositionX(start, width, 0, 0);
        AnimatePagerSettle(0, SwipeNav.CommitMs, overshoot: 0, onDone: ResetPagerVisuals);
    }

    private void ResetPagerVisuals()
    {
        StopPagerAnimations(invokeOnDone: false);
        var width = Math.Max(1, ScrStill.ActualWidth);
        SetPagerCompositionX(0, width, 0, 0);
        XfCurrent.Y = 0;
        XfReviewTilt.Angle = 0;
        BrdReviewTint.Opacity = 0;
        ImgNeighborPrev.Opacity = 0;
        ImgNeighborNext.Opacity = 0;
        ClearEdgeChrome();
    }

    private void ClearEdgeChrome()
    {
        if (_pagerProps is not null)
        {
            _pagerProps.InsertScalar("LeftGlow", 0f);
            _pagerProps.InsertScalar("RightGlow", 0f);
        }

        BrdEdgeGlowLeft.Opacity = 0;
        BrdEdgeGlowRight.Opacity = 0;
        TxtEdgeHint.Opacity = 0;
        TxtEdgeHint.Text = "";
    }

    private void ClearNeighbors()
    {
        ImgNeighborPrev.Source = null;
        ImgNeighborNext.Source = null;
        ImgNeighborPrev.Opacity = 0;
        ImgNeighborNext.Opacity = 0;
        _neighborPrevPath = null;
        _neighborNextPath = null;
    }

    private void RefreshNeighbors()
    {
        if (_gallery is null || IsReviewMode)
        {
            ClearNeighbors();
            return;
        }

        LoadNeighbor(ImgNeighborPrev, -1, ref _neighborPrevPath);
        LoadNeighbor(ImgNeighborNext, +1, ref _neighborNextPath);
        var width = Math.Max(1, ScrStill.ActualWidth);
        if (!_pagerExpressionsReady)
        {
            XfNeighborPrev.X = -SwipeNav.PageStride(width);
            XfNeighborNext.X = SwipeNav.PageStride(width);
        }
        else if (_pagerProps is not null)
        {
            _pagerProps.InsertScalar("Stride", (float)SwipeNav.PageStride(width));
        }
    }

    private void LoadNeighbor(Image target, int delta, ref string? cachedPath)
    {
        var item = _gallery?.PeekNeighbor(delta);
        var path = GalleryViewModel.NeighborSourcePath(item);
        if (string.IsNullOrWhiteSpace(path))
        {
            target.Source = null;
            cachedPath = null;
            return;
        }

        // Skip UI-thread Exists when the same path is already loaded (should-fix 4).
        if (cachedPath == path && target.Source is not null)
        {
            return;
        }

        // Screen-size decode from LargePreview JPEG (docs/specs/swipe-nav.md).
        var decode = (int)Math.Round(
            Math.Max(ScrStill.ActualWidth, 320) * (XamlRoot?.RasterizationScale ?? 1.0));
        try
        {
            if (GalleryMedia.IsRemoteUri(path))
            {
                target.Source = new BitmapImage
                {
                    DecodePixelWidth = decode,
                    AutoPlay = false,
                    UriSource = new Uri(path, UriKind.Absolute)
                };
                cachedPath = path;
                return;
            }

            if (!CloudFile.Exists(path) || CloudFile.IsOnlineOnly(path))
            {
                target.Source = null;
                cachedPath = null;
                return;
            }

            // S3: neighbor GIFs must not autoplay.
            target.Source = new BitmapImage
            {
                DecodePixelWidth = decode,
                AutoPlay = false,
                UriSource = new Uri(path, UriKind.Absolute)
            };
            cachedPath = path;
        }
        catch
        {
            target.Source = null;
            cachedPath = null;
        }
    }

    private void SaveZoomSnapshotForLeave()
    {
        var path = _gallery?.CurrentPath ?? _gallery?.Current?.Path;
        var snap = new SwipeZoomRestore.Snapshot(
            _gallery?.Scaling ?? ImageScaling.Fit,
            _pinchZoom,
            ScrStill.HorizontalOffset,
            ScrStill.VerticalOffset);
        if (SwipeZoomRestore.IsWorthSaving(snap))
        {
            _zoomRestore.Save(path, snap);
        }
    }

    private void QueueZoomRestoreForCurrent()
    {
        var path = _gallery?.CurrentPath ?? _gallery?.Current?.Path;
        if (!_zoomRestore.TryRestore(path, out var snap))
        {
            _pendingZoomRestore = null;
            return;
        }

        // S6: apply after ImageOpened / ApplyScaleLayout — never write Scaling here
        // when KeepZoom already sticky-applied it; restore pinch/pan offsets.
        _pendingZoomRestore = snap;
        if (_gallery is not null
            && !ViewerSettings.KeepZoomBetweenImages
            && snap.Scaling != _gallery.Scaling)
        {
            _gallery.Scaling = snap.Scaling;
        }
    }

    private void ApplyPendingZoomRestore()
    {
        if (_pendingZoomRestore is not { } snap)
        {
            return;
        }

        _pendingZoomRestore = null;
        _pinchZoom = snap.PinchZoom;
        ApplyScaleLayout();
        ScrStill.ChangeView(snap.OffsetX, snap.OffsetY, null, true);
    }
}
