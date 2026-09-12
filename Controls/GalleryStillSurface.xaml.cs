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
            ClearHdrCache();
            _presenter?.Dispose();
            _presenter = null;
        };
    }

    public void Bind(GalleryViewModel? gallery)
    {
        if (_gallery is not null)
        {
            _gallery.PropertyChanged -= Gallery_PropertyChanged;
        }

        _gallery = gallery;
        if (_gallery is not null)
        {
            _gallery.PropertyChanged += Gallery_PropertyChanged;
            _ = RefreshAsync();
            return;
        }

        ClearHdrCache();
        HideHdr();
        ImgStill.Source = null;
    }

    private void Gallery_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryViewModel.Scaling))
        {
            ApplyScaleLayout();
            PresentCached();
            return;
        }

        if (e.PropertyName is nameof(GalleryViewModel.PreviewImageUri)
            or nameof(GalleryViewModel.CurrentPath)
            or nameof(GalleryViewModel.IsImage)
            or nameof(GalleryViewModel.IsVideo)
            or nameof(GalleryViewModel.CurrentProbe))
        {
            _ = RefreshAsync();
        }
    }

    private void GrdStillHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyScaleLayout();
        if (_gallery is { IsImage: true } && ScpHdr.Visibility == Visibility.Visible)
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

        if (epoch != _epoch)
        {
            return;
        }

        await UiDispatch.RunAsync(() =>
        {
            if (epoch != _epoch)
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
        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        var presented = _presenter.TryPresent(frame, gallery.Scaling, (float)scale);
        if (presented)
        {
            ScpHdr.Visibility = Visibility.Visible;
            ImgStill.Visibility = Visibility.Collapsed;
            gallery.SetHdrPresentResult(true, _presenter.DisplayIsHdr);
            return;
        }

        HideHdr();
        gallery.SetHdrPresentResult(false, _presenter.DisplayIsHdr);
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
        if (_hdrFrame is { Width: > 0, Height: > 0 } frame)
        {
            return (frame.Width, frame.Height);
        }

        var item = _gallery?.Current;
        if (item is { Width: > 0, Height: > 0 })
        {
            return (item.Width.Value, item.Height.Value);
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
