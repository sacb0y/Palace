using Microsoft.UI.Xaml.Controls;
using Palace.Services;
using Palace.Services.Cloud;
using Palace.ViewModels;

namespace Palace.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel => AppServices.Settings;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            ViewModel.RequestPickCloudFolder = PickCloudFolderAsync;
            await ViewModel.LoadAsync();
        };
        Unloaded += (_, _) => ViewModel.RequestPickCloudFolder = null;
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
