using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Palace.Models;
using Palace.Services;
using Palace.ViewModels;

namespace Palace.Pages;

public sealed partial class RoomsPage : Page
{
    public RoomsViewModel ViewModel => AppServices.Rooms;

    public RoomsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.RefreshAsync();
    }

    public static IRelayCommand<AssetItem> GetRemovePinCommand() => AppServices.Rooms.RemovePinItemCommand;

    protected override async void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        await ViewModel.PersistOrderAsync();
        base.OnNavigatedFrom(e);
    }
}
