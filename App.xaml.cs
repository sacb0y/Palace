using Microsoft.UI.Xaml;
using Palace.Services;

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
    }

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
        catch (Exception ex)
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

            throw;
        }
    }
}
