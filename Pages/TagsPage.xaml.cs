using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Palace.ViewModels;
using Windows.UI;

namespace Palace.Pages;

public sealed partial class TagsPage : Page
{
    public TagsViewModel ViewModel => AppServices.Tags;

    private bool _ignoreColorChanges;
    private bool _colorDirty;

    public TagsPage()
    {
        InitializeComponent();
        ViewModel.RequestOrganizeChoice = AskOrganizeChoiceAsync;
        ViewModel.RequestConfirm = AskConfirmAsync;
        ViewModel.RequestOpenGallery = GalleryWindow.Show;
        ViewModel.MosaicChunkAppended = () => TagMosaicLayout.InvalidateItemsInfo();
        Loaded += async (_, _) => await ViewModel.RefreshAsync();
    }

    private void TagColorFlyout_Opening(object sender, object e)
    {
        _ignoreColorChanges = true;
        _colorDirty = false;
        PkrTagColor.Color = TagColor.TryParse(ViewModel.SelectedEffectiveColor, out var color)
            ? color
            : Color.FromArgb(255, 255, 255, 255);
        DispatcherQueue.TryEnqueue(() =>
        {
            EnsureColorHexAutomationId(PkrTagColor);
            _ignoreColorChanges = false;
        });
    }

    private static void EnsureColorHexAutomationId(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox box)
            {
                var header = box.Header?.ToString() ?? "";
                var labeled = AutomationProperties.GetName(box) ?? "";
                var text = box.Text ?? "";
                if (header.Contains("Hex", StringComparison.OrdinalIgnoreCase) ||
                    labeled.Contains("Hex", StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith('#') ||
                    (text.Length is 6 or 8 && text.All(Uri.IsHexDigit)))
                {
                    AutomationProperties.SetAutomationId(box, "TxtTagColorHex");
                    return;
                }
            }

            EnsureColorHexAutomationId(child);
        }
    }

    private void PkrTagColor_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_ignoreColorChanges)
        {
            return;
        }

        _colorDirty = true;
    }

    private async void TagColorFlyout_Closed(object sender, object e)
    {
        if (!_colorDirty || ViewModel.SelectedNode?.TagId is null)
        {
            return;
        }

        await ViewModel.ApplyColorAsync(TagColor.ToHex(PkrTagColor.Color));
        _colorDirty = false;
    }

    private async void BtnClearTagColor_Click(object sender, RoutedEventArgs e)
    {
        _colorDirty = false;
        _ignoreColorChanges = true;
        await ViewModel.ClearColorCommand.ExecuteAsync(null);
        PkrTagColor.Color = TagColor.TryParse(ViewModel.SelectedEffectiveColor, out var color)
            ? color
            : Color.FromArgb(255, 255, 255, 255);
        DispatcherQueue.TryEnqueue(() => _ignoreColorChanges = false);
    }

    public static IRelayCommand<TagGroupPick> GetRemoveGroupCommand() => AppServices.Tags.RemoveFromGroupCommand;

    public static IRelayCommand<TagGroupPick> GetRemoveImpliedCommand() => AppServices.Tags.RemoveImpliedCommand;

    private void SelTagScope_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ViewModel.Scope = sender.SelectedItem == SelTagScopeUngrouped
            ? TagScope.Ungrouped
            : sender.SelectedItem == SelTagScopeStarred
                ? TagScope.Starred
                : TagScope.All;
    }

    private void TagChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TagChipItem chip })
        {
            ViewModel.SelectChip(chip);
        }
    }

    private void TagGroupHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TagBoardGroup group })
        {
            ViewModel.SelectGroupCommand.Execute(group);
        }
    }

    private async void TglStarTag_Toggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedIsStarred == TglStarTag.IsOn)
        {
            return;
        }

        await ViewModel.ToggleStarCommand.ExecuteAsync(null);
    }

    private void TagMosaicLayout_ItemsInfoRequested(LinedFlowLayout sender, LinedFlowLayoutItemsInfoRequestedEventArgs args)
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

    private void TagTileImage_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image image)
        {
            return;
        }

        var id = GalleryMedia.FindAssetId(image.Tag, image.DataContext);
        var item = id is null ? null : ViewModel.Assets.FirstOrDefault(a => a.Id == id);
        if (item is null || item.ThumbImage is not null || string.IsNullOrEmpty(item.ThumbPath))
        {
            return;
        }

        item.ThumbImage = LibraryPage.FileToImage(item.ThumbPath);
    }

    private void TagAsset_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        AssetItem? item = null;
        if (sender is FrameworkElement fe)
        {
            var id = GalleryMedia.FindAssetId(fe.Tag, fe.DataContext);
            item = id is null ? null : ViewModel.Assets.FirstOrDefault(a => a.Id == id);
        }

        ViewModel.OpenAssetCommand.Execute(item ?? GrdTagAssets.SelectedItem as AssetItem);
    }

    private async Task<bool> AskConfirmAsync(string title, string message, string primary)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
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
