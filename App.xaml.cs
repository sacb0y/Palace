using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Palace.Helpers;
using Palace.Services;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Palace;

public partial class App : Application
{
    public static MainWindow Window { get; private set; } = null!;

    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>System color captured the first time a brush is retinted, so Follow Windows can put it back.</summary>
    private static readonly Dictionary<SolidColorBrush, (Color Color, double Opacity)> _systemBrushes = new();

    public App()
    {
        InitializeComponent();
        // Packaged builds throw E_UNEXPECTED (0x8000FFFF) from Application.Resources in
        // this constructor. The old apply call caught that and never retinted anything.
        // OnLaunched applies the accent before the first window, once Resources is live.
        InstallErrorHandling();
    }

    /// <summary>
    /// Palace pink retints the SolidColorBrush instances inside XamlControlsResources
    /// theme dictionaries (Default / Dark / Light). Skip HighContrast. Safe to call
    /// again after the accent setting or the app theme changes.
    /// </summary>
    public static void ApplySavedBrandAccent() => ApplySavedBrandAccent(Current.Resources);

    private static void ApplySavedBrandAccent(ResourceDictionary resources)
    {
        try
        {
            if (new AccessibilitySettings().HighContrast)
            {
                return;
            }

            var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            var xcr = FindXamlControlsResources(resources);
            if (xcr is null)
            {
                return;
            }

            if (!BrandAccent.IsPalacePink(values[BrandAccent.SettingsKey]))
            {
                RestoreSystemBrushes();
                return;
            }

            var ramp = BrandAccent.BuildRamp();
            RetintAccentTree(xcr, ramp);
            var active = Current.RequestedTheme == ApplicationTheme.Light
                ? BrandAccent.ForLightTheme(ramp)
                : BrandAccent.ForDarkTheme(ramp);
            RetintLookup(resources, active);
        }
        catch
        {
            // Startup must not die on accent apply (sandbox / missing LocalSettings).
        }
    }

