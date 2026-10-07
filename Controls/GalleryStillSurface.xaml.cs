using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Palace.Helpers;
using Palace.Services;
using Palace.ViewModels;

namespace Palace.Controls;

public sealed partial class GalleryStillSurface : UserControl
{
    private GalleryViewModel? _gallery;
    private HdrSwapchainPresenter? _presenter;
    private HdrFrame? _hdrFrame;
    private string? _hdrFramePath;
    private float _hdrMapMaxNits;
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
    private double _pinchOldZoom = 1;
    private double _coalescedPanX;
    private double _coalescedPanY;
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
        Unloaded += (_, _) =>
        {
            ScrStill.RemoveHandler(UIElement.PointerWheelChangedEvent, _wheelHandler);
            UnhookPeak();
            _gallery?.StopGifPlayback();
            CancelInFlight();
            ClearHdrCache();
            ClearGifCache();
            _presenter?.Dispose();
            _presenter = null;
        };
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
        ResetView();
        _gallery = gallery;
        if (_gallery is not null)
        {
            HookPeak();
            _gallery.PropertyChanged += Gallery_PropertyChanged;
            _ = RefreshAsync();
            return;
        }

        UnhookPeak();
        HideHdr();
        ClearGifCache();
        ImgStill.Source = null;
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
        if (e.PropertyName is nameof(GalleryViewModel.Scaling))
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
                    g.Scaling);
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

            _ = RefreshAsync();
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
        if (_gallery is { IsImage: true } gallery && _hdrFrame is not null)
        {
            var raster = XamlRoot?.RasterizationScale ?? 1.0;
            var (dipW, dipH) = HdrPanelDips();
            var want = GalleryPresent.PresentDecodeSize(
                _hdrFrame.NativeWidth,
                _hdrFrame.NativeHeight,
                (int)Math.Round(Math.Max(dipW, 0) * raster),
                (int)Math.Round(Math.Max(dipH, 0) * raster),
                gallery.Scaling);
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

        // Do not BitmapImage the original HDR file — that is a second full
        // WIC decode. Show the mosaic thumb until scRGB present wins.
        ImgStill.Source = ToStillImage(attempt ? item?.ThumbPath : still, animateGif: true);

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

        var raster = XamlRoot?.RasterizationScale ?? 1.0;
        var (dipW, dipH) = HdrPanelDips();
        var viewPxW = (int)Math.Round(Math.Max(dipW, 0) * raster);
        var viewPxH = (int)Math.Round(Math.Max(dipH, 0) * raster);
        HdrFrame? frame = CachedFrame(still, gallery.CurrentProbe);
        if (frame is not null)
        {
            var want = GalleryPresent.PresentDecodeSize(
                frame.NativeWidth, frame.NativeHeight, viewPxW, viewPxH, gallery.Scaling);
            if (GalleryPresent.NeedsBetterDecode(frame.Width, frame.Height, want.Width, want.Height))
            {
                frame = null;
            }
        }

        if (frame is null)
        {
            _hdrLoadCts = GalleryPresent.LiveTokenSource(_hdrLoadCts);
            try
            {
                frame = await HdrWicDecode.TryLoadAsync(
                    still,
                    gallery.CurrentProbe,
                    viewPxW,
                    viewPxH,
                    gallery.Scaling,
                    _hdrLoadCts.Token);
            }
            catch
            {
                frame = null;
            }
        }

        if (epoch != _epoch || !ReferenceEquals(_gallery, gallery))
        {
            return;
        }

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
                ImgStill.Source = ToStillImage(still);
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
                    gallery.SetHdrStats(frame.MaxNits, frame.AvgNits, frame.MinNits, frame.MaxScrgb);
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
        catch
        {
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
        ImageScaling scaling = ImageScaling.Fit;
        await UiDispatch.RunAsync(() =>
        {
            if (_gallery is not { IsImage: true } g || _hdrFrame is not { } f)
            {
                return;
            }

            gallery = g;
            frame = f;
            scaling = g.Scaling;
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
        HdrPresentOutcome outcome;
        try
        {
            outcome = await presenterRef.TryPresentAsync(
                frameRef, scaling, (float)scale, dipW, dipH, peakOverride, mapMaxNits, StillCurrent, cts.Token);
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
                        frameRef.Width, frameRef.Height, frameRef.NativeWidth, frameRef.NativeHeight));
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
        var primaries = probe.CicpPrimaries;
        var channelMax = GalleryHistogram.ChannelMax(hdr);
        _ = Task.Run(() =>
        {
            GalleryHistogramBins bins;
            try
            {
                bins = GalleryHistogram.FromScrgb(
                    rgba, width, height, primaries, GalleryHistogram.BinCount, GalleryHistogram.DefaultMaxSamples, channelMax);
            }
            catch
            {
                bins = GalleryHistogramBins.Empty;
            }

            return UiDispatch.RunAsync(() =>
            {
                if (epoch != Volatile.Read(ref _histEpoch) || !ReferenceEquals(_gallery, gallery))
                {
                    return;
                }

                gallery.SetHistogram(bins);
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
        var scaling = gallery.Scaling;
        _ = Task.Run(async () =>
        {
            GalleryHistogramBins bins;
            try
            {
                var frame = await HdrWicDecode.TryLoadAsync(
                    path, probe, viewPxW, viewPxH, scaling, token).ConfigureAwait(false);
                bins = frame is null
                    ? GalleryHistogramBins.Empty
                    : GalleryHistogram.FromScrgb(
                        frame.ScrgbRgba,
                        frame.Width,
                        frame.Height,
                        probe.CicpPrimaries,
                        GalleryHistogram.BinCount,
                        GalleryHistogram.DefaultMaxSamples,
                        GalleryHistogram.ChannelMax(probe.CanPresentHdr));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                bins = GalleryHistogramBins.Empty;
            }

            await UiDispatch.RunAsync(() =>
            {
                if (epoch != Volatile.Read(ref _histEpoch) || !ReferenceEquals(_gallery, gallery))
                {
                    return;
                }

                gallery.SetHistogram(bins);
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
            GalleryHistogramBins bins;
            try
            {
                bins = GalleryHistogram.FromPacked8(bgra, width, height, HdrPackedFormat.Bgra8);
            }
            catch
            {
                bins = GalleryHistogramBins.Empty;
            }

            return UiDispatch.RunAsync(() =>
            {
                if (epoch != Volatile.Read(ref _histEpoch) || !ReferenceEquals(_gallery, gallery))
                {
                    return;
                }

                gallery.SetHistogram(bins);
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
        _hdrFrameProbe = HdrProbe.None;
    }

    private void ApplyScaleLayout()
    {
        var scaling = _gallery?.Scaling ?? ImageScaling.Fit;
        var scrolls = GalleryScale.Scrolls(scaling, _pinchZoom);
        var bars = GalleryScale.ShowsScrollBars()
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Hidden;
        ScrStill.HorizontalScrollBarVisibility = bars;
        ScrStill.VerticalScrollBarVisibility = bars;
        ImgStill.Stretch = BindHelpers.ImageStretch(scaling);

        var align = scrolls ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        var valign = scrolls ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        GrdStillContent.HorizontalAlignment = align;
        GrdStillContent.VerticalAlignment = valign;
        ImgStill.HorizontalAlignment = align;
        ImgStill.VerticalAlignment = valign;
        ScpHdr.HorizontalAlignment = align;
        ScpHdr.VerticalAlignment = valign;

        double width;
        double height;
        var viewW = ScrStill.ActualWidth;
        var viewH = ScrStill.ActualHeight;
        if (scaling == ImageScaling.Actual)
        {
            var (iw, ih) = StillPixelSize();
            var raster = XamlRoot?.RasterizationScale ?? 1.0;
            (width, height) = GalleryScale.ActualDipSize(iw, ih, raster);
        }
        else if (scaling == ImageScaling.Fill)
        {
            var (iw, ih) = StillPixelSize();
            (width, height) = GalleryScale.FillCoverDipSize(iw, ih, viewW, viewH);
        }
        else
        {
            width = viewW;
            height = viewH;
            if (width <= 1 || height <= 1)
            {
                width = double.NaN;
                height = double.NaN;
            }
        }

        (width, height) = GalleryScale.ApplyPinchZoom(width, height, _pinchZoom);

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
        var oldZoom = _pinchOldZoom;
        var newZoom = _pinchZoom;
        var panX = _coalescedPanX;
        var panY = _coalescedPanY;
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
        var (horizontal, vertical) = preserve
            ? GalleryScale.PinchThenDrag(
                ScrStill.HorizontalOffset,
                ScrStill.VerticalOffset,
                originX,
                originY,
                oldZoom,
                newZoom,
                panX,
                panY,
                ScrStill.ScrollableWidth,
                ScrStill.ScrollableHeight)
            : GalleryScale.InitialScrollOffset(
                scaling, contentW, contentH, viewW, viewH);
        ScrStill.ChangeView(horizontal, vertical, null, true);
    }

    private void ResetView()
    {
        _panning = false;
        ResetScrollTracking();
        ResetPinchTracking();
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
        _pinchOldZoom = 1;
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

    private void ApplyPinchFrame(
        double originX,
        double originY,
        double nextZoom,
        double panX,
        double panY)
    {
        _preservePinchPan = true;
        _pinchOriginX = originX;
        _pinchOriginY = originY;
        _pinchOldZoom = _pinchZoom;
        _pinchZoom = nextZoom;
        _coalescedPanX = panX;
        _coalescedPanY = panY;
        ApplyScaleLayout();
    }

    private void ImgStill_ImageOpened(object sender, RoutedEventArgs e)
    {
        if (_gallery?.Scaling is ImageScaling.Actual or ImageScaling.Fill)
        {
            ApplyScaleLayout();
        }
    }

    private void ScrStill_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var type = e.Pointer.PointerDeviceType;
        var point = e.GetCurrentPoint(ScrStill);
        CancelScrollViewerDirectManipulation();
        if (!GalleryScale.UsesPointerCapturePan(
                GalleryScale.Scrolls(_gallery?.Scaling ?? ImageScaling.Fit, _pinchZoom),
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
        if (!_panning)
        {
            return;
        }

        var point = e.GetCurrentPoint(ScrStill);
        var type = e.Pointer.PointerDeviceType;
        if (type == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)
        {
            EndPan(e.Pointer);
            return;
        }

        var (horizontal, vertical) = GalleryScale.DragPan(
            ScrStill.HorizontalOffset,
            ScrStill.VerticalOffset,
            point.Position.X - _panLastX,
            point.Position.Y - _panLastY,
            ScrStill.ScrollableWidth,
            ScrStill.ScrollableHeight);
        _panLastX = point.Position.X;
        _panLastY = point.Position.Y;
        ScrStill.ChangeView(horizontal, vertical, null, true);
        e.Handled = true;
    }

    private void ScrStill_PointerReleased(object sender, PointerRoutedEventArgs e) =>
        EndPan(e.Pointer);

    private void ScrStill_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        _panning = false;

    private void EndPan(Pointer pointer)
    {
        if (!_panning)
        {
            return;
        }

        _panning = false;
        ScrStill.ReleasePointerCapture(pointer);
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
        if (!GalleryScale.HandlesManipulationDelta(
                e.IsInertial, isTouch, isPen, isMouse, leftButton: _panning))
        {
            return;
        }

        var scaling = _gallery?.Scaling ?? ImageScaling.Fit;
        var scaleDelta = e.Delta.Scale;
        var nextZoom = GalleryScale.PinchZoom(_pinchZoom, scaleDelta);
        var didPinch = scaleDelta > 0 && Math.Abs(nextZoom - _pinchZoom) > 0.0001;
        var deltaX = e.Delta.Translation.X;
        var deltaY = e.Delta.Translation.Y;
        var hasPan = deltaX != 0 || deltaY != 0;

        if (didPinch)
        {
            ApplyPinchFrame(e.Position.X, e.Position.Y, nextZoom, deltaX, deltaY);
            e.Handled = true;
            return;
        }

        if (!GalleryScale.Scrolls(scaling, _pinchZoom) || !hasPan)
        {
            return;
        }

        var (horizontal, vertical) = GalleryScale.DragPan(
            ScrStill.HorizontalOffset,
            ScrStill.VerticalOffset,
            deltaX,
            deltaY,
            ScrStill.ScrollableWidth,
            ScrStill.ScrollableHeight);
        ScrStill.ChangeView(horizontal, vertical, null, true);
        e.Handled = true;
    }

    private void ScrStill_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ScrStill);
        var delta = point.Properties.MouseWheelDelta;
        CancelScrollViewerDirectManipulation();
        var controlDown = e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control);
        var scaling = _gallery?.Scaling ?? ImageScaling.Fit;
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
            return;
        }

        var pan = GalleryScale.WheelToPanDelta(delta);
        var horizontalWheel = point.Properties.IsHorizontalMouseWheel;
        var (horizontal, vertical) = GalleryScale.DragPan(
            ScrStill.HorizontalOffset,
            ScrStill.VerticalOffset,
            horizontalWheel ? pan : 0,
            horizontalWheel ? 0 : pan,
            ScrStill.ScrollableWidth,
            ScrStill.ScrollableHeight);
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
            if (GifFrames.PickCached(_gifRasters, GifFrames.ClampIndex((int)Math.Round(gallery.GifFrameIndex), _gifRasters!.Count)) is { } cached)
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

        ImgStill.Source = bmp ?? ToStillImage(gallery.Current?.Path);
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
}
