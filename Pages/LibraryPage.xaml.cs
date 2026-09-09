using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Palace.ViewModels;
using Windows.Media.Core;
using Windows.Storage;
using Windows.System;

namespace Palace.Pages;

public sealed partial class LibraryPage : Page
{
    private GalleryViewModel? _hookedOverlay;
    private int _thumbUpgradeEpoch;
    private int _mosaicGeneration;
    private int _upgradeScheduled;
    private bool _thumbsMayUpgrade;
    private string? _lastPointerAssetId;
    private DateTime _lastPointerUtc;
    private static readonly SemaphoreSlim TileDecodeGate = new(4);
    private readonly Dictionary<Image, AssetItem> _realizedTiles = [];

    public LibraryViewModel ViewModel => AppServices.Library;

    public LibraryPage()
    {
        InitializeComponent();
        ViewModel.RequestOrganizeChoice = AskOrganizeChoiceAsync;
        ViewModel.RequestConfirm = AskConfirmAsync;
        ViewModel.RequestPickRoom = AskRoomAsync;
        ViewModel.RequestFocusAssignTag = () =>
            DispatcherQueue.TryEnqueue(() => AsbAssignTag.Focus(FocusState.Programmatic));
        ViewModel.RequestOpenGalleryWindow = GalleryWindow.Show;
        ViewModel.MosaicReset = OnMosaicReset;
        ViewModel.MosaicChunkAppended = OnMosaicChunkAppended;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LibraryViewModel.PreviewPath) or nameof(LibraryViewModel.IsVideoPreview) or nameof(LibraryViewModel.IsImagePreview))
            {
                UpdatePreview();
            }

            if (e.PropertyName is nameof(LibraryViewModel.MosaicRowHeight))
            {
                MosaicLayout.InvalidateItemsInfo();
            }

            if (e.PropertyName is nameof(LibraryViewModel.OverlayGallery) or nameof(LibraryViewModel.IsGalleryOverlayOpen))
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
            await ViewModel.ReloadTagCatalogAsync();
            UpdatePreview();
            MosaicLayout.InvalidateItemsInfo();
            HookOverlayGallery();
            GrdAssets.AddHandler(DoubleTappedEvent, new DoubleTappedEventHandler(GrdAssets_DoubleTapped), true);
            GrdAssets.AddHandler(PointerPressedEvent, new PointerEventHandler(AssetItem_PointerPressed), true);
            GrdAssets.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(GrdAssets_KeyDown), true);
            if (ViewModel.Assets.Count > 0)
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    _thumbsMayUpgrade = true;
                    ScheduleViewportUpgrade();
                });
            }
        };
        Unloaded += (_, _) =>
        {
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

    public static IRelayCommand<AssignedTagItem> GetRemoveTagCommand() => AppServices.Library.RemoveAssignedTagCommand;

    public static IRelayCommand<PromptSuggestion> GetAcceptSuggestionCommand() => AppServices.Library.AcceptSuggestionCommand;

    public static IRelayCommand<AssetItem?> GetOpenOverlayCommand() => AppServices.Library.OpenOverlayCommand;

    public static IRelayCommand<AssetItem?> GetOpenInNewWindowCommand() => AppServices.Library.OpenInNewWindowCommand;

    public static IRelayCommand GetShowInExplorerCommand() => AppServices.Library.ShowInExplorerCommand;

    public static IRelayCommand GetCopyFilesCommand() => AppServices.Library.CopyFilesCommand;

    public static IRelayCommand GetCopyPathCommand() => AppServices.Library.CopyPathCommand;

    public static IRelayCommand GetMoveToFolderCommand() => AppServices.Library.MoveToFolderCommand;

    public static IRelayCommand GetAddSelectionToRoomCommand() => AppServices.Library.AddSelectionToRoomCommand;

    public static IRelayCommand GetFocusAssignTagCommand() => AppServices.Library.FocusAssignTagCommand;

    public static IRelayCommand GetDeleteFilesCommand() => AppServices.Library.DeleteFilesCommand;

    public static BitmapImage? FileToImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path) || CloudFile.IsOnlineOnly(path))
        {
            return null;
        }

        try
        {
            var rowHeight = AppServices.Library?.MosaicRowHeight ?? 140;
            var scale = 1.0;
            try
            {
                scale = App.Window?.Content?.XamlRoot?.RasterizationScale ?? 1.0;
            }
            catch
            {
                // XamlRoot is unavailable during early bind.
            }

            var decode = (int)Math.Clamp(Math.Round(rowHeight * Math.Min(Math.Max(scale, 1.0), 2.0)), 96, 560);
            return new BitmapImage
            {
                DecodePixelHeight = decode,
                UriSource = new Uri(path, UriKind.Absolute)
            };
        }
        catch
        {
            return null;
        }
    }

    public static BitmapImage? FileToFullImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path) || CloudFile.IsOnlineOnly(path))
        {
            return null;
        }

        try
        {
            return new BitmapImage
            {
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
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        ViewModel.TagQuery = sender.Text ?? "";
        // Updating ItemsSource can reset AutoSuggestBox.Text; keep the query the user typed.
        if (!string.Equals(sender.Text, ViewModel.TagQuery, StringComparison.Ordinal))
        {
            sender.Text = ViewModel.TagQuery;
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

    private void BtnAssignTag_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.TagQuery = AsbAssignTag.Text ?? "";
        ViewModel.AssignFromQueryCommand.Execute(null);
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

    private void GrdAssets_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var item = FindAssetItem(e.OriginalSource);
        if (item is null)
        {
            return;
        }

        OpenOverlayFor(item);
        e.Handled = true;
    }

    private void AssetItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var item = FindAssetItem(sender) ?? FindAssetItem(e.OriginalSource);
        if (item is null)
        {
            return;
        }

        OpenOverlayFor(item);
        e.Handled = true;
    }

    private void GrdAssets_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || ViewModel.IsGalleryOverlayOpen)
        {
            return;
        }

        var item = FindAssetItem(e.OriginalSource)
            ?? ViewModel.SelectedAsset
            ?? ViewModel.Assets.FirstOrDefault();
        if (item is null)
        {
            return;
        }

        OpenOverlayFor(item);
        e.Handled = true;
    }

    private void AssetItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint((UIElement)sender);
        var kind = point.PointerDeviceType;
        if (!point.Properties.IsLeftButtonPressed &&
            kind != PointerDeviceType.Touch &&
            kind != PointerDeviceType.Pen)
        {
            return;
        }

        var item = FindAssetItem(sender) ?? FindAssetItem(e.OriginalSource);
        if (item is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (string.Equals(_lastPointerAssetId, item.Id, StringComparison.Ordinal) &&
            now - _lastPointerUtc < TimeSpan.FromMilliseconds(600))
        {
            OpenOverlayFor(item);
            e.Handled = true;
            _lastPointerAssetId = null;
            _lastPointerUtc = DateTime.MinValue;
            return;
        }

        _lastPointerAssetId = item.Id;
        _lastPointerUtc = now;
    }

    private void OpenOverlayFor(AssetItem item)
    {
        EnsureSelectedForContext(item);
        ViewModel.OpenOverlayCommand.Execute(item);
    }

    private static AssetItem? FindAssetItem(object? source)
    {
        for (var current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: AssetItem item })
            {
                return item;
            }
        }

        return null;
    }

    private void AssetItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AssetItem item })
        {
            EnsureSelectedForContext(item);
        }
    }

    private void AssetMenu_Opening(object sender, object e)
    {
        if (sender is MenuFlyout { Target: FrameworkElement { DataContext: AssetItem item } })
        {
            EnsureSelectedForContext(item);
        }
    }

    private void GalleryOverlay_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel.OverlayGallery is null)
        {
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
        if (e.OriginalSource == sender)
        {
            ViewModel.CloseOverlayCommand.Execute(null);
        }
    }

    private void EnsureSelectedForContext(AssetItem item)
    {
        if (GrdAssets.SelectedItems.OfType<AssetItem>().Any(a => a.Id == item.Id))
        {
            return;
        }

        var index = ViewModel.Assets.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        for (var i = 0; i < ViewModel.Assets.Count; i++)
        {
            if (GrdAssets.IsSelected(i))
            {
                GrdAssets.Deselect(i);
            }
        }

        GrdAssets.Select(index);
    }

    private void OnMosaicReset()
    {
        if (!IsLoaded)
        {
            return;
        }

        _mosaicGeneration++;
        _thumbsMayUpgrade = false;
        Interlocked.Increment(ref _thumbUpgradeEpoch);
        _realizedTiles.Clear();
        MosaicLayout.InvalidateItemsInfo();
    }

    private void OnMosaicChunkAppended()
    {
        if (!IsLoaded)
        {
            return;
        }

        MosaicLayout.InvalidateItemsInfo();
        if (ViewModel.Assets.Count == 0)
        {
            return;
        }

        var generation = _mosaicGeneration;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (generation != _mosaicGeneration)
            {
                return;
            }

            _thumbsMayUpgrade = true;
            ScheduleViewportUpgrade();
        });
    }

    private void TileImage_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image image)
        {
            TrackTile(image, FindAssetItem(image) ?? image.DataContext as AssetItem);
        }
    }

    private void TileImage_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is Image image)
        {
            TrackTile(image, args.NewValue as AssetItem);
        }
    }

    private void TileImage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image image)
        {
            _realizedTiles.Remove(image);
        }
    }

    private void TrackTile(Image image, AssetItem? item)
    {
        if (item is null)
        {
            _realizedTiles.Remove(image);
            return;
        }

        _realizedTiles[image] = item;
        _ = LoadTileThumbAsync(item);
        ScheduleViewportUpgrade();
    }

    private void ScheduleViewportUpgrade()
    {
        if (!_thumbsMayUpgrade)
        {
            return;
        }

        var token = Interlocked.Increment(ref _upgradeScheduled);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (token != _upgradeScheduled || !_thumbsMayUpgrade)
            {
                return;
            }

            _ = UpgradeThumbsAsync();
        });
    }

    private async Task LoadTileThumbAsync(AssetItem item)
    {
        if (item.IsOrphan || item.ThumbLoadStarted || item.ThumbImage is not null)
        {
            return;
        }

        if (string.IsNullOrEmpty(item.ThumbPath))
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
            await UiDispatch.RunAsync(() =>
            {
                if (item.ThumbImage is not null)
                {
                    return;
                }

                item.ThumbImage = CreateTileBitmap(path);
            });
        }
        finally
        {
            TileDecodeGate.Release();
        }
    }

    private BitmapImage? CreateTileBitmap(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            var rowHeight = ViewModel.MosaicRowHeight;
            var scale = 1.0;
            try
            {
                scale = XamlRoot?.RasterizationScale ?? 1.0;
            }
            catch
            {
                // XamlRoot is unavailable during early bind.
            }

            var decode = (int)Math.Clamp(Math.Round(rowHeight * Math.Min(Math.Max(scale, 1.0), 2.0)), 96, 560);
            return new BitmapImage
            {
                DecodePixelHeight = decode,
                UriSource = new Uri(path, UriKind.Absolute)
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task UpgradeThumbsAsync()
    {
        if (!_thumbsMayUpgrade)
        {
            return;
        }

        var epoch = Interlocked.Increment(ref _thumbUpgradeEpoch);
        foreach (var item in _realizedTiles.Values.Distinct().ToList())
        {
            if (epoch != _thumbUpgradeEpoch)
            {
                return;
            }

            if (item.IsOnlineOnly || item.IsOrphan || string.IsNullOrEmpty(item.Path) || string.IsNullOrEmpty(item.ThumbPath))
            {
                continue;
            }

            var hash = Path.GetFileNameWithoutExtension(item.ThumbPath);
            var info = await AppServices.Thumbnails.EnsureThumbnailAsync(item.Path, hash, item.Kind);
            if (epoch != _thumbUpgradeEpoch || info is null)
            {
                continue;
            }

            var path = info.Value.Path;
            await UiDispatch.RunAsync(() =>
            {
                if (epoch != _thumbUpgradeEpoch)
                {
                    return;
                }

                item.ThumbPath = path;
                item.ThumbLoadStarted = false;
                item.ThumbImage = CreateTileBitmap(path);
            });
        }
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
        if (e.PropertyName is nameof(GalleryViewModel.CurrentPath)
            or nameof(GalleryViewModel.IsVideo)
            or nameof(GalleryViewModel.IsImage))
        {
            UpdateOverlayMedia();
        }
    }

    private async void UpdatePreview()
    {
        if (ViewModel.IsImagePreview && ViewModel.PreviewPath is not null)
        {
            ImgPreview.Source = FileToFullImage(ViewModel.PreviewPath);
        }
        else
        {
            ImgPreview.Source = null;
        }

        if (ViewModel.IsVideoPreview && ViewModel.PreviewPath is not null
            && !AccessService.WouldHydrateOnOpen(ViewModel.PreviewPath))
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

    private async void UpdateOverlayMedia()
    {
        if (ViewModel.OverlayGallery is { IsVideo: true, CurrentPath: not null } gallery)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(gallery.CurrentPath);
                MpeOverlay.Source = MediaSource.CreateFromStorageFile(file);
            }
            catch
            {
                MpeOverlay.Source = null;
            }
        }
        else
        {
            MpeOverlay.Source = null;
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
