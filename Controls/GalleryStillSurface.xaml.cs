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
    private HdrProbe _hdrFrameProbe = HdrProbe.None;
    private int _epoch;
    private CancellationTokenSource? _hdrLoadCts;
    private CancellationTokenSource? _presentCts;
    private readonly HdrPresentCoalescer _presentQueue = new();
    private bool _panning;
    private double _panLastX;
    private double _panLastY;
    private ImageScaling _scrollScaling;
    private int _scrollRevision = int.MinValue;
    private double _scrollContentW;
    private double _scrollContentH;
    private double _scrollViewW;
    private double _scrollViewH;
    private bool _peakHooked;

    public GalleryStillSurface()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            UnhookPeak();
            CancelInFlight();
            ClearHdrCache();
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
        }

        CancelInFlight();
        ClearHdrCache();
        ResetScrollTracking();
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
        _hdrLoadCts?.Cancel();
        _presentCts?.Cancel();
    }

    private void Gallery_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryViewModel.Scaling))
        {
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

        if (e.PropertyName is nameof(GalleryViewModel.StillRevision))
        {
            _ = RefreshAsync();
        }
    }

    private void GrdStillHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
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
        _hdrLoadCts?.Cancel();
        _presentCts?.Cancel();
        var epoch = Interlocked.Increment(ref _epoch);
        var gallery = _gallery;
        ApplyScaleLayout();
        if (gallery is not { IsImage: true })
        {
            ClearHdrCache();
            HideHdr();
            ImgStill.Source = null;
            return;
        }

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
        ImgStill.Source = ToStillImage(attempt ? item?.ThumbPath : still);

        if (!pathMatches)
        {
            ClearHdrCache();
            HideHdr();
            gallery.SetHdrPresentResult(false, false);
            return;
        }

        if (!attempt || still is null)
        {
            ClearHdrCache();
            HideHdr();
            gallery.SetHdrPresentResult(false, false);
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
                return;
            }

            _hdrFrame = frame;
            _hdrFramePath = still;
            _hdrFrameProbe = gallery.CurrentProbe;
            ApplyScaleLayout();
            RequestPresent(HdrPresentCoalescer.ImmediateMs);
            if (!GalleryPresent.IsNativeDecode(frame.Width, frame.Height, frame.NativeWidth, frame.NativeHeight))
            {
                _hdrLoadCts = GalleryPresent.LiveTokenSource(_hdrLoadCts);
                _ = MeasureStatsAsync(gallery, still, gallery.CurrentProbe, epoch, _hdrLoadCts.Token);
            }
        });
    }

    private async Task MeasureStatsAsync(
        GalleryViewModel gallery,
        string path,
        HdrProbe probe,
        int epoch,
        CancellationToken cancellation)
    {
        HdrStats? stats = null;
        try
        {
            stats = await HdrWicDecode.TryMeasureAsync(path, probe, cancellation);
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
            while (_presentQueue.TryTake(out var delayMs))
            {
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                    waited += delayMs;
                    if (_presentQueue.ShouldRestartDebounce(waited))
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
            _presentQueue.Abort();
        }
    }

    private async Task PresentOnceAsync()
    {
        if (_gallery is not { IsImage: true } gallery || _hdrFrame is not { } frame)
        {
            return;
        }

        var epoch = Volatile.Read(ref _epoch);
        _presenter ??= new HdrSwapchainPresenter(ScpHdr);
        var presenter = _presenter;
        ApplyScaleLayout();
        // Collapsed panels report ActualWidth 0. Show the swapchain under the
        // SDR image so layout/composition can run, then hide the image on success.
        ScpHdr.Visibility = Visibility.Visible;
        ImgStill.Visibility = Visibility.Visible;
        var (dipW, dipH) = HdrPanelDips();
        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        float? peakOverride = GalleryPeak.PresentOverrideNits;
        bool StillCurrent() =>
            epoch == Volatile.Read(ref _epoch)
            && ReferenceEquals(_gallery, gallery)
            && ReferenceEquals(_hdrFrame, frame)
            && ReferenceEquals(_presenter, presenter);

        using var cts = new CancellationTokenSource();
        _presentCts = cts;
        HdrPresentOutcome outcome;
        try
        {
            outcome = await presenter.TryPresentAsync(
                frame, gallery.Scaling, (float)scale, dipW, dipH, peakOverride, StillCurrent, cts.Token);
        }
        finally
        {
            if (ReferenceEquals(_presentCts, cts))
            {
                _presentCts = null;
            }
        }

        if (outcome == HdrPresentOutcome.Cancelled || !StillCurrent())
        {
            return;
        }

        if (outcome == HdrPresentOutcome.Presented)
        {
            ImgStill.Visibility = Visibility.Collapsed;
            gallery.SetHdrPresentResult(
                true,
                presenter.DisplayIsHdr,
                presenter.DisplayPeakNits,
                frame.MaxNits,
                frame.AvgNits,
                frame.MinNits,
                frame.MaxScrgb,
                frame.NativeWidth,
                frame.NativeHeight,
                GalleryPresent.IsNativeDecode(frame.Width, frame.Height, frame.NativeWidth, frame.NativeHeight));
            return;
        }

        HideHdr();
        gallery.SetHdrPresentResult(false, presenter.DisplayIsHdr);
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

    private void HideHdr()
    {
        ScpHdr.Visibility = Visibility.Collapsed;
        ImgStill.Visibility = Visibility.Visible;
    }

    private void ClearHdrCache()
    {
        _hdrFrame = null;
        _hdrFramePath = null;
        _hdrFrameProbe = HdrProbe.None;
    }

    private void ApplyScaleLayout()
    {
        var scaling = _gallery?.Scaling ?? ImageScaling.Fit;
        var scrolls = GalleryScale.Scrolls(scaling);
        ScrStill.HorizontalScrollBarVisibility = scrolls
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
        ScrStill.VerticalScrollBarVisibility = scrolls
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
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
            return;
        }

        var revision = _gallery?.StillRevision ?? 0;
        if (scaling == _scrollScaling
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

        var (horizontal, vertical) = GalleryScale.InitialScrollOffset(
            scaling, contentW, contentH, viewW, viewH);
        ScrStill.UpdateLayout();
        ScrStill.ChangeView(horizontal, vertical, null, true);
    }

    private void ResetScrollTracking()
    {
        _scrollRevision = int.MinValue;
        _scrollContentW = 0;
        _scrollContentH = 0;
        _scrollViewW = 0;
        _scrollViewH = 0;
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

        return (0, 0);
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
        if (!GalleryScale.Scrolls(_gallery?.Scaling ?? ImageScaling.Fit)
            || e.Pointer.PointerDeviceType != PointerDeviceType.Mouse)
        {
            return;
        }

        var point = e.GetCurrentPoint(ScrStill);
        if (!point.Properties.IsLeftButtonPressed)
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
        if (!point.Properties.IsLeftButtonPressed)
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

    private static BitmapImage? ToStillImage(string? path)
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

            return new BitmapImage { UriSource = new Uri(path, UriKind.Absolute) };
        }
        catch
        {
            return null;
        }
    }
}