    private static XamlControlsResources? FindXamlControlsResources(ResourceDictionary root)
    {
        foreach (var merged in root.MergedDictionaries)
        {
            if (merged is XamlControlsResources xcr)
            {
                return xcr;
            }

            var nested = FindXamlControlsResources(merged);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static int RetintAccentTree(ResourceDictionary dict, BrandAccent.Ramp ramp)
    {
        var count = RetintTheme(dict, "Default", BrandAccent.ForDarkTheme(ramp), ramp, lightPane: false);
        count += RetintTheme(dict, "Dark", BrandAccent.ForDarkTheme(ramp), ramp, lightPane: false);
        count += RetintTheme(dict, "Light", BrandAccent.ForLightTheme(ramp), ramp, lightPane: true);
        foreach (var merged in dict.MergedDictionaries)
        {
            count += RetintAccentTree(merged, ramp);
        }

        return count;
    }

    private static int RetintTheme(
        ResourceDictionary dict,
        string themeKey,
        BrandAccent.ThemeAccentColors colors,
        BrandAccent.Ramp ramp,
        bool lightPane)
    {
        if (!dict.ThemeDictionaries.TryGetValue(themeKey, out var themeObj)
            || themeObj is not ResourceDictionary theme)
        {
            return 0;
        }

        var painted = 0;
        foreach (var key in BrandAccent.AccentBrushKeys)
        {
            if (!BrandAccent.TryBrushPaint(key, colors, out var rgb, out var opacity))
            {
                continue;
            }

            if (RetintBrush(theme, key, ToColor(rgb), opacity))
            {
                painted++;
            }
        }

        if (theme.TryGetValue("PalacePaneFillBrush", out var pane) && pane is AcrylicBrush acrylic)
        {
            acrylic.TintColor = lightPane ? ToColor(ramp.Light3) : ToColor(ramp.Dark3);
        }

        return painted;
    }

    private static int RetintLookup(ResourceDictionary root, BrandAccent.ThemeAccentColors colors)
    {
        var painted = 0;
        foreach (var key in BrandAccent.AccentBrushKeys)
        {
            if (!BrandAccent.TryBrushPaint(key, colors, out var rgb, out var opacity))
            {
                continue;
            }

            if (!root.TryGetValue(key, out var existing) || existing is not SolidColorBrush brush)
            {
                continue;
            }

            RememberAndPaint(brush, ToColor(rgb), opacity);
            painted++;
        }

        return painted;
    }

    private static bool RetintBrush(ResourceDictionary theme, string key, Color color, double opacity)
    {
        // Replace-by-key loses: controls and StaticResource aliases already hold this instance.
        if (!theme.TryGetValue(key, out var existing) || existing is not SolidColorBrush brush)
        {
            return false;
        }

        RememberAndPaint(brush, color, opacity);
        return true;
    }

    private static void RememberAndPaint(SolidColorBrush brush, Color color, double opacity)
    {
        if (!_systemBrushes.ContainsKey(brush))
        {
            _systemBrushes[brush] = (brush.Color, brush.Opacity);
        }

        brush.Color = color;
        brush.Opacity = opacity;
    }

    private static void RestoreSystemBrushes()
    {
        foreach (var (brush, saved) in _systemBrushes)
        {
            brush.Color = saved.Color;
            brush.Opacity = saved.Opacity;
        }
    }

    private static Color ToColor(BrandAccent.Rgb rgb) =>
        Color.FromArgb(255, rgb.R, rgb.G, rgb.B);

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        // Application.Resources throws E_UNEXPECTED in the App constructor on a
        // packaged build, so the accent has to be applied here, before the window.
        ApplySavedBrandAccent(Resources);
        Window = new MainWindow();
        Window.Activate();
        try
        {
            await AppServices.InitializeAsync();
            Window.ShowMain();
            await AppServices.LoadInitialDataAsync();
        }
        catch (Exception ex) when (!ErrorReporter.IsFatal(ex))
        {
            try
            {
                var log = Path.Combine(
                    Windows.Storage.ApplicationData.Current.LocalFolder.Path,
                    "startup-error.txt");
                File.WriteAllText(log, ex.ToString());
            }
            catch
            {
                // Best-effort diagnostics only.
            }

            ErrorReporter.LogOnly("Startup", ex);
            Window.ShowStartupFailure(ErrorReporter.UserMessage("Palace startup", ex));
        }
    }

    private void InstallErrorHandling()
    {
        ErrorLog? log = null;
        try
        {
            var local = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            log = new ErrorLog(Path.Combine(local, ErrorLog.FileName));
            ThumbTimingLog.Configure(Path.Combine(local, ThumbTimingLog.FileName));
        }
        catch
        {
            // Unpackaged/diagnostic hosts have no LocalFolder; handlers below still keep the app alive.
        }

        ErrorReporter.Configure(log, ShowGlobalError);

        UnhandledException += (_, e) =>
        {
            if (e.Exception is null || ErrorReporter.IsFatal(e.Exception))
            {
                if (e.Exception is not null)
                {
                    ErrorReporter.LogOnly("Application.UnhandledException (fatal)", e.Exception);
                }

                return;
            }

            e.Handled = true;
            ErrorReporter.ReportUnhandled("Application.UnhandledException", e.Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            foreach (var inner in e.Exception.Flatten().InnerExceptions)
            {
                ErrorReporter.ReportUnhandled("TaskScheduler.UnobservedTaskException", inner, notifyUser: false);
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                ErrorReporter.LogOnly(
                    e.IsTerminating ? "AppDomain.UnhandledException (terminating)" : "AppDomain.UnhandledException",
                    ex);
            }
        };
    }

    private static void ShowGlobalError(string message)
    {
        var queue = DispatcherQueue;
        var window = Window;
        if (queue is null || window is null)
        {
            return;
        }

        if (queue.HasThreadAccess)
        {
            window.ShowError(message);
        }
        else
        {
            queue.TryEnqueue(() => window.ShowError(message));
        }
    }
}
