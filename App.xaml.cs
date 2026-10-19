using Microsoft.UI.Xaml;
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

    public App()
    {
        InitializeComponent();
        // Ramp + brush recreate must finish before OnLaunched creates the first window —
        // XamlControlsResources already resolved Accent*Brush from system colors.
        TryApplyBrandAccent();
        InstallErrorHandling();
    }

    /// <summary>
    /// Optional Palace pink ramp into SystemAccentColor* and recreated Accent*Brush
    /// resources so WinUI ThemeResource consumers rebind. Skip HighContrast.
    /// Resource lookups are cached — changing the setting needs a restart.
    /// </summary>
    private static void TryApplyBrandAccent()
    {
        try
        {
            if (new AccessibilitySettings().HighContrast)
            {
                return;
            }

            var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            if (!BrandAccent.IsPalacePink(values[BrandAccent.SettingsKey]))
            {
                return;
            }

            var ramp = BrandAccent.BuildRamp();
            ApplySystemAccentColors(Current.Resources, ramp);
            RecreateAccentBrushes(Current.Resources, ramp);
        }
        catch
        {
            // Startup must not die on accent apply (sandbox / missing LocalSettings).
        }
    }

    private static void ApplySystemAccentColors(ResourceDictionary root, BrandAccent.Ramp ramp)
    {
        root["SystemAccentColor"] = ToColor(ramp.Base);
        root["SystemAccentColorLight1"] = ToColor(ramp.Light1);
        root["SystemAccentColorLight2"] = ToColor(ramp.Light2);
        root["SystemAccentColorLight3"] = ToColor(ramp.Light3);
        root["SystemAccentColorDark1"] = ToColor(ramp.Dark1);
        root["SystemAccentColorDark2"] = ToColor(ramp.Dark2);
        root["SystemAccentColorDark3"] = ToColor(ramp.Dark3);

        // ThemeDictionaries may also hold Color keys; override so pane TintColor rebinds.
        foreach (var key in new[] { "Default", "Light" })
        {
            if (root.ThemeDictionaries.TryGetValue(key, out var theme) && theme is ResourceDictionary dict)
            {
                dict["SystemAccentColor"] = ToColor(ramp.Base);
                dict["SystemAccentColorLight1"] = ToColor(ramp.Light1);
                dict["SystemAccentColorLight2"] = ToColor(ramp.Light2);
                dict["SystemAccentColorLight3"] = ToColor(ramp.Light3);
                dict["SystemAccentColorDark1"] = ToColor(ramp.Dark1);
                dict["SystemAccentColorDark2"] = ToColor(ramp.Dark2);
                dict["SystemAccentColorDark3"] = ToColor(ramp.Dark3);
            }
        }
    }

    /// <summary>
    /// Recreate Accent* brushes in Application ThemeDictionaries (ThemeResource override)
    /// and mutate matching brushes inside merged XamlControlsResources so already-held
    /// SolidColorBrush references update in place.
    /// </summary>
    private static void RecreateAccentBrushes(ResourceDictionary root, BrandAccent.Ramp ramp)
    {
        ApplyThemeAccent(root, "Default", BrandAccent.ForDarkTheme(ramp), recreate: true);
        ApplyThemeAccent(root, "Light", BrandAccent.ForLightTheme(ramp), recreate: true);

        if (root.ThemeDictionaries.TryGetValue("Default", out var darkObj)
            && darkObj is ResourceDictionary dark)
        {
            RefreshPalacePaneBrush(
                dark,
                tint: ToColor(ramp.Dark3),
                fallback: Color.FromArgb(255, 0x2C, 0x2C, 0x2C),
                tintLuminosityOpacity: 0.85);
        }

        if (root.ThemeDictionaries.TryGetValue("Light", out var lightObj)
            && lightObj is ResourceDictionary light)
        {
            RefreshPalacePaneBrush(
                light,
                tint: ToColor(ramp.Light3),
                fallback: Color.FromArgb(255, 0xF3, 0xF3, 0xF3),
                tintLuminosityOpacity: 0.9);
        }

        foreach (var merged in root.MergedDictionaries)
        {
            PatchMergedAccentBrushes(merged, ramp);
        }
    }

    private static void PatchMergedAccentBrushes(ResourceDictionary dict, BrandAccent.Ramp ramp)
    {
        ApplyThemeAccent(dict, "Default", BrandAccent.ForDarkTheme(ramp), recreate: false);
        ApplyThemeAccent(dict, "Light", BrandAccent.ForLightTheme(ramp), recreate: false);
        foreach (var nested in dict.MergedDictionaries)
        {
            PatchMergedAccentBrushes(nested, ramp);
        }
    }

    private static void ApplyThemeAccent(
        ResourceDictionary root,
        string themeKey,
        BrandAccent.ThemeAccentColors colors,
        bool recreate)
    {
        if (!root.ThemeDictionaries.TryGetValue(themeKey, out var themeObj)
            || themeObj is not ResourceDictionary theme)
        {
            if (!recreate)
            {
                return;
            }

            theme = new ResourceDictionary();
            root.ThemeDictionaries[themeKey] = theme;
        }

        WriteAccentBrushes(theme, colors, recreate);
    }

    private static void WriteAccentBrushes(
        ResourceDictionary theme,
        BrandAccent.ThemeAccentColors colors,
        bool recreate)
    {
        var fill = ToColor(colors.Fill);
        var accentText = ToColor(colors.AccentText);
        var accentTextSecondary = ToColor(colors.AccentTextSecondary);
        var textOn = ToColor(colors.TextOnAccent);

        SetBrush(theme, "AccentFillColorDefaultBrush", fill, 1.0, recreate);
        SetBrush(theme, "AccentFillColorSecondaryBrush", fill, 0.9, recreate);
        SetBrush(theme, "AccentFillColorTertiaryBrush", fill, 0.8, recreate);
        SetBrush(theme, "AccentTextFillColorPrimaryBrush", accentText, 1.0, recreate);
        SetBrush(theme, "AccentTextFillColorSecondaryBrush", accentTextSecondary, 1.0, recreate);
        SetBrush(theme, "AccentTextFillColorTertiaryBrush", accentTextSecondary, 0.6, recreate);
        SetBrush(theme, "TextOnAccentFillColorPrimaryBrush", textOn, 1.0, recreate);
        SetBrush(theme, "TextOnAccentFillColorSecondaryBrush", textOn, 0.5, recreate);
        SetBrush(theme, "TextOnAccentFillColorDisabledBrush", textOn, 0.2, recreate);
    }

    private static void SetBrush(
        ResourceDictionary theme,
        string key,
        Color color,
        double opacity,
        bool recreate)
    {
        if (!recreate
            && theme.TryGetValue(key, out var existing)
            && existing is SolidColorBrush brush)
        {
            brush.Color = color;
            brush.Opacity = opacity;
            return;
        }

        theme[key] = Solid(color, opacity);
    }

    private static void RefreshPalacePaneBrush(
        ResourceDictionary theme,
        Color tint,
        Color fallback,
        double tintLuminosityOpacity)
    {
        theme["PalacePaneFillBrush"] = new AcrylicBrush
        {
            FallbackColor = fallback,
            TintColor = tint,
            TintLuminosityOpacity = tintLuminosityOpacity,
            TintOpacity = 0.08,
        };
    }

    private static SolidColorBrush Solid(Color color, double opacity) =>
        new() { Color = color, Opacity = opacity };

    private static Color ToColor(BrandAccent.Rgb rgb) =>
        Color.FromArgb(255, rgb.R, rgb.G, rgb.B);

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
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
