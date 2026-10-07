using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Palace.Helpers;
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
        // Version in Title (not only Subtitle) — Subtitle alone was easy to miss / looked unchanged.
        AppTitleBar.Title = AppVersion.TitleBarTitle;
        AppTitleBar.Subtitle = AppVersion.TitleBar;
        AppWindow.Title = AppVersion.TitleBarTitle;
        Title = AppVersion.TitleBarTitle;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1200 * scale), (int)(800 * scale)));

        ShellBackground.Changed += ShellBackground_Changed;
        ShellBackground.DisplayReleasing += ShellBackground_DisplayReleasing;
        if (Content is FrameworkElement root)
        {
            root.ActualThemeChanged += (_, _) => ApplyShellBackdrop();
        }

        ApplyShellBackdrop();
        Closed += (_, _) =>
        {
            ShellBackground.Changed -= ShellBackground_Changed;
            ShellBackground.DisplayReleasing -= ShellBackground_DisplayReleasing;
            GalleryWindow.CloseAll();
        };
    }

    private void ShellBackground_Changed(object? sender, EventArgs e) => ApplyShellBackdrop();

    private void ShellBackground_DisplayReleasing(object? sender, EventArgs e) =>
        ShellBackdrop.Release(ImgShellWallpaper);

    private void ApplyShellBackdrop() =>
        ShellBackdrop.Apply(BrdShellGradient, ImgShellWallpaper, BrdShellGlass, BrdShellDim, BrdShellTint);

    public void ShowMain()
    {
        StartupPane.Visibility = Visibility.Collapsed;
        if (RootFrame.Content is null)
        {
            RootFrame.Navigate(typeof(MainPage));
        }
    }

    /// <summary>Non-fatal failure before the shell is ready — stop the spinner and keep the window alive.</summary>
    public void ShowStartupFailure(string message)
    {
        PrgStartup.ShowPaused = true;
        PrgStartup.IsIndeterminate = false;
        TxtStartup.Text = message;
        ShowError(message);
    }

    /// <summary>Safe global Notify for <see cref="Helpers.ErrorReporter"/> (InfoBar; never throws).</summary>
    public void ShowError(string message)
    {
        try
        {
            InfAppError.Message = message;
            InfAppError.IsOpen = true;
        }
        catch
        {
            // Presenter must never crash the app.
        }
    }
}
