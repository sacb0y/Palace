using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Palace.Controls;
using Windows.UI.Core;
using Palace.Helpers;
using Palace.ViewModels;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;

using Palace.Services;
namespace Palace;

public sealed partial class GalleryWindow : Window
{
    private static readonly List<GalleryWindow> OpenWindows = [];

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    public GalleryViewModel Gallery { get; }

    private Border? _chipFadeOld;
    private TextBlock? _chipFadeOldTitle;
    private TextBlock? _chipFadeOldPos;

    public GalleryWindow(GalleryViewModel gallery)
    {
        Gallery = gallery;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(GalleryTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var desiredW = (int)(1280 * scale);
        var desiredH = (int)(840 * scale);
        try
        {
            var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            (desiredW, desiredH) = ViewerMediaChrome.ClampGalleryWindowSize(
                desiredW, desiredH, work.Width, work.Height);
        }
        catch
        {
            // DisplayArea can fail early; still Resize to the DIP-scaled default.
        }

        AppWindow.Resize(new SizeInt32(desiredW, desiredH));
        ViewerMediaElement.SuppressFullWindow(MpeGallery);
        CardViewerDetails.AttachInFlow(GrdGalleryWindow);
        SrfWindowStill.ReviewSwipeCommitted += OnWindowStillSwipe;
        SrfWindowStill.ChipCrossFadeRequested += OnWindowChipCrossFade;

        Gallery.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GalleryViewModel.StillRevision)
                or nameof(GalleryViewModel.IsVideo)
                or nameof(GalleryViewModel.IsImage))
            {
                UpdateMedia();
            }

