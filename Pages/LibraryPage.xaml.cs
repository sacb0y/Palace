using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI.Core;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Palace.ViewModels;
using Windows.Media.Core;
using Windows.Media.Playback;
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
    private static readonly SemaphoreSlim TileDecodeGate = new(4);
    private readonly Dictionary<Image, AssetItem> _realizedTiles = [];
    private readonly Dictionary<Image, long> _tileTagCallbacks = [];
    private readonly Dictionary<string, WeakReference<BitmapImage>> _tileBitmapCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressBrowseChrome;
    private bool _suppressMosaicSelection;
    private bool _mosaicSortArmed;
    private VirtualKey _mosaicArrow;
    private long _headerGestureAtMs = -1;

    public LibraryViewModel ViewModel => AppServices.Library;

    public LibraryPage()
    {
        _suppressBrowseChrome = true;
        InitializeComponent();
        SyncBrowseChromeFromViewModel();
        _suppressBrowseChrome = false;
        ViewModel.RequestOrganizeChoice = AskOrganizeChoiceAsync;
        ViewModel.RequestConfirm = AskConfirmAsync;
        ViewModel.RequestPickRoom = AskRoomAsync;
        ViewModel.RequestFocusAssignTag = () =>
            DispatcherQueue.TryEnqueue(() => AsbAssignTag.Focus(FocusState.Programmatic));
        ViewModel.RequestOpenAssignPanel = () =>
            DispatcherQueue.TryEnqueue(() => ShowAssignTagsFlyout(preferTagSelectedCta: true));
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

            if (e.PropertyName is nameof(LibraryViewModel.IsTagBrowse) or nameof(LibraryViewModel.TagFilterMode))
            {
                _suppressBrowseChrome = true;
                SyncBrowseChromeFromViewModel();
                _suppressBrowseChrome = false;
            }
        };
        Loaded += async (_, _) =>
        {
            _suppressBrowseChrome = true;
            SyncBrowseChromeFromViewModel();
            _suppressBrowseChrome = false;
            await ErrorReporter.RunAsync("Reload mosaic sort", null, ViewModel.ReloadMosaicSortAsync);
            ArmMosaicSortCombo();
            await ErrorReporter.RunAsync("Load tag catalog", null, ViewModel.ReloadTagCatalogAsync);
            UpdatePreview();
            MosaicLayout.InvalidateItemsInfo();
            HookOverlayGallery();
            GrdAssets.AddHandler(DoubleTappedEvent, new DoubleTappedEventHandler(GrdAssets_DoubleTapped), true);
            GrdAssets.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(GrdAssets_KeyDown), true);
            RefreshRealizedTiles();
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

    public static IRelayCommand<AssignedTagItem> GetRemoveTagCommand() => AppServices.Library.RemoveAssignedTagCommand;

    public static IRelayCommand<PromptSuggestion> GetAcceptSuggestionCommand() => AppServices.Library.AcceptSuggestionCommand;

    public static IRelayCommand<AssetItem?> GetActivateFolderHeaderCommand() => AppServices.Library.ActivateFolderHeaderCommand;

    public static IRelayCommand<AssetItem?> GetOpenOverlayCommand() => AppServices.Library.OpenOverlayCommand;

    public static IRelayCommand<AssetItem?> GetOpenInNewWindowCommand() => AppServices.Library.OpenInNewWindowCommand;

    public static IRelayCommand GetShowInExplorerCommand() => AppServices.Library.ShowInExplorerCommand;

    public static IRelayCommand GetCopyFilesCommand() => AppServices.Library.CopyFilesCommand;

    public static IRelayCommand GetCopyPathCommand() => AppServices.Library.CopyPathCommand;

    public static IRelayCommand GetMoveToFolderCommand() => AppServices.Library.MoveToFolderCommand;

    public static IRelayCommand GetAddSelectionToRoomCommand() => AppServices.Library.AddSelectionToRoomCommand;

    public static IRelayCommand GetFocusAssignTagCommand() => AppServices.Library.FocusAssignTagCommand;

    public static IRelayCommand<TagChipItem> GetRemoveFilterTagCommand() => AppServices.Library.RemoveFilterTagCommand;

    public static IRelayCommand<TagChipItem> GetToggleAssignChipCommand() => AppServices.Library.ToggleAssignChipCommand;

    public static IRelayCommand GetDeleteFilesCommand() => AppServices.Library.DeleteFilesCommand;

    public static BitmapImage? FileToImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !CloudFile.Exists(path) || CloudFile.IsOnlineOnly(path))
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
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            if (GalleryMedia.IsRemoteUri(path))
            {
                return new BitmapImage { UriSource = new Uri(path, UriKind.Absolute) };
            }

            if (!CloudFile.Exists(path) || CloudFile.IsOnlineOnly(path))
            {
                return null;
            }

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
        var length = GalleryMedia.MosaicAspectCount(args.ItemsRangeRequestedLength, available);
        if (length <= 0)
        {
            return;
        }

        var ratios = new double[length];
        for (var i = 0; i < length; i++)
        {
            if (i >= available)
            {
                ratios[i] = 1.0;
                continue;
            }

            var item = assets[start + i];
            ratios[i] = GalleryMedia.MosaicAspect(item.IsFolderHeader, item.AspectRatio);
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
            ViewModel.ToggleFilterTag(node);
        }
    }

    private void SelBrowseMode_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_suppressBrowseChrome)
        {
            return;
        }

        ViewModel.IsTagBrowse = sender.SelectedItem == SelTags;
    }

    private void SelTagMatch_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_suppressBrowseChrome)
        {
            return;
        }

        ViewModel.TagFilterMode = sender.SelectedItem == SelTagMatchAny
            ? TagFilterMode.Any
            : sender.SelectedItem == SelTagMatchNone
                ? TagFilterMode.None
                : TagFilterMode.All;
    }

    private void SyncBrowseChromeFromViewModel()
    {
        SelBrowseMode.SelectedItem = ViewModel.IsTagBrowse ? SelTags : SelFolders;
        SelTagMatch.SelectedItem = ViewModel.TagFilterMode switch
        {
            TagFilterMode.Any => SelTagMatchAny,
            TagFilterMode.None => SelTagMatchNone,
            _ => SelTagMatchAll
        };
    }

    private void ArmMosaicSortCombo()
    {
        _mosaicSortArmed = false;
        CmbMosaicSort.SelectedIndex = ViewModel.MosaicSortIndex;
        _mosaicSortArmed = true;
    }

    private void CmbMosaicSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_mosaicSortArmed)
        {
            return;
        }

        var index = CmbMosaicSort.SelectedIndex;
        if (!MosaicSortOrder.ShouldApplyIndex(index, ViewModel.MosaicSort, out _))
        {
            return;
        }

        ViewModel.MosaicSortIndex = index;
    }

    private void BtnBrowseTags_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RebuildAssignPanelPublic();
    }

    private void BtnTagSelected_Click(object sender, RoutedEventArgs e)
    {
        ShowAssignTagsFlyout(preferTagSelectedCta: true);
    }

    private void ShowAssignTagsFlyout(bool preferTagSelectedCta)
    {
        ViewModel.RebuildAssignPanelPublic();
        var anchor = preferTagSelectedCta && ViewModel.ShowTagSelectedCta
            ? BtnTagSelected
            : BtnBrowseTags;
        FlyAssignTags.ShowAt(anchor);
    }

    private void FlyAssignTags_Opening(object sender, object e)
    {
        ViewModel.RebuildAssignPanelPublic();
    }

    private void TglSelectMode_Changed(object sender, RoutedEventArgs e)
    {
        var on = (sender as ToggleButton)?.IsChecked == true;
        GrdAssets.SelectionMode = SelectMode.ClickTogglesTile(on)
            ? ItemsViewSelectionMode.Multiple
            : ItemsViewSelectionMode.Extended;
    }

    private void GrdAssets_SelectionChanged(ItemsView sender, ItemsViewSelectionChangedEventArgs e)
    {
        if (_suppressMosaicSelection)
        {
            return;
        }

        var assets = ViewModel.Assets;
        var keep = new List<AssetItem>();
        var headerIndexes = new List<int>();
        for (var i = 0; i < assets.Count; i++)
        {
            if (!sender.IsSelected(i))
            {
                continue;
            }

            if (assets[i].IsFolderHeader)
            {
                headerIndexes.Add(i);
            }
            else
            {
                keep.Add(assets[i]);
            }
        }

        var arrow = _mosaicArrow;
        _mosaicArrow = VirtualKey.None;
        if (headerIndexes.Count == 0)
        {
            ViewModel.SetSelection(keep);
            return;
        }

        _suppressMosaicSelection = true;
        try
        {
            foreach (var index in headerIndexes)
            {
                sender.Deselect(index);
            }

            if (keep.Count == 0 && arrow is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
            {
                var step = arrow is VirtualKey.Left or VirtualKey.Up ? -1 : 1;
                var flags = assets.Select(item => item.IsFolderHeader).ToList();
                var next = FolderGroups.NextNonHeaderIndex(flags, headerIndexes[0], step);
                if (next >= 0)
                {
                    sender.Select(next);
                    keep.Add(assets[next]);
                }
            }

            if (keep.Count == 0)
            {
                for (var i = 0; i < assets.Count; i++)
                {
                    if (assets[i].IsFolderHeader || !assets[i].IsSelected)
                    {
                        continue;
                    }

                    sender.Select(i);
                    keep.Add(assets[i]);
                }
            }
        }
        finally
        {
            _suppressMosaicSelection = false;
        }

        if (keep.Count > 0)
        {
            ViewModel.SetSelection(keep);
        }
    }

    private void FolderGroupHeader_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _mosaicArrow = VirtualKey.None;
        NoteHeaderGesture();
        e.Handled = true;
    }

    private void FolderGroupHeader_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var header = FindAssetItem(sender) ?? FindAssetItem(e.OriginalSource);
        if (header is not { IsFolderHeader: true })
        {
            return;
        }

        NoteHeaderGesture();
        ViewModel.SelectFolderGroup(header);
        e.Handled = true;
    }

    private void NoteHeaderGesture() => _headerGestureAtMs = Environment.TickCount64;

    private long MillisecondsSinceHeaderGesture() =>
        _headerGestureAtMs < 0 ? -1 : Environment.TickCount64 - _headerGestureAtMs;

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

    private void GrdAssets_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) =>
        TryOpenOverlayFromDoubleTap(e.OriginalSource, e);

    private void AssetItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) =>
        TryOpenOverlayFromDoubleTap(sender, e);

    private void TryOpenOverlayFromDoubleTap(object? source, DoubleTappedRoutedEventArgs e)
    {
        var item = FindAssetItem(source) ?? FindAssetItem(e.OriginalSource);
        if (!GalleryMedia.ShouldOpenOverlayFromDoubleTap(
                item?.IsFolderHeader == true,
                MillisecondsSinceHeaderGesture()))
        {
            e.Handled = true;
            return;
        }

        if (item is null)
        {
            return;
        }

        OpenOverlayFor(item);
        e.Handled = true;
    }

    private void GrdAssets_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
        {
            _mosaicArrow = e.Key;
        }

        if (GalleryScale.ClosesOverlay(ViewModel.IsGalleryOverlayOpen, (int)e.Key))
        {
            ViewModel.CloseOverlayCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (!GalleryMedia.ShouldOpenOverlayFromSpace(
                ViewModel.IsGalleryOverlayOpen,
                FocusIsTextInput(e.OriginalSource),
                false,
                (int)e.Key))
        {
            return;
        }

        var item = FindAssetItem(e.OriginalSource)
            ?? ViewModel.SelectedAsset
            ?? ViewModel.Assets.FirstOrDefault(asset => !asset.IsFolderHeader);
        if (item is null || item.IsFolderHeader)
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
            ?? ViewModel.Assets.FirstOrDefault(asset => !asset.IsFolderHeader);
        if (item is null)
        {
            return;
        }

        if (item.IsFolderHeader)
        {
            ViewModel.SelectFolderGroup(item);
            e.Handled = true;
            return;
        }

        OpenOverlayFor(item);
        e.Handled = true;
    }

    private void OpenOverlayFor(AssetItem item)
    {
        if (item.IsFolderHeader)
        {
            return;
        }

        EnsureSelectedForContext(item);
        ViewModel.OpenOverlayCommand.Execute(item);
    }

    private AssetItem? FindAssetItem(object? source)
    {
        for (var current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is not FrameworkElement element)
            {
                continue;
            }

            if (element.DataContext is AssetItem bound)
            {
                return bound;
            }

            var key = GalleryMedia.FindAssetId(element.Tag, element.DataContext);
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            var match = ViewModel.Assets.FirstOrDefault(item =>
                GalleryMedia.MatchesMosaicKey(item.Id, item.MosaicTag, key));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private void AssetItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var item = FindAssetItem(sender) ?? FindAssetItem(e.OriginalSource);
        if (item is null || !GalleryMedia.ShouldShowAssetContextFlyout(item.IsFolderHeader))
        {
            e.Handled = true;
            return;
        }

        EnsureSelectedForContext(item);
    }

    private void AssetItem_ContextRequested(object sender, ContextRequestedEventArgs e)
    {
        var item = FindAssetItem(sender) ?? FindAssetItem(e.OriginalSource);
        if (item is null || !GalleryMedia.ShouldShowAssetContextFlyout(item.IsFolderHeader))
        {
            e.Handled = true;
            return;
        }

        EnsureSelectedForContext(item);
    }

    private void AssetMenu_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout)
        {
            return;
        }

        var item = flyout.Target is FrameworkElement target ? FindAssetItem(target) : null;
        if (item is null || !GalleryMedia.ShouldShowAssetContextFlyout(item.IsFolderHeader))
        {
            flyout.Hide();
            return;
        }

        EnsureSelectedForContext(item);
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
            case VirtualKey.Space:
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

    private void EnsureSelectedForContext(AssetItem item)
    {
        if (item.IsFolderHeader || string.IsNullOrEmpty(item.Id))
        {
            return;
        }

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
        RefreshRealizedTiles();
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

            RefreshRealizedTiles();
            _thumbsMayUpgrade = true;
            ScheduleViewportUpgrade();
        });
    }

    private void TileImage_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image image)
        {
            EnsureTileTagCallback(image);
            BindTileImage(image);
        }
    }

    private void TileImage_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is Image image)
        {
            EnsureTileTagCallback(image);
            BindTileImage(image, args.NewValue as AssetItem);
        }
    }

    private void TileImage_Unloaded(object sender, RoutedEventArgs e)
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
        if (item is null || item.IsFolderHeader)
        {
            _realizedTiles.Remove(image);
            return;
        }

        _realizedTiles[image] = item;
        if (item.ThumbImage is null
            && !string.IsNullOrEmpty(item.ThumbPath)
            && TryGetCachedTileBitmap(item.ThumbPath, out var cached))
        {
            item.ThumbImage = cached;
            item.ThumbLoadStarted = true;
            ScheduleViewportUpgrade();
            return;
        }

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

                item.ThumbImage = CreateTileBitmap(path);
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

    private bool TryGetCachedTileBitmap(string path, out BitmapImage? bitmap)
    {
        if (_tileBitmapCache.TryGetValue(path, out var weak) && weak.TryGetTarget(out var cached))
        {
            bitmap = cached;
            return true;
        }

        bitmap = null;
        return false;
    }

    private BitmapImage? CreateTileBitmap(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (TryGetCachedTileBitmap(path, out var cached))
        {
            return cached;
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
            var created = new BitmapImage
            {
                DecodePixelHeight = decode,
                UriSource = new Uri(path, UriKind.Absolute)
            };
            _tileBitmapCache[path] = new WeakReference<BitmapImage>(created);
            return created;
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

            if (item.IsFolderHeader
                || !GalleryMedia.ShouldUpgradeThumb(
                    _thumbsMayUpgrade,
                    CloudFile.IsOnlineOnly(item.Path),
                    item.IsOrphan,
                    item.Path,
                    item.ContentHash,
                    item.ThumbImage is not null,
                    item.ThumbPath))
            {
                continue;
            }

            var info = await AppServices.Thumbnails.EnsureThumbnailAsync(item.Path, item.ContentHash, item.Kind);
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

                if (!GalleryMedia.ShouldReplaceTileBitmap(item.ThumbPath, path, item.ThumbImage is not null))
                {
                    if (string.IsNullOrEmpty(item.ThumbPath))
                    {
                        item.ThumbPath = path;
                    }

                    return;
                }

                item.ThumbPath = path;
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
            if (e.PropertyName is nameof(GalleryViewModel.StillRevision)
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

    private int _overlayMediaEpoch;

    private async void UpdateOverlayMedia()
    {
        var epoch = Interlocked.Increment(ref _overlayMediaEpoch);
        var gallery = ViewModel.OverlayGallery;
        SrfOverlayStill.Bind(ViewModel.IsGalleryOverlayOpen && gallery is { IsImage: true } ? gallery : null);

        if (gallery is { IsVideo: true, CurrentPath: not null } video
            && !AccessService.WouldHydrateOnOpen(video.CurrentPath))
        {
            BtnGalleryPlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
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
            BtnGalleryPlay.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
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
        BtnGalleryPlay.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
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
            ItemTemplate = Application.Current.Resources["RoomPickTemplate"] as DataTemplate,
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
