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
    private bool _panning;
    private double _panLastX;
    private double _panLastY;
    private ImageScaling _scrollScaling;
    private int _scrollRevision = int.MinValue;
    private double _scrollContentW;
    private double _scrollContentH;
    private double _scrollViewW;
    private double _scrollViewH;

    public GalleryStillSurface()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
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
            _gallery.PropertyChanged += Gallery_PropertyChanged;
            _ = RefreshAsync();
            return;
        }

        HideHdr();
        ImgStill.Source = null;
    }

    private void CancelInFlight() => Interlocked.Increment(ref _epoch);

    private void Gallery_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryViewModel.Scaling)
            or nameof(GalleryViewModel.PeakOverrideEnabled)
            or nameof(GalleryViewModel.PeakOverrideNits))
        {
            if (e.PropertyName is nameof(GalleryViewModel.Scaling))
            {
                ApplyScaleLayout();
            }

            PresentCached();
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
        if (_gallery is { IsImage: true } && _hdrFrame is not null)
        {
            PresentCached();
        }
    }

    private async Task RefreshAsync()
    {
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
        ImgStill.Source = ToStillImage(still);

        var item = gallery.Current;
        var pathMatches = still is not null
            && item is not null
            && string.Equals(still, item.Path, StringComparison.OrdinalIgnoreCase);
        if (!pathMatches)
        {
            ClearHdrCache();
            HideHdr();
            gallery.SetHdrPresentResult(false, false);
            return;
        }

        var attempt = GalleryPresent.ShouldAttemptHdrPresent(
            item.Kind,
            item.IsOrphan,
            item.IsOnlineOnly,
            AssetItemMapper.IsApiOnly(item),
            CloudFile.Exists(item.Path) && !CloudFile.IsOnlineOnly(item.Path),
            gallery.CurrentProbe);

        if (!attempt || still is null)
        {
            ClearHdrCache();
            HideHdr();
            gallery.SetHdrPresentResult(false, false);
            return;
        }

        HdrFrame? frame = CachedFrame(still, gallery.CurrentProbe);
        if (frame is null)
        {
            try
            {
                frame = await HdrWicDecode.TryLoadAsync(still, gallery.CurrentProbe, CancellationToken.None);
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
                gallery.SetHdrPresentResult(false, false);
                return;
            }

            _hdrFrame = frame;
            _hdrFramePath = still;
            _hdrFrameProbe = gallery.CurrentProbe;
            ApplyScaleLayout();
            PresentFrame(gallery, frame);
        });
    }

    private HdrFrame? CachedFrame(string path, HdrProbe probe) =>
        _hdrFrame is not null
        && string.Equals(_hdrFramePath, path, StringComparison.OrdinalIgnoreCase)
        && _hdrFrameProbe.Equals(probe)
            ? _hdrFrame
            : null;

    private void PresentCached()
    {
        if (_gallery is not { IsImage: true } gallery || _hdrFrame is null)
        {
            return;
        }

        PresentFrame(gallery, _hdrFrame);
    }

    private void PresentFrame(GalleryViewModel gallery, HdrFrame frame)
    {
        _presenter ??= new HdrSwapchainPresenter(ScpHdr);
        ApplyScaleLayout();
        // Collapsed panels report ActualWidth 0. Show the swapchain under the
        // SDR image so layout/composition can run, then hide the image on success.
        ScpHdr.Visibility = Visibility.Visible;
        ImgStill.Visibility = Visibility.Visible;
        var (dipW, dipH) = HdrPanelDips();
        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        float? peakOverride = gallery.PeakOverrideEnabled
            ? (float)gallery.PeakOverrideNits
            : null;
        var presented = _presenter.TryPresent(
            frame, gallery.Scaling, (float)scale, dipW, dipH, peakOverride);
        if (presented)
        {
            ImgStill.Visibility = Visibility.Collapsed;
            gallery.SetHdrPresentResult(
                true,
                _presenter.DisplayIsHdr,
                _presenter.DisplayPeakNits,
                frame.MaxNits,
                frame.AvgNits,
                frame.MinNits);
            return;
        }

        HideHdr();
        gallery.SetHdrPresentResult(false, _presenter.DisplayIsHdr);
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
        if (_hdrFrame is { Width: > 0, Height: > 0 } frame)
        {
            return (frame.Width, frame.Height);
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