            if (e.PropertyName is nameof(GalleryViewModel.LoopVideo))
            {
                ApplyLoop();
            }
        };

        OpenWindows.Add(this);
        Closed += (_, _) =>
        {
            OpenWindows.Remove(this);
            Gallery.StopGifPlayback();
            SrfWindowStill.ReviewSwipeCommitted -= OnWindowStillSwipe;
            SrfWindowStill.ChipCrossFadeRequested -= OnWindowChipCrossFade;
            SrfWindowStill.Bind(null);
            MpeGallery.Source = null;
            VolGallery.VolumeChanged -= GalleryVolume_Changed;
        };

        VolGallery.VolumeChanged += GalleryVolume_Changed;
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

    private ViewerFocusZone _viewerFocusZone = ViewerFocusZone.Image;

    private void GalleryRoot_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);
        var alt = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)
            .HasFlag(CoreVirtualKeyStates.Down);
        if (Gallery.TryHandleViewerShortcut(control, alt, (int)e.Key))
        {
            if (GalleryScale.TogglesDetails(control, alt, (int)e.Key))
            {
                if (Gallery.ShowDetails)
                {
                    CardViewerDetails.FocusCard();
                    _viewerFocusZone = ViewerFocusZone.Details;
                }
                else
                {
                    BtnGalleryWindowImageInfo.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                    _viewerFocusZone = ViewerFocusZone.Controls;
                }
            }

            e.Handled = true;
            return;
        }

        if (GalleryScale.CyclesViewerFocus((int)e.Key))
        {
            CycleViewerFocus();
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

        if (GalleryVolume.PassesVolumeFocusKeys(VolGallery.OwnsFocus(e.OriginalSource), (int)e.Key))
        {
            return;
        }

        if (VolGallery.TryHandleViewerKey(
                Gallery.IsVideo,
                FocusIsTextInput(e.OriginalSource),
                shift,
                e.Key))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Escape:
                if (Gallery.TryCloseDetailsFirst())
                {
                    BtnGalleryWindowImageInfo.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                    _viewerFocusZone = ViewerFocusZone.Controls;
                    e.Handled = true;
                    break;
                }

                Close();
                e.Handled = true;
                break;
            case VirtualKey.Left:
                if (ViewerFocusCycle.AllowsArrowNavigate(_viewerFocusZone))
                {
                    Gallery.GoPreviousCommand.Execute(null);
                    e.Handled = true;
                }

                break;
            case VirtualKey.Right:
                if (ViewerFocusCycle.AllowsArrowNavigate(_viewerFocusZone))
                {
                    Gallery.GoNextCommand.Execute(null);
                    e.Handled = true;
                }

                break;
        }
    }

    private void CycleViewerFocus()
    {
        _viewerFocusZone = ViewerFocusCycle.Next(
            _viewerFocusZone,
            Gallery.ShowDetails);
        switch (_viewerFocusZone)
        {
            case ViewerFocusZone.Controls:
                BtnGalleryWindowImageInfo.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                break;
            case ViewerFocusZone.Details:
                CardViewerDetails.FocusCard();
                break;
            default:
                GrdGalleryWindow.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                break;
        }
    }

    private static bool FocusIsTextInput(object? source)
    {
        for (var current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Microsoft.UI.Xaml.Controls.TextBox
                or Microsoft.UI.Xaml.Controls.RichEditBox
                or Microsoft.UI.Xaml.Controls.PasswordBox
                or Microsoft.UI.Xaml.Controls.AutoSuggestBox)
            {
                return true;
            }
        }

        return false;
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
            var wasMaximized = ViewerMediaElement.IsPresenterMaximized(AppWindow);
            try
            {
                if (MpeGallery.MediaPlayer is null)
                {
                    MpeGallery.SetMediaPlayer(new MediaPlayer());
                }

                ViewerMediaElement.SuppressFullWindow(MpeGallery);
                ApplyLoop();
                ApplyVolume();
                VolGallery.AttachTransport(MpeGallery.TransportControls);
                VolGallery.SyncFromStore();
                var file = await StorageFile.GetFileFromPathAsync(Gallery.CurrentPath);
                if (epoch != _mediaEpoch)
                {
                    return;
                }

                MpeGallery.Source = MediaSource.CreateFromStorageFile(file);
                ViewerMediaElement.SuppressFullWindow(MpeGallery);
                ViewerMediaElement.RestoreIfVideoMaximized(AppWindow, wasMaximized);
                HookVideoBrowseSwipe();
            }
            catch
            {
                if (epoch == _mediaEpoch)
                {
                    MpeGallery.Source = null;
                    SyncVideoBrowseSwipeHitTest();
                }
            }
        }
        else
        {
            MpeGallery.Source = null;
            SyncVideoBrowseSwipeHitTest();
        }
    }

    private void ApplyLoop()
    {
        var player = MpeGallery.MediaPlayer;
        if (player is null)
        {
            return;
        }

        player.IsLoopingEnabled = Gallery.LoopVideo;
    }

    private void ApplyVolume() =>
        Controls.VolumeControl.ApplyToPlayer(MpeGallery.MediaPlayer);

    private void GalleryVolume_Changed(object? sender, EventArgs e) => ApplyVolume();

    private VideoBrowseSwipeBinder? _videoBrowseSwipe;

    private void OnWindowStillSwipe(TouchGestures.SwipeAction action)
    {
        switch (action)
        {
            case TouchGestures.SwipeAction.BrowsePrevious:
                Gallery.GoPreviousCommand.Execute(null);
                break;
            case TouchGestures.SwipeAction.BrowseNext:
                Gallery.GoNextCommand.Execute(null);
                break;
        }
    }

    /// <summary>
    /// B4: true old→new title/position cross-fade at pager commit start
    /// (docs/specs/swipe-nav.md). Snapshot runs before navigation.
    /// </summary>
    private void OnWindowChipCrossFade()
    {
        if (!ViewerSettings.TouchGesturesEnabled)
        {
            return;
        }

        EnsureChipFadeOverlay();
        PagerChipCrossFade.Begin(
            DispatcherQueue,
            GalleryTitleBar,
            _chipFadeOld!,
            _chipFadeOldTitle!,
            _chipFadeOldPos!,
            Gallery.Title,
            Gallery.PositionLabel);
    }

    private void EnsureChipFadeOverlay()
    {
        if (_chipFadeOld is not null)
        {
            return;
        }

        _chipFadeOld = PagerChipCrossFade.CreateOldOverlay(out _chipFadeOldTitle!, out _chipFadeOldPos!);
        // Float over the gallery content under the title bar.
        if (GrdGalleryWindow.Children is { } children)
        {
            Grid.SetRow(_chipFadeOld, 1);
            children.Add(_chipFadeOld);
        }
    }

    private void HookVideoBrowseSwipe()
    {
        _videoBrowseSwipe ??= new VideoBrowseSwipeBinder(
            MpeGallery,
            VideoBrowseSwipeAllowedNow,
            OnWindowStillSwipe);
        _videoBrowseSwipe.Hook();

        var session = MpeGallery.MediaPlayer?.PlaybackSession;
        if (session is not null)
        {
            session.PlaybackStateChanged -= GalleryPlayback_StateChanged;
            session.PlaybackStateChanged += GalleryPlayback_StateChanged;
        }

        SyncVideoBrowseSwipeHitTest();
    }

    private void GalleryPlayback_StateChanged(MediaPlaybackSession sender, object args) =>
        DispatcherQueue.TryEnqueue(SyncVideoBrowseSwipeHitTest);

    private bool VideoBrowseSwipeAllowedNow()
    {
        // Playing OK — binder routes scrub-bar vs page (docs/specs/swipe-nav.md).
        return Gallery.IsVideo && ViewerSettings.TouchGesturesEnabled;
    }

    private void SyncVideoBrowseSwipeHitTest() => _videoBrowseSwipe?.SyncAllowed();

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
                    Microsoft.UI.Xaml.Media.Imaging.BitmapImage? image = null;
                    try
                    {
                        image = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage
                        {
                            CreateOptions = Microsoft.UI.Xaml.Media.Imaging.BitmapCreateOptions.IgnoreImageCache,
                            UriSource = new Uri(info.Value.Path, UriKind.Absolute)
                        };
                    }
                    catch
                    {
                        image = null;
                    }

                    Gallery.ApplyMosaicThumb(info.Value.Path, image);
                });
            });
    }

}
