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
    private int _epoch;
    private bool _sizeHooked;

    public GalleryStillSurface()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
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
        }

        _ = RefreshAsync();
    }

    private void Gallery_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryViewModel.PreviewImageUri)
            or nameof(GalleryViewModel.CurrentPath)
            or nameof(GalleryViewModel.IsImage)
            or nameof(GalleryViewModel.IsVideo)
            or nameof(GalleryViewModel.Scaling)
            or nameof(GalleryViewModel.CurrentProbe))
        {
            _ = RefreshAsync();
        }
    }

    private void GrdStillHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_sizeHooked && _gallery is { IsImage: true } && ScpHdr.Visibility == Visibility.Visible)
        {
            _ = RefreshAsync();
        }

        _sizeHooked = true;
        ApplyScaleLayout();
    }

    private async Task RefreshAsync()
    {
        var epoch = Interlocked.Increment(ref _epoch);
        var gallery = _gallery;
        ApplyScaleLayout();
        if (gallery is not { IsImage: true })
        {
            HideHdr();
            ImgStill.Source = null;
            return;
        }

        var still = gallery.PreviewImageUri ?? gallery.CurrentPath;
        ImgStill.Source = ToStillImage(still);

        var item = gallery.Current;
        var attempt = item is not null
            && GalleryPresent.ShouldAttemptHdrPresent(
                item.Kind,
                item.IsOrphan,
                item.IsOnlineOnly,
                AssetItemMapper.IsApiOnly(item),
                CloudFile.Exists(item.Path) && !CloudFile.IsOnlineOnly(item.Path),
                gallery.CurrentProbe)
            && still is not null
            && string.Equals(still, item.Path, StringComparison.OrdinalIgnoreCase);

        if (!attempt || still is null)
        {
            HideHdr();
            gallery.SetHdrPresentResult(false, false);
            return;
        }

        HdrFrame? frame;
        try
        {
            frame = await HdrWicDecode.TryLoadAsync(still, gallery.CurrentProbe, CancellationToken.None);
        }
        catch
        {
            frame = null;
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
                HideHdr();
                gallery.SetHdrPresentResult(false, false);
                return;
            }

            _presenter ??= new HdrSwapchainPresenter(ScpHdr);
            var scale = XamlRoot?.RasterizationScale ?? 1.0;
            var presented = _presenter.TryPresent(frame, gallery.Scaling, (float)scale);
            if (presented)
            {
                ScpHdr.Visibility = Visibility.Visible;
                ImgStill.Visibility = Visibility.Collapsed;
                gallery.SetHdrPresentResult(true, _presenter.DisplayIsHdr);
            }
            else
            {
                HideHdr();
                gallery.SetHdrPresentResult(false, _presenter.DisplayIsHdr);
            }
        });
    }

    private void HideHdr()
    {
        ScpHdr.Visibility = Visibility.Collapsed;
        ImgStill.Visibility = Visibility.Visible;
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
        if (scrolls)
        {
            ImgStill.Width = double.NaN;
            ImgStill.Height = double.NaN;
            ImgStill.HorizontalAlignment = HorizontalAlignment.Left;
            ImgStill.VerticalAlignment = VerticalAlignment.Top;
        }
        else
        {
            ImgStill.HorizontalAlignment = HorizontalAlignment.Stretch;
            ImgStill.VerticalAlignment = VerticalAlignment.Stretch;
            var w = ScrStill.ActualWidth;
            var h = ScrStill.ActualHeight;
            if (w > 1 && h > 1)
            {
                ImgStill.Width = w;
                ImgStill.Height = h;
            }
        }
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
