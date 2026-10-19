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

    /// <summary>Merged into <see cref="XamlControlsResources"/> when Palace pink is on.</summary>
    private static ResourceDictionary? _brandAccentDictionary;

    public App()
    {
        InitializeComponent();
        // Must finish before OnLaunched creates the first window. Overrides go inside
        // XamlControlsResources.MergedDictionaries — Application-level ThemeDictionaries
        // alone do not win over WinUI Accent*Brush (microsoft-ui-xaml #4279 / #6394).
        TryApplyBrandAccent();
        InstallErrorHandling();
    }

    /// <summary>
    /// Optional Palace pink: SystemAccentColor* Color keys + Accent*Brush recreation
    /// inside XamlControlsResources so Nav selection indicator and tree fills rebind.
    /// Skip HighContrast. Changing the setting needs a restart.
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
            var xcr = FindXamlControlsResources(Current.Resources);
            if (xcr is null)
            {
                return;
            }

            if (_brandAccentDictionary is not null)
            {
                xcr.MergedDictionaries.Remove(_brandAccentDictionary);
            }

            _brandAccentDictionary = BuildBrandAccentDictionary(ramp);
            // Last merged dictionary wins ThemeResource lookups inside XCR.
            xcr.MergedDictionaries.Add(_brandAccentDictionary);

            // Root Color keys as a fallback for non-theme lookups.
            WriteSystemAccentColors(Current.Resources, ramp);
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

    private static ResourceDictionary BuildBrandAccentDictionary(BrandAccent.Ramp ramp)
    {
        var root = new ResourceDictionary();
        WriteSystemAccentColors(root, ramp);

        // Default + Dark: WASDK 2.x theme keys vary; cover both.
        WriteThemeAccent(root, "Default", BrandAccent.ForDarkTheme(ramp), ramp, lightPane: false);
        WriteThemeAccent(root, "Dark", BrandAccent.ForDarkTheme(ramp), ramp, lightPane: false);
        WriteThemeAccent(root, "Light", BrandAccent.ForLightTheme(ramp), ramp, lightPane: true);
        return root;
    }

    private static void WriteThemeAccent(
        ResourceDictionary root,
        string themeKey,
        BrandAccent.ThemeAccentColors colors,
        BrandAccent.Ramp ramp,
        bool lightPane)
    {
        var theme = new ResourceDictionary();
        WriteSystemAccentColors(theme, ramp);
        WriteAccentBrushes(theme, colors);

        var fill = ToColor(colors.Fill);
        // Nav selection pill uses this key (StaticResource → AccentFill by default).
        theme["NavigationViewSelectionIndicatorForeground"] = Solid(fill, 1.0);

        theme["PalacePaneFillBrush"] = new AcrylicBrush
        {
            FallbackColor = lightPane
                ? Color.FromArgb(255, 0xF3, 0xF3, 0xF3)
                : Color.FromArgb(255, 0x2C, 0x2C, 0x2C),
            TintColor = lightPane ? ToColor(ramp.Light3) : ToColor(ramp.Dark3),
            TintLuminosityOpacity = lightPane ? 0.9 : 0.85,
            TintOpacity = 0.08,
        };

        root.ThemeDictionaries[themeKey] = theme;
    }

    private static void WriteSystemAccentColors(ResourceDictionary dict, BrandAccent.Ramp ramp)
    {
        dict["SystemAccentColor"] = ToColor(ramp.Base);
        dict["SystemAccentColorLight1"] = ToColor(ramp.Light1);
        dict["SystemAccentColorLight2"] = ToColor(ramp.Light2);
        dict["SystemAccentColorLight3"] = ToColor(ramp.Light3);
        dict["SystemAccentColorDark1"] = ToColor(ramp.Dark1);
        dict["SystemAccentColorDark2"] = ToColor(ramp.Dark2);
        dict["SystemAccentColorDark3"] = ToColor(ramp.Dark3);
    }

    private static void WriteAccentBrushes(
        ResourceDictionary theme,
        BrandAccent.ThemeAccentColors colors)
    {
        var fill = ToColor(colors.Fill);
        var accentText = ToColor(colors.AccentText);
        var accentTextSecondary = ToColor(colors.AccentTextSecondary);
        var textOn = ToColor(colors.TextOnAccent);

        theme["AccentFillColorDefaultBrush"] = Solid(fill, 1.0);
        theme["AccentFillColorSecondaryBrush"] = Solid(fill, 0.9);
        theme["AccentFillColorTertiaryBrush"] = Solid(fill, 0.8);
        theme["AccentTextFillColorPrimaryBrush"] = Solid(accentText, 1.0);
        theme["AccentTextFillColorSecondaryBrush"] = Solid(accentTextSecondary, 1.0);
        theme["AccentTextFillColorTertiaryBrush"] = Solid(accentTextSecondary, 0.6);
        theme["TextOnAccentFillColorPrimaryBrush"] = Solid(textOn, 1.0);
        theme["TextOnAccentFillColorSecondaryBrush"] = Solid(textOn, 0.5);
        theme["TextOnAccentFillColorDisabledBrush"] = Solid(textOn, 0.2);
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
