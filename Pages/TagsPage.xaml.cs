using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Palace.ViewModels;
using Windows.Media.Core;
using Windows.Storage;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace Palace.Pages;

public sealed partial class TagsPage : Page
{
    public TagsViewModel ViewModel => AppServices.Tags;

    private bool _ignoreColorChanges;
    private bool _colorDirty;
    private bool _mosaicSortArmed;
    private static readonly SemaphoreSlim TileDecodeGate = new(4);
    private readonly Dictionary<Image, AssetItem> _realizedTiles = [];
    private readonly Dictionary<Image, long> _tileTagCallbacks = [];
    private AssetItem? _selectedMosaicAsset;
    private GalleryViewModel? _hookedOverlay;
    private int _overlayMediaEpoch;

    public TagsPage()
    {
        InitializeComponent();
        ViewModel.RequestOrganizeChoice = AskOrganizeChoiceAsync;
        ViewModel.RequestConfirm = AskConfirmAsync;
        ViewModel.RequestOpenGallery = GalleryWindow.Show;
        ViewModel.RequestFocusRename = FocusRenameBox;
        ViewModel.MosaicReset = OnMosaicReset;
        ViewModel.MosaicChunkAppended = OnMosaicChunkAppended;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TagsViewModel.OverlayGallery) or nameof(TagsViewModel.IsGalleryOverlayOpen))
            {
                HookOverlayGallery();
                UpdateOverlayMedia();
                if (ViewModel.IsGalleryOverlayOpen)
                {
                    GrdGalleryOverlay.Focus(FocusState.Programmatic);
                }
            }
        };
        Loaded += async (_, _) =>
        {
            GrdTagAssets.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(GrdTagAssets_KeyDown), true);
            RefreshRealizedTiles();
            HookOverlayGallery();
            await ErrorReporter.RunAsync("Load tags", null, ViewModel.RefreshAsync);
            ArmMosaicSortCombo();
        };
        Unloaded += (_, _) =>
        {
            _mosaicSortArmed = false;
            if (ReferenceEquals(ViewModel.MosaicReset, (Action)OnMosaicReset))
            {
                ViewModel.MosaicReset = null;
                ViewModel.MosaicChunkAppended = null;
            }

            if (_hookedOverlay is not null)
            {
                _hookedOverlay.PropertyChanged -= OverlayGallery_PropertyChanged;
                _hookedOverlay = null;
            }
        };
    }

    private void ArmMosaicSortCombo()
    {
        _mosaicSortArmed = false;
        CmbTagMosaicSort.SelectedIndex = ViewModel.MosaicSortIndex;
        _mosaicSortArmed = true;
    }

    private void CmbTagMosaicSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_mosaicSortArmed)
        {
            return;
        }

        var index = CmbTagMosaicSort.SelectedIndex;
        if (!MosaicSortOrder.ShouldApplyIndex(index, ViewModel.MosaicSort, out _))
        {
            return;
        }

        ViewModel.MosaicSortIndex = index;
    }

    public static IRelayCommand<AssetItem?> GetOpenAssetCommand() => AppServices.Tags.OpenAssetCommand;

    public static IRelayCommand<TagChipItem?> GetStarChipCommand() => AppServices.Tags.StarChipCommand;

    public static IRelayCommand<TagChipItem?> GetFilterChipCommand() => AppServices.Tags.FilterChipCommand;

    public static IRelayCommand<TagChipItem?> GetRenameChipCommand() => AppServices.Tags.RenameChipCommand;

    public static IRelayCommand<TagChipItem?> GetRemoveChipFromGroupCommand() => AppServices.Tags.RemoveChipFromGroupCommand;

    public static IRelayCommand<TagChipItem?> GetDeleteChipCommand() => AppServices.Tags.DeleteChipCommand;

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

        await ErrorReporter.RunAsync("Apply tag color", null, async () =>
        {
            await ViewModel.ApplyColorAsync(TagColor.ToHex(PkrTagColor.Color));
            _colorDirty = false;
        });
    }

    private async void BtnClearTagColor_Click(object sender, RoutedEventArgs e)
    {
        _colorDirty = false;
        _ignoreColorChanges = true;
        await ErrorReporter.RunAsync("Clear tag color", null, async () =>
        {
            await ViewModel.ClearColorCommand.ExecuteAsync(null);
            PkrTagColor.Color = TagColor.TryParse(ViewModel.SelectedEffectiveColor, out var color)
                ? color
                : Color.FromArgb(255, 255, 255, 255);
        });
        DispatcherQueue.TryEnqueue(() => _ignoreColorChanges = false);
    }

    public static IRelayCommand<TagGroupPick> GetRemoveGroupCommand() => AppServices.Tags.RemoveFromGroupCommand;

    public static IRelayCommand<TagGroupPick> GetRemoveImpliedCommand() => AppServices.Tags.RemoveImpliedCommand;

    private void TagChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TagChipItem chip })
        {
            ViewModel.SelectChip(chip);
        }
    }

    private void FocusRenameBox()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            TxtRenameTag.Focus(FocusState.Programmatic);
            TxtRenameTag.SelectAll();
        });
    }

    private void TagChipMenu_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout)
        {
            return;
        }

        var chip = flyout.Target is FrameworkElement { Tag: TagChipItem bound }
            ? bound
            : null;
        if (chip is null)
        {
            return;
        }

        foreach (var item in flyout.Items)
        {
            if (item is not MenuFlyoutSubItem sub || sub.Tag is not string kind)
            {
                continue;
            }

            var add = kind == "add";
            FillGroupSubmenu(sub, chip, add);
        }
    }

    private void FillGroupSubmenu(MenuFlyoutSubItem sub, TagChipItem chip, bool add)
    {
        sub.Items.Clear();
        var groups = ViewModel.GroupDestinations(chip, add);
        sub.IsEnabled = groups.Count > 0;
        var command = add ? ViewModel.AddChipToGroupCommand : ViewModel.MoveChipToGroupCommand;
        var prefix = add ? "MnuAddToGroup_" : "MnuMoveToGroup_";
        foreach (var group in groups)
        {
            var item = new MenuFlyoutItem
            {
                Text = group.Name,
                Command = command,
                CommandParameter = new TagChipGroupMove
                {
                    Chip = chip,
                    GroupId = group.TagId,
                    GroupName = group.Name
                }
            };
            AutomationProperties.SetAutomationId(
                item,
                prefix + string.Concat((group.Name ?? "").Where(char.IsLetterOrDigit)));
            AutomationProperties.SetName(item, group.Name);
            sub.Items.Add(item);
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

        await ErrorReporter.RunAsync("Toggle star", null, () => ViewModel.ToggleStarCommand.ExecuteAsync(null));
    }

    private void OnMosaicReset()
    {
        if (!IsLoaded)
        {
            return;
        }

        _realizedTiles.Clear();
        _selectedMosaicAsset = null;
    }

    private void OnMosaicChunkAppended()
    {
        if (!IsLoaded)
        {
            return;
        }

        RefreshRealizedTiles();
    }

    private void TagTileImage_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image image)
        {
            EnsureTileTagCallback(image);
            BindTileImage(image);
        }
    }

    private void TagTileImage_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is Image image)
        {
            EnsureTileTagCallback(image);
            BindTileImage(image, args.NewValue as AssetItem);
        }
    }

    private void TagTileImage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image image)
        {
            if (_tileTagCallbacks.TryGetValue(image, out var token))
            {
                image.UnregisterPropertyChangedCallback(FrameworkElement.TagProperty, token);
                _tileTagCallbacks.Remove(image);
            }

            _realizedTiles.Remove(image);
        }
    }

    private void OnTileTagChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (sender is Image image)
        {
            BindTileImage(image);
        }
    }

    private void EnsureTileTagCallback(Image image)
    {
        if (_tileTagCallbacks.ContainsKey(image))
        {
            return;
        }

        var token = image.RegisterPropertyChangedCallback(FrameworkElement.TagProperty, OnTileTagChanged);
        _tileTagCallbacks[image] = token;
    }

    private void RefreshRealizedTiles()
    {
        foreach (var image in _tileTagCallbacks.Keys.ToList())
        {
            BindTileImage(image);
        }
    }

    private void BindTileImage(Image image, AssetItem? hinted = null)
    {
        var item = FindAssetItem(image) ?? hinted ?? image.DataContext as AssetItem;
        if (item is null)
        {
            _realizedTiles.Remove(image);
            return;
        }

        _realizedTiles[image] = item;
        _ = LoadTileThumbAsync(item);
    }

    private async Task LoadTileThumbAsync(AssetItem item)
    {
        if (!GalleryMedia.ShouldLoadTileThumb(
                item.IsOrphan,
                item.ThumbLoadStarted,
                item.ThumbImage is not null,
                item.ThumbPath,
                item.ContentHash))
        {
            return;
        }

        item.ThumbLoadStarted = true;
        await TileDecodeGate.WaitAsync();
        try
        {
            if (item.ThumbImage is not null)
            {
                return;
            }

            var path = item.ThumbPath;
            if (GalleryMedia.ShouldRequestMosaicThumb(
                    item.IsOrphan,
                    item.Path,
                    item.ContentHash,
                    path,
                    item.IsOnlineOnly || CloudFile.IsOnlineOnly(item.Path)))
            {
                var generated = await AppServices.Thumbnails.EnsureThumbnailAsync(item.Path, item.ContentHash, item.Kind);
                path = generated?.Path;
                if (!string.IsNullOrEmpty(path))
                {
                    item.ThumbPath = path;
                }
            }

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            await UiDispatch.RunAsync(() =>
            {
                if (item.ThumbImage is not null)
                {
                    return;
                }

                item.ThumbImage = LibraryPage.FileToImage(path);
            });
        }
        catch
        {
            item.ThumbLoadStarted = false;
        }
        finally
        {
            TileDecodeGate.Release();
        }
    }

    private void TagAsset_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var item = FindAssetItem(sender) ?? FindAssetItem(e.OriginalSource);
        if (item is null)
        {
            return;
        }

        EnsureSelectedForContext(item);
    }

    private void TagAsset_GotFocus(object sender, RoutedEventArgs e)
    {
        var item = FindAssetItem(sender);
        if (item is not null)
        {
            StampMosaicSelection(item);
        }
    }

    private void TagAsset_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var item = FindAssetItem(sender) ?? FindAssetItem(e.OriginalSource);
        if (item is null)
        {
            return;
        }

        if (!GalleryMedia.ShouldOpenOverlayFromDoubleTap(item.IsFolderHeader, -1))
        {
            e.Handled = true;
            return;
        }

        OpenTagAsset(item);
        e.Handled = true;
    }

    private void GrdTagAssets_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (GalleryScale.ClosesOverlay(ViewModel.IsGalleryOverlayOpen, (int)e.Key))
        {
            ViewModel.CloseOverlayCommand.Execute(null);
            e.Handled = true;
            return;
        }

        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        var item = FindAssetItem(e.OriginalSource)
            ?? FindAssetItem(focused)
            ?? _selectedMosaicAsset
            ?? ViewModel.Assets.FirstOrDefault(asset => asset.IsSelected);
        var isHeader = item?.IsFolderHeader == true;
        if (GalleryMedia.ShouldOpenOverlayFromSpace(
                ViewModel.IsGalleryOverlayOpen,
                FocusIsTextInput(e.OriginalSource),
                isHeader,
                (int)e.Key))
        {
            if (item is not null)
            {
                OpenTagAsset(item);
                e.Handled = true;
            }

            return;
        }

        if (e.Key != VirtualKey.Enter || ViewModel.IsGalleryOverlayOpen)
        {
            return;
        }

        if (item is null)
        {
            return;
        }

        OpenTagAsset(item);
        e.Handled = true;
    }

    private void TagAsset_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var item = FindAssetItem(sender) ?? FindAssetItem(e.OriginalSource);
        if (item is not null)
        {
            EnsureSelectedForContext(item);
        }
    }

    private void TagAssetMenu_Opening(object sender, object e)
    {
        if (sender is MenuFlyout { Target: FrameworkElement target })
        {
            var item = FindAssetItem(target);
            if (item is not null)
            {
                EnsureSelectedForContext(item);
            }
        }
    }

    private void OpenTagAsset(AssetItem item)
    {
        if (item.IsFolderHeader)
        {
            return;
        }

        EnsureSelectedForContext(item);
        ViewModel.OpenAssetCommand.Execute(item);
    }

    private void EnsureSelectedForContext(AssetItem item)
    {
        StampMosaicSelection(item);
        FocusMosaicTile(item);
    }

    private void StampMosaicSelection(AssetItem item)
    {
        if (_selectedMosaicAsset?.Id == item.Id)
        {
            item.IsSelected = true;
            _selectedMosaicAsset = item;
            return;
        }

        foreach (var asset in ViewModel.Assets)
        {
            asset.IsSelected = asset.Id == item.Id;
        }

        _selectedMosaicAsset = ViewModel.Assets.FirstOrDefault(asset => asset.Id == item.Id) ?? item;
        _selectedMosaicAsset.IsSelected = true;
    }

    private void FocusMosaicTile(AssetItem item)
    {
        foreach (var (image, bound) in _realizedTiles)
        {
            if (bound.Id != item.Id)
            {
                continue;
            }

            var tile = image.Parent as FrameworkElement ?? image;
            tile.Focus(FocusState.Programmatic);
            return;
        }
    }

    private AssetItem? FindAssetItem(object? source)
    {
        for (var current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement element)
            {
                if (element.DataContext is AssetItem bound)
                {
                    return bound;
                }

                var id = GalleryMedia.FindAssetId(element.Tag, element.DataContext);
                if (!string.IsNullOrEmpty(id))
                {
                    var match = ViewModel.Assets.FirstOrDefault(a => a.Id == id);
                    if (match is not null)
                    {
                        return match;
                    }
                }
            }
        }

        return null;
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
        pick.Click += async (_, _) => await ErrorReporter.RunAsync("Pick destination", null, async () =>
        {
            var folder = await AppServices.Access.PickFolderAsync();
            if (folder is not null)
            {
                destPath = folder.Path;
                folderBox.Text = folder.Path;
                dest.IsChecked = true;
            }
        });

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

    private void HookOverlayGallery()
    {
        if (_hookedOverlay is not null)
        {
            _hookedOverlay.PropertyChanged -= OverlayGallery_PropertyChanged;
        }

        _hookedOverlay = ViewModel.OverlayGallery;
        if (_hookedOverlay is not null)
        {
            _hookedOverlay.PropertyChanged += OverlayGallery_PropertyChanged;
        }
    }

    private void OverlayGallery_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryViewModel.StillRevision)
            or nameof(GalleryViewModel.IsVideo)
            or nameof(GalleryViewModel.IsImage))
        {
            UpdateOverlayMedia();
        }
    }

    private async void UpdateOverlayMedia()
    {
        var epoch = Interlocked.Increment(ref _overlayMediaEpoch);
        var gallery = ViewModel.OverlayGallery;
        SrfOverlayStill.Bind(ViewModel.IsGalleryOverlayOpen && gallery is { IsImage: true } ? gallery : null);

        if (gallery is { IsVideo: true, CurrentPath: not null } video
            && !AccessService.WouldHydrateOnOpen(video.CurrentPath))
        {
            BtnGalleryPlay.Visibility = Visibility.Visible;
            try
            {
                EnsureOverlayPlayer();
                var file = await StorageFile.GetFileFromPathAsync(video.CurrentPath);
                if (epoch != _overlayMediaEpoch)
                {
                    return;
                }

                MpeOverlay.Source = MediaSource.CreateFromStorageFile(file);
            }
            catch
            {
                if (epoch == _overlayMediaEpoch)
                {
                    MpeOverlay.Source = null;
                }
            }
        }
        else
        {
            BtnGalleryPlay.Visibility = Visibility.Collapsed;
            MpeOverlay.Source = null;
        }
    }

    private void EnsureOverlayPlayer()
    {
        if (MpeOverlay.MediaPlayer is null)
        {
            MpeOverlay.SetMediaPlayer(new Windows.Media.Playback.MediaPlayer());
        }
    }

    private void BtnGalleryPlay_Click(object sender, RoutedEventArgs e)
    {
        EnsureOverlayPlayer();
        var player = MpeOverlay.MediaPlayer;
        if (player is null)
        {
            return;
        }

        player.Play();
        BtnGalleryPlay.Visibility = Visibility.Collapsed;
    }

    private void GalleryOverlay_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel.OverlayGallery is null)
        {
            return;
        }

        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        if (ViewModel.OverlayGallery.TryHandleViewerShortcut(control, (int)e.Key))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Escape:
                ViewModel.CloseOverlayCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Left:
                ViewModel.OverlayGallery.GoPreviousCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Right:
                ViewModel.OverlayGallery.GoNextCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void GalleryOverlay_BackdropPressed(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.CloseOverlayCommand.Execute(null);
        e.Handled = true;
    }

    private static bool FocusIsTextInput(object? source)
    {
        for (var current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TextBox or RichEditBox or PasswordBox or AutoSuggestBox)
            {
                return true;
            }
        }

        return false;
    }
}
