using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Palace.Models;
using Palace.Services;
using Palace.ViewModels;

namespace Palace.Pages;

public sealed partial class TagsPage : Page
{
    public TagsViewModel ViewModel => AppServices.Tags;

    public TagsPage()
    {
        InitializeComponent();
        ViewModel.RequestOrganizeChoice = AskOrganizeChoiceAsync;
        Loaded += async (_, _) => await ViewModel.RefreshAsync();
    }

    public static IRelayCommand<TagGroupPick> GetRemoveGroupCommand() => AppServices.Tags.RemoveFromGroupCommand;

    public static IRelayCommand<TagGroupPick> GetRemoveImpliedCommand() => AppServices.Tags.RemoveImpliedCommand;

    private void TreTags_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TagTreeNode node)
        {
            ViewModel.SelectNode(node);
        }
    }

    private void AsbAddImplied_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput ||
            args.Reason == AutoSuggestionBoxTextChangeReason.ProgrammaticChange)
        {
            ViewModel.ImpliedQuery = sender.Text;
        }
    }

    private void AsbAddImplied_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is TagGroupPick pick)
        {
            ViewModel.ImpliedQuery = pick.Name;
        }
    }

    private void AsbAddImplied_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is TagGroupPick pick)
        {
            ViewModel.ImpliedQuery = pick.Name;
        }
        else
        {
            ViewModel.ImpliedQuery = sender.Text;
        }

        ViewModel.AddImpliedCommand.Execute(null);
    }

    private async Task<OrganizeChoice?> AskOrganizeChoiceAsync(IReadOnlyList<TagPath> paths)
    {
        var pathBox = new ComboBox
        {
            Header = "Folder path",
            DisplayMemberPath = "Display",
            ItemsSource = paths,
            SelectedIndex = 0
        };
        AutomationProperties.SetAutomationId(pathBox, "CmbTagPath");

        var inSource = new RadioButton { Content = "Stay in the current source folders", IsChecked = true, GroupName = "tagorg" };
        AutomationProperties.SetAutomationId(inSource, "RadTagOrganizeInSource");
        var dest = new RadioButton { Content = "Move to a destination folder", GroupName = "tagorg" };
        AutomationProperties.SetAutomationId(dest, "RadTagOrganizeDestination");
        var folderBox = new TextBox { Header = "Destination", IsReadOnly = true };
        AutomationProperties.SetAutomationId(folderBox, "TxtTagOrganizeDestination");
        var pick = new Button { Content = "Pick destination" };
        AutomationProperties.SetAutomationId(pick, "BtnPickTagOrganizeDestination");
        string? destPath = null;
        pick.Click += async (_, _) =>
        {
            var folder = await AppServices.Access.PickFolderAsync();
            if (folder is not null)
            {
                destPath = folder.Path;
                folderBox.Text = folder.Path;
                dest.IsChecked = true;
            }
        };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "Choose which parent chain becomes the folder path, then where files should move.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(pathBox);
        panel.Children.Add(inSource);
        panel.Children.Add(dest);
        panel.Children.Add(folderBox);
        panel.Children.Add(pick);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Organize by tag",
            PrimaryButtonText = "Preview",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = panel
        };
        dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        var useDest = dest.IsChecked == true;
        if (useDest && string.IsNullOrWhiteSpace(destPath))
        {
            ViewModel.StatusText = "Pick a destination folder or stay in source.";
            return null;
        }

        var path = pathBox.SelectedItem as TagPath ?? paths[0];
        return new OrganizeChoice
        {
            Policy = useDest ? DestinationPolicy.Destination : DestinationPolicy.InSource,
            DestinationRoot = destPath,
            FolderSegments = path.Slugs,
            KeepOriginalFileName = true
        };
    }
}
