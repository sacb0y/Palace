using Microsoft.UI.Xaml.Controls;
using Palace.Pages;
using Palace.ViewModels;

namespace Palace;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            NavMain.SelectedItem = NavLibrary;
        };
    }

    private void NavMain_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        var tag = item.Tag as string;
        switch (tag)
        {
            case "tags":
                ContentFrame.Navigate(typeof(TagsPage));
                break;
            case "rooms":
                ContentFrame.Navigate(typeof(RoomsPage));
                break;
            case "settings":
                ContentFrame.Navigate(typeof(SettingsPage));
                break;
            default:
                ContentFrame.Navigate(typeof(LibraryPage));
                break;
        }
    }
}
