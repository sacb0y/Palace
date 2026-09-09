using Microsoft.UI.Xaml.Controls;
using Palace.Services;
using Palace.ViewModels;

namespace Palace.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel => AppServices.Settings;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }
}
