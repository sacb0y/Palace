using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        if (e.PropertyName is nameof(GalleryViewModel.Scaling))
        {
            ApplyScaleLayout();
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
        var presented = _presenter.TryPresent(frame, gallery.Scaling, (float)scale, dipW, dipH);
        if (presented)
        {
            ImgStill.Visibility = Visibility.Collapsed;
            gallery.SetHdrPresentResult(true, _presenter.DisplayIsHdr);
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
        if (scrolls)
        {
            var (iw, ih) = StillPixelSize();
            if (iw > 0 && ih > 0)
            {
                width = iw;
                height = ih;
            }
            else
            {
                width = double.NaN;
                height = double.NaN;
            }
        }
        else
        {
            width = ScrStill.ActualWidth;
            height = ScrStill.ActualHeight;
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
    }

    private (int Width, int Height) StillPixelSize()
    {
        // Oriented WIC / BitmapImage size. Catalog Width/Height are unoriented
        // headers — 90°/270° EXIF would get the wrong 1:1 aspect.
        if (_hdrFrame is { Width: > 0, Height: > 0 } frame)
        {
            return (frame.Width, frame.Height);
        }

        return (0, 0);
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
