using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Core;
using Palace.Helpers;
using Palace.Pages;
using Palace.Services;
using Palace.ViewModels;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;

namespace Palace;

public sealed partial class GalleryWindow : Window
{
    private static readonly List<GalleryWindow> OpenWindows = [];

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    public GalleryViewModel Gallery { get; }

    public GalleryWindow(GalleryViewModel gallery)
    {
        Gallery = gallery;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(GalleryTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1280 * scale), (int)(840 * scale)));

        Gallery.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GalleryViewModel.StillRevision)
                or nameof(GalleryViewModel.IsVideo)
                or nameof(GalleryViewModel.IsImage))
            {
                UpdateMedia();
            }
        };

        OpenWindows.Add(this);
        Closed += (_, _) =>
        {
            OpenWindows.Remove(this);
            Gallery.StopGifPlayback();
            SrfWindowStill.Bind(null);
            MpeGallery.Source = null;
        };

        Activated += (_, _) => GrdGalleryWindow.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        UpdateMedia();
    }

    public static void Show(GalleryViewModel gallery)
    {
        var window = new GalleryWindow(gallery);
        window.Activate();
    }

    public static void CloseAll()
    {
        foreach (var window in OpenWindows.ToArray())
        {
            window.Close();
        }
    }

    private void BtnGalleryWindowClose_Click(object sender, RoutedEventArgs e) => Close();

    private async void BtnGalleryWindowSetThumb_Click(object sender, RoutedEventArgs e)
    {
        if (!Gallery.CanSetVideoThumb)
        {
            return;
        }

        await ErrorReporter.RunAsync(
            "Set mosaic thumbnail",
            null,
            async () =>
            {
                if (MpeGallery.MediaPlayer is null)
                {
                    MpeGallery.SetMediaPlayer(new MediaPlayer());
                }

                var item = Gallery.Current;
                var path = Gallery.CurrentPath;
                var hash = item?.ContentHash;
                if (item is null || string.IsNullOrEmpty(path) || string.IsNullOrEmpty(hash))
                {
                    throw new InvalidOperationException(Gallery.SetVideoThumbTooltip);
                }

                var info = await VideoFrameThumb.CaptureMosaicAsync(
                    MpeGallery.MediaPlayer,
                    path,
                    hash,
                    AppServices.Thumbnails,
                    item.Width,
                    item.Height);
                if (info is null)
                {
                    throw new InvalidOperationException("Could not capture a frame from this video.");
                }

                await UiDispatch.RunAsync(() =>
                {
                    var image = LibraryPage.FileToImage(info.Value.Path, ignoreImageCache: true);
                    Gallery.ApplyMosaicThumb(info.Value.Path, image);
                });
            });
    }

    private void GalleryRoot_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        if (Gallery.TryHandleViewerShortcut(control, (int)e.Key))
        {
            e.Handled = true;
            return;
        }

        if (GalleryScale.PassesViewerSpace(true, (int)e.Key))
        {
            return;
        }

        if (GifFrames.PassesGifSliderArrows(FocusIsGifSlider(e.OriginalSource), (int)e.Key))
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Escape:
                Close();
                e.Handled = true;
                break;
            case VirtualKey.Left:
                Gallery.GoPreviousCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Right:
                Gallery.GoNextCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private static bool FocusIsGifSlider(object? source)
    {
        for (var current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement element
                && GifFrames.IsGifFrameSlider(AutomationProperties.GetAutomationId(element)))
            {
                return true;
            }
        }

        return false;
    }

    private int _mediaEpoch;

    private async void UpdateMedia()
    {
        var epoch = Interlocked.Increment(ref _mediaEpoch);
        SrfWindowStill.Bind(Gallery.IsImage ? Gallery : null);
        if (Gallery.IsVideo && Gallery.CurrentPath is not null)
        {
            try
            {
                if (MpeGallery.MediaPlayer is null)
                {
                    MpeGallery.SetMediaPlayer(new MediaPlayer());
                }

                var file = await StorageFile.GetFileFromPathAsync(Gallery.CurrentPath);
                if (epoch != _mediaEpoch)
                {
                    return;
                }

                MpeGallery.Source = MediaSource.CreateFromStorageFile(file);
            }
            catch
            {
                if (epoch == _mediaEpoch)
                {
                    MpeGallery.Source = null;
                }
            }
        }
        else
        {
            MpeGallery.Source = null;
        }
    }
}
