using Microsoft.UI.Xaml.Controls;
using Palace.Helpers;
using Palace.Services;
using Palace.Services.Cloud;
using Palace.ViewModels;
using Windows.UI;

namespace Palace.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _ignoreTintChanges;

    public SettingsViewModel ViewModel => AppServices.Settings;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            ViewModel.RequestPickCloudFolder = PickCloudFolderAsync;
            await ErrorReporter.RunAsync("Load settings", null, ViewModel.LoadAsync);
            SyncTintPicker();
        };
        Unloaded += (_, _) =>
        {
            ViewModel.RequestPickCloudFolder = null;
            ViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        };
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    private void ViewModelOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsViewModel.TintHex) or nameof(SettingsViewModel.TintEnabled))
        {
            SyncTintPicker();
        }
    }

    private void SyncTintPicker()
    {
        if (!TagColor.TryParse(ViewModel.TintHex, out var color)
            && !TagColor.TryParse(ShellBackground.DefaultTintHex, out color))
        {
            color = Color.FromArgb(0x50, 0x2C, 0x3A, 0x6B);
        }

        _ignoreTintChanges = true;
        PkrShellTint.Color = color;
        DispatcherQueue.TryEnqueue(() => _ignoreTintChanges = false);
    }

    private void PkrShellTint_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_ignoreTintChanges)
        {
            return;
        }

        ViewModel.TintHex = TagColor.ToHex(args.NewColor);
    }

    private async Task<CloudEntry?> PickCloudFolderAsync(ICloudLibrary library)
    {
        var dialog = new CloudFolderPickerDialog(library, AppServices.CurrentProject.Name)
        {
            XamlRoot = XamlRoot
        };
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? dialog.Result : null;
    }
}
