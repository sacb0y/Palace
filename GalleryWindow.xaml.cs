using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.UI.Core;
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
            if (e.PropertyName is nameof(GalleryViewModel.CurrentPath)
                or nameof(GalleryViewModel.PreviewImageUri)
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

    private void GalleryRoot_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        if (Gallery.TryHandleScaleShortcut(control, (int)e.Key))
        {
            e.Handled = true;
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
