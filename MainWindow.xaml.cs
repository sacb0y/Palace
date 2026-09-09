using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Palace;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1200 * scale), (int)(800 * scale)));

        Closed += (_, _) => GalleryWindow.CloseAll();
    }

    public void ShowMain()
    {
        StartupPane.Visibility = Visibility.Collapsed;
        if (RootFrame.Content is null)
        {
            RootFrame.Navigate(typeof(MainPage));
        }
    }
}
