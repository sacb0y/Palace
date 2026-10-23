using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Palace.Helpers;
using Palace.Models;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace Palace;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    private AccessibilitySettings? _accessibility;
    private MicaBackdrop? _micaBackdrop;
    private bool _shellApplyQueued;
    private XamlRoot? _minSizeXamlRoot;

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
        ApplyPreferredMinimumSize(scale);

        ShellBackground.Changed += ShellBackground_Changed;
        ShellBackground.LiveAmountsChanged += ShellBackground_LiveAmountsChanged;
        ShellBackground.DisplayReleasing += ShellBackground_DisplayReleasing;
        if (Content is FrameworkElement root)
        {
            // Defer off ActualThemeChanged — recreating SystemBackdrop on that stack AVs (0xc0000005).
            root.ActualThemeChanged += (_, _) =>
            {
                QueueApplyShellBackdrop();
                // ThemeResource may paint the system accent back onto the same brushes.
                root.DispatcherQueue.TryEnqueue(App.ApplySavedBrandAccent);
            };
            // PreferredMinimum* are physical px — refresh when DPI / scale changes.
            root.Loaded += Root_LoadedForMinSize;
        }

        try
        {
            _accessibility = new AccessibilitySettings();
            _accessibility.HighContrastChanged += Accessibility_HighContrastChanged;
        }
        catch
        {
            _accessibility = null;
        }

        ApplyShellBackdrop();
        Closed += (_, _) =>
        {
            ShellBackground.Changed -= ShellBackground_Changed;
            ShellBackground.LiveAmountsChanged -= ShellBackground_LiveAmountsChanged;
            ShellBackground.DisplayReleasing -= ShellBackground_DisplayReleasing;
            if (_minSizeXamlRoot is not null)
            {
                _minSizeXamlRoot.Changed -= XamlRoot_ChangedForMinSize;
                _minSizeXamlRoot = null;
            }

            if (_accessibility is not null)
            {
                _accessibility.HighContrastChanged -= Accessibility_HighContrastChanged;
            }

            GalleryWindow.CloseAll();
        };
    }

    private void Accessibility_HighContrastChanged(AccessibilitySettings sender, object args) =>
        QueueApplyShellBackdrop();

    private void Root_LoadedForMinSize(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement root)
        {
            return;
        }

        root.Loaded -= Root_LoadedForMinSize;
        if (root.XamlRoot is null)
        {
            return;
        }

        _minSizeXamlRoot = root.XamlRoot;
        _minSizeXamlRoot.Changed += XamlRoot_ChangedForMinSize;
        ApplyPreferredMinimumSize(_minSizeXamlRoot.RasterizationScale);
    }

    private void XamlRoot_ChangedForMinSize(XamlRoot sender, XamlRootChangedEventArgs args) =>
        ApplyPreferredMinimumSize(sender.RasterizationScale);

    /// <summary>
    /// PreferredMinimum* are physical pixels — convert DIPs with the current scale.
    /// Floor keeps Library mosaic ≥560 + tree min without clipping (~770 DIP).
    /// </summary>
    private void ApplyPreferredMinimumSize(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            scale = 1;
        }

        if (AppWindow.Presenter is not OverlappedPresenter overlapped)
        {
            return;
        }

        overlapped.PreferredMinimumWidth =
            (int)(LibraryColumnLayout.MinWindowWidthDip * scale);
        overlapped.PreferredMinimumHeight =
            (int)(LibraryColumnLayout.MinWindowHeightDip * scale);
    }

    private void ShellBackground_Changed(object? sender, EventArgs e) => QueueApplyShellBackdrop();

    /// <summary>
    /// Blur/darkness slider ticks — Composition InsertScalar only (shared surface stays).
    /// </summary>
    private void ShellBackground_LiveAmountsChanged(object? sender, EventArgs e)
    {
        try
        {
            ShellBackdrop.SyncLiveAmounts();
        }
        catch
        {
            QueueApplyShellBackdrop();
        }
    }

    private void ShellBackground_DisplayReleasing(object? sender, EventArgs e) =>
        ShellBackdrop.Release(ImgShellWallpaper);

    private void QueueApplyShellBackdrop()
    {
        if (_shellApplyQueued)
        {
            return;
        }

        _shellApplyQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            _shellApplyQueued = false;
            ApplyShellBackdrop();
        });
    }

    private void ApplyShellBackdrop()
    {
        try
        {
            var useMica = ShellBackdrop.Apply(
                BrdShellGradient, ImgShellWallpaper, BrdShellGlass, BrdShellDim, BrdShellTint);
            ApplySystemBackdrop(useMica);
        }
        catch
        {
            // Painted layers / backdrop must never take down the process. Prefer Default paint.
            try
            {
                SystemBackdrop = null;
                _micaBackdrop = null;
            }
            catch
            {
                // ignore
            }
        }
    }

    private void ApplySystemBackdrop(bool useMica)
    {
        var micaSupported = MicaController.IsSupported();
        var want = useMica && micaSupported;
        var has = _micaBackdrop is not null && ReferenceEquals(SystemBackdrop, _micaBackdrop);

        if (!want)
        {
            if (SystemBackdrop is not null)
            {
                SystemBackdrop = null;
            }

            _micaBackdrop = null;
            return;
        }

        var kind = ShellBackground.Preset == ShellPresetKind.MicaAlt
            ? MicaKind.BaseAlt
            : MicaKind.Base;

        // Keep the same MicaBackdrop across theme flips — new SystemBackdrop during
        // ActualThemeChanged crashes WinUI (Microsoft.UI.Xaml.dll AV 0xc0000005).
        if (_micaBackdrop is null)
        {
            _micaBackdrop = new MicaBackdrop { Kind = kind };
            SystemBackdrop = _micaBackdrop;
            return;
        }

        if (_micaBackdrop.Kind != kind)
        {
            _micaBackdrop.Kind = kind;
        }

        if (!has)
        {
            SystemBackdrop = _micaBackdrop;
        }
    }

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
