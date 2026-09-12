using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Palace.Pages;
using Palace.Services;
using Palace.ViewModels;

namespace Palace;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        ViewModel.RequestProjectName = AskProjectNameAsync;
        AppServices.Tags.RequestShowLibrary = () => NavMain.SelectedItem = NavLibrary;
        Loaded += async (_, _) =>
        {
            await ViewModel.LoadAsync();
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

    private async Task<string?> AskProjectNameAsync(string title, string primary, string initial)
    {
        var box = new TextBox
        {
            Header = "Name",
            Text = initial,
            PlaceholderText = "Project name"
        };
        AutomationProperties.SetAutomationId(box, "TxtProjectName");
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = box
        };
        dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text)
            ? box.Text.Trim()
            : null;
    }
}
