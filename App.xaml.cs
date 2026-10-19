using Microsoft.UI.Xaml;
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
        TryApplyBrandAccent();
        InstallErrorHandling();
    }

    /// <summary>
    /// Optional Palace pink ramp into SystemAccentColor*. Skip HighContrast.
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
            var r = Current.Resources;
            r["SystemAccentColor"] = ToColor(ramp.Base);
            r["SystemAccentColorLight1"] = ToColor(ramp.Light1);
            r["SystemAccentColorLight2"] = ToColor(ramp.Light2);
            r["SystemAccentColorLight3"] = ToColor(ramp.Light3);
            r["SystemAccentColorDark1"] = ToColor(ramp.Dark1);
            r["SystemAccentColorDark2"] = ToColor(ramp.Dark2);
            r["SystemAccentColorDark3"] = ToColor(ramp.Dark3);
        }
        catch
        {
            // Startup must not die on accent apply (sandbox / missing LocalSettings).
        }
    }

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
            log = new ErrorLog(Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, ErrorLog.FileName));
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