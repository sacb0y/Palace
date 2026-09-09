using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Palace.Models;
using Palace.Services;
using Palace.ViewModels;
using Windows.Media.Core;
using Windows.Storage;

namespace Palace.Pages;

public sealed partial class LibraryPage : Page
{
    public LibraryViewModel ViewModel => AppServices.Library;

    public LibraryPage()
    {
        InitializeComponent();
        ViewModel.RequestOrganizeChoice = AskOrganizeChoiceAsync;
        ViewModel.RequestConfirm = AskConfirmAsync;
        ViewModel.RequestPickRoom = AskRoomAsync;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LibraryViewModel.PreviewPath) or nameof(LibraryViewModel.IsVideoPreview) or nameof(LibraryViewModel.IsImagePreview))
            {
                UpdatePreview();
            }

            if (e.PropertyName is nameof(LibraryViewModel.Assets) or nameof(LibraryViewModel.MosaicRowHeight))
            {
                MosaicLayout.InvalidateItemsInfo();
            }
        };
        Loaded += async (_, _) =>
        {
            await ViewModel.ReloadTagCatalogAsync();
            UpdatePreview();
            MosaicLayout.InvalidateItemsInfo();
        };
    }

    public static IRelayCommand<AssignedTagItem> GetRemoveTagCommand() => AppServices.Library.RemoveAssignedTagCommand;

    public static IRelayCommand<PromptSuggestion> GetAcceptSuggestionCommand() => AppServices.Library.AcceptSuggestionCommand;

    public static BitmapImage? FileToImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new BitmapImage
            {
                DecodePixelHeight = 280,
                UriSource = new Uri(path, UriKind.Absolute)
            };
        }
        catch
        {
            return null;
        }
    }

    private void MosaicLayout_ItemsInfoRequested(LinedFlowLayout sender, LinedFlowLayoutItemsInfoRequestedEventArgs args)
    {
        var assets = ViewModel.Assets;
        var start = Math.Max(0, args.ItemsRangeStartIndex);
        if (start >= assets.Count)
        {
            return;
        }

        var available = assets.Count - start;
        var length = Math.Max(args.ItemsRangeRequestedLength, available);
        if (length <= 0)
        {
            return;
        }

        var ratios = new double[length];
        for (var i = 0; i < length; i++)
        {
            ratios[i] = i < available ? assets[start + i].AspectRatio : 1.0;
        }

        args.SetDesiredAspectRatios(ratios);
    }

    private void TreFolders_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is FolderNode node)
        {
            ViewModel.SelectedFolder = node;
        }
    }

    private void TreTagsBrowse_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TagTreeNode node)
        {
            ViewModel.SelectedTag = node;
        }
    }

    private void SelBrowseMode_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ViewModel.IsTagBrowse = sender.SelectedItem == SelTags;
    }

    private void GrdAssets_SelectionChanged(ItemsView sender, ItemsViewSelectionChangedEventArgs e)
    {
        ViewModel.SetSelection(sender.SelectedItems.OfType<AssetItem>());
    }

    private void BcrPath_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is PathCrumb crumb)
        {
            ViewModel.NavigateBreadcrumb(crumb);
        }
    }

    private void AsbSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        ViewModel.SearchQuery = sender.Text;
        ViewModel.SearchCommand.Execute(null);
    }

    private void AsbAssignTag_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput ||
            args.Reason == AutoSuggestionBoxTextChangeReason.ProgrammaticChange)
        {
            ViewModel.TagQuery = sender.Text;
        }
    }

    private void AsbAssignTag_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is TagPickItem pick)
        {
            ViewModel.SelectedPickTag = pick;
            ViewModel.TagQuery = pick.Name;
        }
    }

    private void AsbAssignTag_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is TagPickItem pick)
        {
            ViewModel.SelectedPickTag = pick;
            ViewModel.TagQuery = pick.Name;
        }
        else
        {
            ViewModel.TagQuery = sender.Text;
        }

        ViewModel.AssignFromQueryCommand.Execute(null);
    }

    private async void UpdatePreview()
    {
        if (ViewModel.IsImagePreview && ViewModel.PreviewPath is not null)
        {
            ImgPreview.Source = FileToImage(ViewModel.PreviewPath);
        }
        else
        {
            ImgPreview.Source = null;
        }

        if (ViewModel.IsVideoPreview && ViewModel.PreviewPath is not null)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(ViewModel.PreviewPath);
                MpePreview.Source = MediaSource.CreateFromStorageFile(file);
            }
            catch
            {
                MpePreview.Source = null;
            }
        }
        else
        {
            MpePreview.Source = null;
        }
    }

    private async Task<OrganizeChoice?> AskOrganizeChoiceAsync()
    {
        var inSource = new RadioButton { Content = "Stay in the current source folders", IsChecked = true, GroupName = "org" };
        AutomationProperties.SetAutomationId(inSource, "RadOrganizeInSource");
        var dest = new RadioButton { Content = "Move to a destination folder", GroupName = "org" };
        AutomationProperties.SetAutomationId(dest, "RadOrganizeDestination");
        var folderBox = new TextBox { Header = "Destination", IsReadOnly = true, PlaceholderText = "Pick a folder if moving out of source" };
        AutomationProperties.SetAutomationId(folderBox, "TxtOrganizeDestination");
        var pick = new Button { Content = "Pick destination" };
        AutomationProperties.SetAutomationId(pick, "BtnPickOrganizeDestination");
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

        var folderTemplate = new TextBox
        {
            Header = "Folder template",
            Text = "{Character}/{tags:2}"
        };
        AutomationProperties.SetAutomationId(folderTemplate, "TxtFolderTemplate");
        var fileTemplate = new TextBox
        {
            Header = "Filename template",
            Text = "{Character}-{tags}.{ext}"
        };
        AutomationProperties.SetAutomationId(fileTemplate, "TxtFileTemplate");

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "Choose where organized files go. Review the dry-run table before applying.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(inSource);
        panel.Children.Add(dest);
        panel.Children.Add(folderBox);
        panel.Children.Add(pick);
        panel.Children.Add(folderTemplate);
        panel.Children.Add(fileTemplate);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Organize assets",
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
            ViewModel.InfoMessage = "Pick a destination folder or stay in source.";
            ViewModel.ShowInfo = true;
            return null;
        }

        return new OrganizeChoice
        {
            Policy = useDest ? DestinationPolicy.Destination : DestinationPolicy.InSource,
            DestinationRoot = destPath,
            FolderTemplate = string.IsNullOrWhiteSpace(folderTemplate.Text) ? "{Character}/{tags:2}" : folderTemplate.Text,
            FileTemplate = string.IsNullOrWhiteSpace(fileTemplate.Text) ? "{Character}-{tags}.{ext}" : fileTemplate.Text
        };
    }

    private async Task<bool> AskConfirmAsync(string title, string body, string primary)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = body,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<Room?> AskRoomAsync(IReadOnlyList<Room> rooms)
    {
        var list = new ListView
        {
            ItemsSource = rooms,
            DisplayMemberPath = "Name",
            SelectedIndex = 0,
            MaxHeight = 240
        };
        AutomationProperties.SetAutomationId(list, "LstPickRoom");
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Add to room",
            Content = list,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? list.SelectedItem as Room : null;
    }
}
