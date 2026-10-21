using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
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
    private int _mosaicGeneration;
    private bool _thumbsMayUpgrade;
    private bool _shellReady;
    private bool _scrollHooked;
    private bool _planQueued;
    private string _planReason = "realize";
    private long _lastPlanLogMs;
    private ScrollViewer? _mosaicScrollViewer;
    private ScrollView? _mosaicScrollView;
    private readonly ThumbLoadQueue _thumbQueue = new(
        ThumbScheduler.EffectiveConcurrencyCap(Environment.ProcessorCount));
    private readonly ThumbMemoryCache<ImageSource> _thumbMemory = new(256);
    private int _previewEpoch;
    private CancellationTokenSource? _previewCts;
    private readonly Dictionary<string, CancellationTokenSource> _thumbJobs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _thumbSkipped = new(StringComparer.Ordinal);
    private readonly Dictionary<Image, AssetItem> _realizedTiles = [];
    private readonly Dictionary<Image, long> _tileTagCallbacks = [];
    private bool _suppressBrowseChrome;
    private bool _suppressMosaicSelection;
    private VirtualKey _mosaicArrow;
    private long _headerGestureAtMs = -1;
    private RangeSelectSession? _rangeSelect;
    private long _rangePressMs;
    private Windows.Foundation.Point _rangePressPoint;
    private uint _rangePointerId;
    private Pointer? _rangePointer;
    private bool _rangeCaptured;
    private bool _rangeConsumedTap;
    private int _rangeConsumedFrom;
    private int _rangeConsumedTo;
    private ViewerOverlayAutoHide? _viewerOverlayAutoHide;

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
        ViewModel.RequestPickMoveDestination = AskMoveDestinationAsync;
        ViewModel.RequestFocusAssignTag = () =>
            DispatcherQueue.TryEnqueue(() =>
            {
                ShowAssignTagsFlyout(preferTagSelectedCta: true);
                AsbAssignTag.Focus(FocusState.Programmatic);
            });
        ViewModel.RequestOpenAssignPanel = () =>
            DispatcherQueue.TryEnqueue(() => ShowAssignTagsFlyout(preferTagSelectedCta: true));
        ViewModel.RequestOpenGalleryWindow = GalleryWindow.Show;
        SrfOverlayStill.ReviewSwipeCommitted += OnOverlayReviewSwipe;
        SrfOverlayStill.LongPressCommitted += OnOverlayStillLongPress;
        // Same ContextFlyout for long-press, right-click, and … (Select is a menu item).
        SrfOverlayStill.ContextFlyout = FlyOverlayAsset;
        MpeOverlay.ContextFlyout = FlyOverlayAsset;
        PerformanceSettings.Changed += PerformanceSettings_Changed;
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
                InvalidateMosaicLayout();
            }

            if (e.PropertyName is nameof(LibraryViewModel.OverlayGallery) or nameof(LibraryViewModel.IsGalleryOverlayOpen))
            {
                HookOverlayGallery();
                UpdateOverlayMedia();
                SyncViewerOverlayAutoHide();
                if (ViewModel.IsGalleryOverlayOpen)
                {
                    GrdGalleryOverlay.Focus(FocusState.Programmatic);
                }
            }

            if (e.PropertyName is nameof(LibraryViewModel.IsReviewMode))
            {
                SrfOverlayStill.IsReviewMode = ViewModel.IsReviewMode;
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
            // NavigationCacheMode keeps this instance. Loaded still runs when the
            // page re-enters the tree after Settings. Do not rebuild the preview
            // or the overlay still on that return.
            if (_shellReady)
            {
                ViewModel.MosaicReset = OnMosaicReset;
                ViewModel.MosaicChunkAppended = OnMosaicChunkAppended;
                CardViewerDetails.AttachInFlow(GrdGalleryOverlay);
                ViewerMediaElement.SuppressFullWindow(MpeOverlay);
                HookOverlayGallery();
                TryHookMosaicScroll();
                LogLibraryShow(pageReused: true);
                _thumbsMayUpgrade = true;
                ScheduleThumbPlan("return");
                return;
            }

            _shellReady = true;
            CardViewerDetails.AttachInFlow(GrdGalleryOverlay);
            ViewerMediaElement.SuppressFullWindow(MpeOverlay);
            _suppressBrowseChrome = true;
            SyncBrowseChromeFromViewModel();
            _suppressBrowseChrome = false;
            await ErrorReporter.RunAsync("Reload mosaic sort", null, ViewModel.ReloadMosaicSortAsync);
            await ErrorReporter.RunAsync("Load tag catalog", null, ViewModel.ReloadTagCatalogAsync);
            UpdatePreview();
            InvalidateMosaicLayout();
            HookOverlayGallery();
            GrdAssets.AddHandler(DoubleTappedEvent, new DoubleTappedEventHandler(GrdAssets_DoubleTapped), true);
            GrdAssets.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(GrdAssets_KeyDown), true);
            GrdAssets.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(GrdAssets_RangePressed), true);
            GrdAssets.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(GrdAssets_RangeMoved), true);
            GrdAssets.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(GrdAssets_RangeReleased), true);
            GrdAssets.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(GrdAssets_RangeCaptureLost), true);
            GrdAssets.AddHandler(UIElement.TappedEvent, new TappedEventHandler(GrdAssets_RangeTapped), true);
            GrdAssets.AddHandler(UIElement.HoldingEvent, new HoldingEventHandler(GrdAssets_RangeHolding), true);
            RefreshRealizedTiles();
            HookFolderTreeExpansion();
            TryHookMosaicScroll();
            LogLibraryShow(pageReused: false);
            if (ViewModel.Assets.Count > 0)
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    _thumbsMayUpgrade = true;
                    ScheduleThumbPlan("library-show");
                });
            }
        };
        Unloaded += (_, _) =>
        {
            PerformanceSettings.Changed -= PerformanceSettings_Changed;
            UnhookFolderTreeExpansion();
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

    private void PerformanceSettings_Changed(object? sender, EventArgs e)
    {
        _thumbQueue.SetCap(ThumbScheduler.EffectiveConcurrencyCap(Environment.ProcessorCount));
        ScheduleThumbPlan("perf-settings");
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

    public static IRelayCommand<TagChipItem> GetToggleFilterChipCommand() => AppServices.Library.ToggleFilterChipCommand;

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

    private static WriteableBitmap? BgraBitmap(byte[] bgra, int width, int height)
    {
        if (width < 1 || height < 1 || bgra.Length < width * height * 4)
        {
            return null;
        }

        var bitmap = new WriteableBitmap(width, height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(bgra, 0, width * height * 4);
        }

        bitmap.Invalidate();
        return bitmap;
    }

    /// <summary>
    /// Decode <paramref name="path"/> off the UI thread into the box, then build the
    /// bitmap on the dispatcher. Tags and Rooms use this instead of <see cref="FileToImage"/>.
    /// </summary>
    public static async Task<ImageSource?> DecodeCappedSourceAsync(
        string? path,
        int boxWidth,
        int boxHeight,
        CancellationToken cancellationToken)
    {
        var shown = PathForPixelDecode(path);
        if (string.IsNullOrEmpty(shown) || AppServices.Thumbnails is null)
        {
            return null;
        }

        var pixels = await AppServices.Thumbnails
            .DecodeCappedBgraAsync(shown, boxWidth, boxHeight, cancellationToken)
            .ConfigureAwait(false);
        if (pixels is null)
        {
            return null;
        }

        var frame = pixels.Value;
        ImageSource? image = null;
        await UiDispatch.RunAsync(() => image = BgraBitmap(frame.Bgra, frame.Width, frame.Height));
        return image;
    }

    /// <summary>EXR / HDR / TGA / Clip / PSD / JXL display the cached JPEG, not the original.</summary>
    public static string? PathForPixelDecode(string? path)
    {
        if (string.IsNullOrEmpty(path) || GalleryMedia.IsRemoteUri(path))
        {
            return null;
        }

        return AppServices.Thumbnails is { } thumbs ? thumbs.DisplayPathFor(path) : path;
    }

    private void TreFolders_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is FolderNode node)
        {
            ViewModel.SelectedFolder = node;
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

    private void InfUndo_PointerEntered(object sender, PointerRoutedEventArgs e) =>
        ViewModel.PauseUndoToastTimer();

    private void InfUndo_PointerExited(object sender, PointerRoutedEventArgs e) =>
        ViewModel.ResumeUndoToastTimer();

    private void InfUndo_FocusEntered(object sender, RoutedEventArgs e) =>
        ViewModel.PauseUndoToastTimer();

    private void InfUndo_FocusExited(object sender, RoutedEventArgs e) =>
        ViewModel.ResumeUndoToastTimer();

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
        DispatcherQueue.TryEnqueue(() => AsbAssignTag.Focus(FocusState.Programmatic));
    }

    private void InvalidateMosaicLayout() => MosaicLayout.Relayout();

    private void TglSelectMode_Changed(object sender, RoutedEventArgs e)
    {
        var on = (sender as ToggleButton)?.IsChecked == true;
        _suppressMosaicSelection = true;
        try
        {
            GrdAssets.SelectionMode = SelectMode.ClickTogglesTile(on)
                ? ItemsViewSelectionMode.Multiple
                : ItemsViewSelectionMode.Single;
            if (on)
            {
                RestoreMosaicSelectionHighlights();
            }
        }
        finally
        {
            _suppressMosaicSelection = false;
        }
    }

    private void RestoreMosaicSelectionHighlights()
    {
        var assets = ViewModel.Assets;
        for (var i = 0; i < assets.Count; i++)
        {
            var want = !assets[i].IsFolderHeader && assets[i].IsSelected;
            if (want == GrdAssets.IsSelected(i))
            {
                continue;
            }

            if (want)
            {
                GrdAssets.Select(i);
            }
            else
            {
                GrdAssets.Deselect(i);
            }
        }
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

    private static RangeSelectPointer ToRangePointer(PointerDeviceType type) => type switch
    {
        PointerDeviceType.Touch => RangeSelectPointer.Touch,
        PointerDeviceType.Pen => RangeSelectPointer.Pen,
        PointerDeviceType.Mouse => RangeSelectPointer.Mouse,
        _ => RangeSelectPointer.Other
    };

    private void GrdAssets_RangePressed(object sender, PointerRoutedEventArgs e)
    {
        _rangeConsumedTap = false;
        _lastPressWasMouse = e.Pointer.PointerDeviceType == PointerDeviceType.Mouse;
        _lastPressWasTouch = e.Pointer.PointerDeviceType == PointerDeviceType.Touch;
        // CanDrag on ItemContainer steals touch pan — arm only for mouse folder-drag.
        if (FindAncestor<ItemContainer>(e.OriginalSource as DependencyObject) is { } dragContainer)
        {
            var dragItem = FindAssetItem(dragContainer);
            dragContainer.CanDrag = dragItem is not null
                && AssetDrag.ArmsItemContainerCanDrag(
                    _lastPressWasMouse,
                    ViewModel.IsSelectMode,
                    dragItem.IsSelected,
                    dragItem.IsFolderHeader,
                    ViewModel.IsGalleryOverlayOpen);
        }

        if (!ViewModel.IsSelectMode || ViewModel.IsGalleryOverlayOpen)
        {
            return;
        }

        var point = e.GetCurrentPoint(GrdAssets);
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var item = FindAssetItem(e.OriginalSource);
        if (item is null
            || !RangeSelect.AllowsGesture(true, ViewModel.IsGalleryOverlayOpen, item.IsFolderHeader))
        {
            return;
        }

        // Pressing an already selected tile with the mouse drags the selection to a folder instead.
        if (AssetDrag.YieldsRangeSelect(_lastPressWasMouse, item.IsSelected))
        {
            return;
        }

        var index = ViewModel.Assets.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        _rangeSelect = new RangeSelectSession(ToRangePointer(e.Pointer.PointerDeviceType), index);
        _rangePressMs = Environment.TickCount64;
        _rangePressPoint = point.Position;
        _rangePointerId = e.Pointer.PointerId;
        _rangePointer = e.Pointer;
        _rangeCaptured = false;
    }

    private void GrdAssets_RangeMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_rangeSelect is null || e.Pointer.PointerId != _rangePointerId)
        {
            return;
        }

        var point = e.GetCurrentPoint(GrdAssets);
        var dx = point.Position.X - _rangePressPoint.X;
        var dy = point.Position.Y - _rangePressPoint.Y;
        var elapsed = (int)(Environment.TickCount64 - _rangePressMs);
        if (!_rangeSelect.IsActive)
        {
            if (RangeSelect.CancelsHold(_rangeSelect.Pointer, elapsed, dx, dy))
            {
                EndRangeSelect(e.Pointer);
                return;
            }

            if (!_rangeSelect.TryActivate(elapsed, dx, dy))
            {
                return;
            }
        }

        if (_rangeSelect.RequiresPointerCapture)
        {
            CaptureRangePointer(e.Pointer);
        }

        ApplyRangeHover(e, liveDrag: true);
        e.Handled = true;
    }

    private void GrdAssets_RangeHolding(object sender, HoldingRoutedEventArgs e)
    {
        if (ViewModel.IsGalleryOverlayOpen)
        {
            return;
        }

        if (!ViewModel.IsSelectMode)
        {
            if (e.HoldingState != HoldingState.Started)
            {
                return;
            }

            var holdItem = FindAssetItem(e.OriginalSource);
            if (holdItem is null
                || !SelectMode.EnterFromHold(false, ViewModel.IsGalleryOverlayOpen, holdItem.IsFolderHeader))
            {
                return;
            }

            ViewModel.SelectAsset(holdItem, toggle: false);
            ViewModel.IsSelectMode = true;
            RestoreMosaicSelectionHighlights();
            // Touch long-press = select; context menu is "…" / right-click only.
            if (_lastPressWasTouch)
            {
                _suppressTouchContextFlyout = true;
            }

            e.Handled = true;
            return;
        }

        if (_rangeSelect is null)
        {
            return;
        }

        if (e.HoldingState == HoldingState.Canceled && !_rangeSelect.IsActive)
        {
            EndRangeSelect(null);
            return;
        }

        if (e.HoldingState != HoldingState.Started)
        {
            return;
        }

        var elapsed = (int)(Environment.TickCount64 - _rangePressMs);
        if (!_rangeSelect.TryActivateHold(elapsed))
        {
            return;
        }

        CaptureRangePointer(_rangePointer);
        ApplyRangeIndexes(_rangeSelect.Highlight(MosaicHeaderFlags(), _rangeSelect.AnchorIndex), liveDrag: true);
        e.Handled = true;
    }

    private void GrdAssets_RangeReleased(object sender, PointerRoutedEventArgs e)
    {
        // Disarm after mouse press so a recycled tile does not keep CanDrag for touch.
        if (FindAncestor<ItemContainer>(e.OriginalSource as DependencyObject) is { } released)
        {
            released.CanDrag = false;
        }

        if (_rangeSelect is null || e.Pointer.PointerId != _rangePointerId)
        {
            return;
        }

        if (_rangeSelect.IsActive)
        {
            ApplyRangeHover(e, liveDrag: false);
            _rangeConsumedTap = true;
            _rangeConsumedFrom = _rangeSelect.AnchorIndex;
            _rangeConsumedTo = _rangeSelect.EndIndex;
            e.Handled = true;
        }

        EndRangeSelect(e.Pointer);
    }

    private void GrdAssets_RangeTapped(object sender, TappedRoutedEventArgs e)
    {
        if (!_rangeConsumedTap)
        {
            return;
        }

        _rangeConsumedTap = false;
        var indexes = RangeSelect.ContiguousIndexes(MosaicHeaderFlags(), _rangeConsumedFrom, _rangeConsumedTo);
        ApplyRangeIndexes(indexes, liveDrag: false, _rangeConsumedFrom, _rangeConsumedTo);
    }

    private void GrdAssets_RangeCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (FindAncestor<ItemContainer>(e.OriginalSource as DependencyObject) is { } lost)
        {
            lost.CanDrag = false;
        }

        if (_rangeSelect is not null && e.Pointer.PointerId == _rangePointerId)
        {
            if (_rangeSelect.IsActive)
            {
                var from = _rangeSelect.AnchorIndex;
                var to = _rangeSelect.EndIndex;
                var indexes = RangeSelect.ContiguousIndexes(MosaicHeaderFlags(), from, to);
                EndRangeSelect(null);
                ApplyRangeIndexes(indexes, liveDrag: false, from, to);
                return;
            }

            EndRangeSelect(null);
        }
    }

    private void ApplyRangeHover(PointerRoutedEventArgs e, bool liveDrag)
    {
        if (_rangeSelect is null)
        {
            return;
        }

        var hover = HitMosaicItem(e);
        var hoverIndex = hover is null ? _rangeSelect.EndIndex : ViewModel.Assets.IndexOf(hover);
        ApplyRangeIndexes(_rangeSelect.Highlight(MosaicHeaderFlags(), hoverIndex), liveDrag);
    }

    private IReadOnlyList<bool> MosaicHeaderFlags() =>
        ViewModel.Assets.Select(asset => asset.IsFolderHeader).ToList();

    private void ApplyRangeIndexes(IReadOnlyList<int> indexes, bool liveDrag, int? from = null, int? to = null)
    {
        _suppressMosaicSelection = true;
        try
        {
            var want = indexes.ToHashSet();
            for (var i = 0; i < ViewModel.Assets.Count; i++)
            {
                var on = want.Contains(i);
                if (on == GrdAssets.IsSelected(i))
                {
                    continue;
                }

                if (on)
                {
                    GrdAssets.Select(i);
                }
                else
                {
                    GrdAssets.Deselect(i);
                }
            }
        }
        finally
        {
            _suppressMosaicSelection = false;
        }

        if (from is not null && to is not null)
        {
            ViewModel.ApplyMosaicRange(from.Value, to.Value, RangeSelect.LoadsPreview(liveDrag));
        }
        else if (_rangeSelect is not null)
        {
            ViewModel.ApplyMosaicRange(
                _rangeSelect.AnchorIndex,
                _rangeSelect.EndIndex,
                RangeSelect.LoadsPreview(liveDrag));
        }
    }

    private AssetItem? HitMosaicItem(PointerRoutedEventArgs e)
    {
        var local = e.GetCurrentPoint(GrdAssets).Position;
        var origin = GrdAssets.TransformToVisual(null).TransformPoint(default);
        var (x, y) = RangeSelect.ToWindowPoint(local.X, local.Y, origin.X, origin.Y);
        var windowPoint = new Windows.Foundation.Point(x, y);
        foreach (var hit in VisualTreeHelper.FindElementsInHostCoordinates(windowPoint, GrdAssets))
        {
            var item = FindAssetItem(hit);
            if (item is not null)
            {
                return item;
            }
        }

        return FindAssetItem(e.OriginalSource);
    }

    private void CaptureRangePointer(Pointer? pointer)
    {
        pointer ??= _rangePointer;
        if (pointer is null || _rangeCaptured)
        {
            return;
        }

        _rangeCaptured = GrdAssets.CapturePointer(pointer);
        _rangePointer = pointer;
    }

    private void EndRangeSelect(Pointer? pointer)
    {
        var release = pointer ?? _rangePointer;
        _rangeSelect = null;
        _rangePointerId = 0;
        _rangePointer = null;
        _rangeCaptured = false;

        if (release is not null)
        {
            try
            {
                GrdAssets.ReleasePointerCapture(release);
            }
            catch (ArgumentException)
            {
                // Not captured.
            }
        }
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

        if (GalleryScale.ClosesDetailsFirst(
                ViewModel.OverlayGallery?.ShowDetails == true,
                (int)e.Key)
            && ViewModel.OverlayGallery?.TryCloseDetailsFirst() == true)
        {
            BtnGalleryImageInfo.Focus(FocusState.Programmatic);
            e.Handled = true;
            return;
        }

        if (GalleryScale.ClosesOverlay(ViewModel.IsGalleryOverlayOpen, (int)e.Key))
        {
            ViewModel.CloseOverlayCommand.Execute(null);
            e.Handled = true;
            return;
        }

        var typing = FocusIsTextInput(e.OriginalSource);
        var item = FindAssetItem(e.OriginalSource)
            ?? ViewModel.SelectedAsset
            ?? ViewModel.Assets.FirstOrDefault(asset => !asset.IsFolderHeader);

        if (item is { IsFolderHeader: true }
            && e.Key == VirtualKey.Enter
            && !ViewModel.IsGalleryOverlayOpen
            && !typing)
        {
            ViewModel.SelectFolderGroup(item);
            e.Handled = true;
            return;
        }

        var openSpace = GalleryMedia.ShouldOpenOverlayFromSpace(
            ViewModel.IsGalleryOverlayOpen,
            typing,
            item?.IsFolderHeader == true,
            (int)e.Key);
        var openEnter = GalleryMedia.ShouldOpenOverlayFromEnter(
            ViewModel.IsGalleryOverlayOpen,
            typing,
            item?.IsFolderHeader == true,
            (int)e.Key);
        if (!openSpace && !openEnter)
        {
            return;
        }

        if (item is null || item.IsFolderHeader)
        {
            return;
        }

        OpenOverlayFor(item);
        e.Handled = true;
    }

    private void GrdAssets_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!e.Handled && TryHandleMosaicMoveKey(e))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Double-tap, Enter, and UIA InvokePattern (not a single pointer tap while
    /// SelectionMode is Single/Multiple — see ItemsView.CanRaiseItemInvoked).
    /// </summary>
    private void GrdAssets_ItemInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs e)
    {
        if (e.InvokedItem is not AssetItem item)
        {
            return;
        }

        if (item.IsFolderHeader)
        {
            ViewModel.SelectFolderGroup(item);
            return;
        }

        if (!GalleryMedia.ShouldOpenOverlayFromItemInvoke(
                ViewModel.IsGalleryOverlayOpen,
                item.IsFolderHeader))
        {
            return;
        }

        OpenOverlayFor(item);
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
        _suppressTouchContextFlyout = false;
        var item = FindAssetItem(sender) ?? FindAssetItem(e.OriginalSource);
        if (item is null || !TouchGestures.OpensContextFromRightClick(item.IsFolderHeader))
        {
            e.Handled = true;
            return;
        }

        EnsureSelectedForContext(item);
    }

    private void AssetItem_ContextRequested(object sender, ContextRequestedEventArgs e)
    {
        // Touch long-press opens select mode, not the context menu.
        if (_lastPressWasTouch || _suppressTouchContextFlyout || TouchGestures.OpensContextFromTouchHold())
        {
            _suppressTouchContextFlyout = false;
            e.Handled = true;
            return;
        }

        var item = FindAssetItem(sender) ?? FindAssetItem(e.OriginalSource);
        if (item is null || !TouchGestures.OpensContextFromRightClick(item.IsFolderHeader))
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

        if (_suppressTouchContextFlyout || _lastPressWasTouch)
        {
            _suppressTouchContextFlyout = false;
            flyout.Hide();
            return;
        }

        var item = flyout.Target is FrameworkElement target ? FindAssetItem(target) : null;
        if (item is null || !TouchGestures.OpensContextFromRightClick(item.IsFolderHeader))
        {
            flyout.Hide();
            return;
        }

        EnsureSelectedForContext(item);
        PopulateMoveRecents(flyout);
    }

    private void AssetMore_Click(object sender, RoutedEventArgs e)
    {
        _suppressTouchContextFlyout = false;
        _lastPressWasTouch = false;
        if (sender is not FrameworkElement fe)
        {
            return;
        }

        var item = FindAssetItem(fe);
        if (item is null || !TouchGestures.OpensContextFromRightClick(item.IsFolderHeader))
        {
            return;
        }

        EnsureSelectedForContext(item);
        var container = FindAncestor<ItemContainer>(fe);
        if (container?.ContextFlyout is FlyoutBase flyout)
        {
            flyout.ShowAt(fe);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        for (var cur = start; cur is not null; cur = VisualTreeHelper.GetParent(cur))
        {
            if (cur is T match)
            {
                return match;
            }
        }

        return null;
    }

    private void GalleryOverlay_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _lastPressWasTouch = e.Pointer.PointerDeviceType == PointerDeviceType.Touch;
        _lastPressWasMouse = e.Pointer.PointerDeviceType == PointerDeviceType.Mouse;
    }

    private void OnOverlayReviewSwipe(TouchGestures.SwipeAction action)
    {
        // Surface already classified with IsReviewMode; ViewModel commands
        // also gate so browse never moves files even if a Review action leaks.
        switch (action)
        {
            case TouchGestures.SwipeAction.BrowsePrevious:
                ViewModel.OverlayGallery?.GoPreviousCommand.Execute(null);
                break;
            case TouchGestures.SwipeAction.BrowseNext:
                ViewModel.OverlayGallery?.GoNextCommand.Execute(null);
                break;
            case TouchGestures.SwipeAction.Reject:
                ViewModel.ReviewRejectCommand.Execute(null);
                break;
            case TouchGestures.SwipeAction.Keep:
                ViewModel.ReviewKeepCommand.Execute(null);
                break;
            case TouchGestures.SwipeAction.Skip:
                ViewModel.ReviewSkipCommand.Execute(null);
                break;
        }
    }

    private void GalleryOverlayVideo_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!TouchGestures.IsTouchOnly(_lastPressWasTouch)
            || !ViewerSettings.TouchGesturesEnabled
            || MpeOverlay.MediaPlayer?.PlaybackSession is not { } session)
        {
            return;
        }

        var pos = e.GetPosition(MpeOverlay);
        var height = MpeOverlay.ActualHeight;
        var yFromBottom = height - pos.Y;
        var controlsVisible = MpeOverlay.TransportControls is not null;
        if (TouchGestures.BlocksGestureOverVideoControls(controlsVisible, yFromBottom))
        {
            return;
        }

        var zone = TouchGestures.ClassifyVideoDoubleTap(pos.X, MpeOverlay.ActualWidth);
        if (zone == TouchGestures.VideoDoubleTapZone.CenterZoom)
        {
            // Video center: play/pause (tap already does); no still zoom cycle.
            return;
        }

        var delta = TimeSpan.FromSeconds(
            zone == TouchGestures.VideoDoubleTapZone.LeftSeekBack
                ? -TouchGestures.VideoSeekSeconds
                : TouchGestures.VideoSeekSeconds);
        var next = session.Position + delta;
        if (next < TimeSpan.Zero)
        {
            next = TimeSpan.Zero;
        }

        // PlaybackSession.NaturalDuration is already a TimeSpan (not WinRT Duration).
        var duration = session.NaturalDuration;
        if (duration > TimeSpan.Zero && next > duration)
        {
            next = duration;
        }

        session.Position = next;
        e.Handled = true;
    }

    private double _videoSwipeStartX;
    private double _videoSwipeStartY;
    private long _videoSwipeStartMs;
    private uint _videoSwipePointerId;
    private bool _videoSwipeTracking;
    private bool _videoSwipeCaptured;

    private bool VideoBrowseSwipeAllowedNow()
    {
        var playing = MpeOverlay.MediaPlayer?.PlaybackSession?.PlaybackState
            == Windows.Media.Playback.MediaPlaybackState.Playing;
        return TouchGestures.AllowsVideoBrowseSwipe(
            ViewModel.OverlayGallery?.IsVideo == true,
            playing,
            ViewerSettings.TouchGesturesEnabled);
    }

    private void GrdVideoBrowseSwipe_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Touch || !VideoBrowseSwipeAllowedNow())
        {
            return;
        }

        var point = e.GetCurrentPoint(GrdVideoBrowseSwipe);
        _videoSwipeTracking = true;
        _videoSwipeCaptured = false;
        _videoSwipePointerId = e.Pointer.PointerId;
        _videoSwipeStartX = point.Position.X;
        _videoSwipeStartY = point.Position.Y;
        _videoSwipeStartMs = Environment.TickCount64;
        // Do not capture / Handled yet — preserve double-tap seek until drag exceeds slop.
    }

    private void GrdVideoBrowseSwipe_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_videoSwipeTracking || e.Pointer.PointerId != _videoSwipePointerId)
        {
            return;
        }

        var point = e.GetCurrentPoint(GrdVideoBrowseSwipe);
        var dx = point.Position.X - _videoSwipeStartX;
        var dy = point.Position.Y - _videoSwipeStartY;
        if (!TouchGestures.ExceedsSlop(dx, dy))
        {
            return;
        }

        if (!_videoSwipeCaptured)
        {
            _videoSwipeCaptured = GrdVideoBrowseSwipe.CapturePointer(e.Pointer);
        }

        e.Handled = true;
    }

    private void GrdVideoBrowseSwipe_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_videoSwipeTracking || e.Pointer.PointerId != _videoSwipePointerId)
        {
            return;
        }

        var point = e.GetCurrentPoint(GrdVideoBrowseSwipe);
        var dx = point.Position.X - _videoSwipeStartX;
        var dy = point.Position.Y - _videoSwipeStartY;
        var elapsedMs = Math.Max(1, Environment.TickCount64 - _videoSwipeStartMs);
        var vx = dx / (double)elapsedMs;
        var hadCapture = _videoSwipeCaptured;
        EndVideoBrowseSwipeTracking(e.Pointer);

        if (!hadCapture || !VideoBrowseSwipeAllowedNow())
        {
            return;
        }

        // Catcher Margin already clears VideoControlsDeadZoneDip so transport/seek stay hittable.
        var action = TouchGestures.ClassifyBrowseSwipe(
            dx, dy, GrdVideoBrowseSwipe.ActualWidth, vx);
        if (action is TouchGestures.SwipeAction.BrowsePrevious
            or TouchGestures.SwipeAction.BrowseNext)
        {
            OnOverlayReviewSwipe(action);
            e.Handled = true;
        }
    }

    private void GrdVideoBrowseSwipe_PointerCanceled(object sender, PointerRoutedEventArgs e) =>
        EndVideoBrowseSwipeTracking(e.Pointer);

    private void GrdVideoBrowseSwipe_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId == _videoSwipePointerId)
        {
            _videoSwipeTracking = false;
            _videoSwipeCaptured = false;
            _videoSwipePointerId = 0;
        }
    }

    private void EndVideoBrowseSwipeTracking(Pointer? pointer)
    {
        var captured = _videoSwipeCaptured;
        _videoSwipeTracking = false;
        _videoSwipeCaptured = false;
        _videoSwipePointerId = 0;
        if (!captured || pointer is null)
        {
            return;
        }

        try
        {
            GrdVideoBrowseSwipe.ReleasePointerCapture(pointer);
        }
        catch
        {
            // Capture may already be released.
        }
    }

    private void SyncVideoBrowseSwipeHitTest()
    {
        // Bottom inset clears seek bar + transport (hit-test does not fall through siblings).
        GrdVideoBrowseSwipe.Margin = new Thickness(0, 0, 0, TouchGestures.VideoControlsDeadZoneDip);
        // XAML defaults True; only clear while Playing so transport/seek get taps.
        GrdVideoBrowseSwipe.IsHitTestVisible = VideoBrowseSwipeAllowedNow();
        if (!GrdVideoBrowseSwipe.IsHitTestVisible)
        {
            _videoSwipeTracking = false;
            _videoSwipeCaptured = false;
            _videoSwipePointerId = 0;
        }
    }

    private void OverlayPlayback_StateChanged(
        Windows.Media.Playback.MediaPlaybackSession sender,
        object args) =>
        DispatcherQueue.TryEnqueue(SyncVideoBrowseSwipeHitTest);

    private void GalleryOverlay_Holding(object sender, HoldingRoutedEventArgs e)
    {
        // Fallback when Holding reaches the overlay chrome (still uses LongPressCommitted
        // because Manipulation cancels Holding on the image).
        if (e.HoldingState != HoldingState.Started
            || !TouchGestures.IsTouchOnly(_lastPressWasTouch)
            || !ViewerSettings.TouchGesturesEnabled
            || !SelectMode.ViewerHoldOpensContext(
                ViewModel.IsGalleryOverlayOpen, ViewModel.IsReviewMode))
        {
            return;
        }

        ShowOverlayAssetContextFlyout(BtnGalleryMore);
        e.Handled = true;
    }

    private void OnOverlayStillLongPress()
    {
        if (!ViewerSettings.TouchGesturesEnabled
            || !SelectMode.ViewerHoldOpensContext(
                ViewModel.IsGalleryOverlayOpen, ViewModel.IsReviewMode))
        {
            return;
        }

        ShowOverlayAssetContextFlyout(SrfOverlayStill);
    }

    private void MnuOverlaySelect_Click(object sender, RoutedEventArgs e) =>
        ViewModel.SelectOverlayCurrentFromMenu();

    private void OverlayAssetMenu_Opening(object sender, object e)
    {
        if (ViewModel.OverlayGallery?.Current is not { IsFolderHeader: false } current)
        {
            if (sender is MenuFlyout flyout)
            {
                flyout.Hide();
            }

            return;
        }

        // Commands act on Library selection — keep overlay current selected.
        EnsureSelectedForContext(current);
    }

    private void ShowOverlayAssetContextFlyout(FrameworkElement anchor)
    {
        if (ViewModel.OverlayGallery?.Current is not { IsFolderHeader: false } current)
        {
            return;
        }

        EnsureSelectedForContext(current);
        FlyOverlayAsset.ShowAt(anchor);
    }

    private void SyncViewerOverlayAutoHide()
    {
        if (ViewModel.IsGalleryOverlayOpen)
        {
            _viewerOverlayAutoHide ??= new ViewerOverlayAutoHide(
                GrdGalleryOverlay,
                GrdViewerOverlayNav,
                BtnGalleryPrev,
                BtnGalleryNext,
                BtnGalleryClose,
                BtnGalleryImageInfo);
            _viewerOverlayAutoHide.Attach();
        }
        else
        {
            _viewerOverlayAutoHide?.Detach();
        }
    }

    private ViewerFocusZone _viewerFocusZone = ViewerFocusZone.Image;


    private void GalleryOverlay_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel.OverlayGallery is null)
        {
            return;
        }

        _viewerOverlayAutoHide?.Show(resetIdle: true);

        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);
        var alt = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)
            .HasFlag(CoreVirtualKeyStates.Down);
        if (ViewModel.OverlayGallery.TryHandleViewerShortcut(control, alt, (int)e.Key))
        {
            if (GalleryScale.TogglesDetails(control, alt, (int)e.Key))
            {
                if (ViewModel.OverlayGallery.ShowDetails)
                {
                    CardViewerDetails.FocusCard();
                    _viewerFocusZone = ViewerFocusZone.Details;
                }
                else
                {
                    BtnGalleryImageInfo.Focus(FocusState.Programmatic);
                    _viewerFocusZone = ViewerFocusZone.Controls;
                }
            }

            e.Handled = true;
            return;
        }

        if (GalleryScale.CyclesViewerFocus((int)e.Key))
        {
            CycleViewerFocus();
            e.Handled = true;
            return;
        }

        if (GalleryScale.PassesViewerSpace(true, (int)e.Key))
        {
            return;
        }

        if (GifFrames.PassesGifSliderArrows(FocusIsGifSlider(e.OriginalSource), (int)e.Key))
        {
            return;
        }

        if (GalleryVolume.PassesVolumeFocusKeys(VolOverlay.OwnsFocus(e.OriginalSource), (int)e.Key))
        {
            return;
        }

        if (VolOverlay.TryHandleViewerKey(
                ViewModel.OverlayGallery.IsVideo,
                FocusIsTextInput(e.OriginalSource),
                shift,
                e.Key))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Escape:
                if (ViewModel.OverlayGallery.TryCloseDetailsFirst())
                {
                    BtnGalleryImageInfo.Focus(FocusState.Programmatic);
                    _viewerFocusZone = ViewerFocusZone.Controls;
                    e.Handled = true;
                    break;
                }

                ViewModel.CloseOverlayCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Left:
                if (ViewerFocusCycle.AllowsArrowNavigate(_viewerFocusZone))
                {
                    ViewModel.OverlayGallery.GoPreviousCommand.Execute(null);
                    e.Handled = true;
                }

                break;
            case VirtualKey.Right:
                if (ViewerFocusCycle.AllowsArrowNavigate(_viewerFocusZone))
                {
                    ViewModel.OverlayGallery.GoNextCommand.Execute(null);
                    e.Handled = true;
                }

                break;
        }
    }

    private void CycleViewerFocus()
    {
        _viewerFocusZone = ViewerFocusCycle.Next(
            _viewerFocusZone,
            ViewModel.OverlayGallery?.ShowDetails == true);
        switch (_viewerFocusZone)
        {
            case ViewerFocusZone.Controls:
                BtnGalleryImageInfo.Focus(FocusState.Programmatic);
                break;
            case ViewerFocusZone.Details:
                CardViewerDetails.FocusCard();
                break;
            default:
                GrdGalleryOverlay.Focus(FocusState.Programmatic);
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

    private static bool FocusIsGifSlider(object? source)
    {
        for (var current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement element
                && GifFrames.IsGifFrameSlider(AutomationProperties.GetAutomationId(element)))
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
        _thumbSkipped.Clear();
        foreach (var id in _thumbQueue.CancelOutstanding())
        {
            if (_thumbJobs.TryGetValue(id, out var cts))
            {
                cts.Cancel();
            }
        }

        _thumbJobs.Clear();
        _realizedTiles.Clear();
        InvalidateMosaicLayout();
    }

    private void OnMosaicChunkAppended()
    {
        if (!IsLoaded)
        {
            return;
        }

        InvalidateMosaicLayout();
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
            ScheduleThumbPlan("chunk");
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
        if (item.ThumbImage is not null)
        {
            _thumbQueue.MarkSatisfied(item.Id);
            return;
        }

        // Never start StorageFile / shell-thumb work during ItemsRepeater Measure
        // (Tag bind → OnTileTagChanged). GetFileFromPathAsync pumps the STA and
        // WinUI FailFastWithStowedExceptions (0xc000027b) — see dump PID 155568.
        // The queue runs later, off this measure pass, in viewport order.
        ScheduleThumbPlan("realize");
    }

    private void ScheduleThumbPlan(string reason)
    {
        if (!_thumbsMayUpgrade && reason is not ("return" or "library-show"))
        {
            return;
        }

        _planReason = reason;
        if (_planQueued)
        {
            return;
        }

        _planQueued = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _planQueued = false;
            if (!_thumbsMayUpgrade && _planReason is not ("return" or "library-show"))
            {
                return;
            }

            ApplyThumbPlan(_planReason);
        });
    }

    private void ApplyThumbPlan(string reason)
    {
        if (!_thumbsMayUpgrade && reason != "return")
        {
            return;
        }

        TryHookMosaicScroll();
        var width = GrdAssets.ActualWidth;
        if (width < 1 || ViewModel.Assets.Count == 0)
        {
            return;
        }

        var scale = RasterScale();
        var viewport = CurrentThumbViewport();
        var specs = new (bool IsHeader, double Aspect)[ViewModel.Assets.Count];
        for (var i = 0; i < ViewModel.Assets.Count; i++)
        {
            var asset = ViewModel.Assets[i];
            specs[i] = (asset.IsFolderHeader, asset.AspectRatio);
        }

        var slots = MosaicRows.Layout(specs, width, ViewModel.MosaicRowHeight, 8, GalleryMedia.FolderHeaderHeight);
        var tiles = new List<ThumbTile>();
        var cacheHits = 0;
        var count = Math.Min(ViewModel.Assets.Count, slots.Count);
        for (var i = 0; i < count; i++)
        {
            var item = ViewModel.Assets[i];
            if (item.IsFolderHeader || string.IsNullOrEmpty(item.Id) || _thumbSkipped.Contains(item.Id))
            {
                continue;
            }

            if (item.ThumbImage is not null)
            {
                _thumbQueue.MarkSatisfied(item.Id);
                continue;
            }

            var slot = slots[i];
            var (decodeWidth, decodeHeight) = ThumbScheduler.DecodePixels(slot.Width, slot.Height, scale);
            var cacheKey = ThumbMemoryCache<ImageSource>.KeyFor(item.ThumbPath, decodeWidth, decodeHeight);
            if (!string.IsNullOrEmpty(cacheKey) && _thumbMemory.TryGet(cacheKey, out var cached) && cached is not null)
            {
                item.ThumbImage = cached;
                item.ThumbLoadStarted = true;
                _thumbQueue.MarkSatisfied(item.Id);
                cacheHits++;
                continue;
            }

            // ThumbLoadStarted is true while a job is in flight. Keep the tile in
            // the wanted set so a later plan does not cancel it. The queue will
            // not start a second copy.
            if (!GalleryMedia.ShouldLoadTileThumb(
                    item.IsOrphan,
                    alreadyStarted: false,
                    item.ThumbImage is not null,
                    item.ThumbPath,
                    item.ContentHash))
            {
                continue;
            }

            tiles.Add(new ThumbTile(item.Id, slot.X, slot.Y, slot.Width, slot.Height));
        }

        var plan = _thumbQueue.Plan(tiles, viewport, ThumbScheduler.PrefetchBandDip);
        foreach (var id in plan.Cancel)
        {
            CancelThumbJob(id);
        }

        foreach (var id in plan.Start)
        {
            var item = FindAssetById(id);
            if (item is null)
            {
                _thumbQueue.Abandon(id);
                continue;
            }

            var index = ViewModel.Assets.IndexOf(item);
            var slot = (uint)index < (uint)slots.Count
                ? slots[index]
                : new MosaicSlot(0, 0, ViewModel.MosaicRowHeight, ViewModel.MosaicRowHeight);
            StartThumbJob(item, slot, scale);
        }

        var now = Environment.TickCount64;
        var interesting = plan.Start.Count > 0 || plan.Cancel.Count > 0 || cacheHits > 0 || reason is "return" or "scroll";
        if (interesting && (reason != "scroll" || now - _lastPlanLogMs >= 200))
        {
            _lastPlanLogMs = now;
            ThumbTimingLog.Write(ThumbTimingLog.PlanLine(
                reason,
                plan.InFlight,
                plan.Cap,
                plan.Start.Count,
                plan.Cancel.Count,
                cacheHits,
                Environment.CurrentManagedThreadId));
        }
    }

    private void StartThumbJob(AssetItem item, MosaicSlot slot, double scale)
    {
        if (_thumbJobs.ContainsKey(item.Id))
        {
            // The previous decode is still winding down and no longer holds a slot.
            _thumbQueue.TryAbandon(item.Id, _thumbQueue.TicketFor(item.Id));
            return;
        }

        var (decodeWidth, decodeHeight) = ThumbScheduler.DecodePixels(slot.Width, slot.Height, scale);
        var cacheKey = ThumbMemoryCache<ImageSource>.KeyFor(item.ThumbPath, decodeWidth, decodeHeight);
        if (!string.IsNullOrEmpty(cacheKey) && _thumbMemory.TryGet(cacheKey, out var cached) && cached is not null)
        {
            item.ThumbImage = cached;
            item.ThumbLoadStarted = true;
            _thumbQueue.Complete(item.Id);
            var thread = Environment.CurrentManagedThreadId;
            ThumbTimingLog.Write(ThumbTimingLog.TileLine(
                "hit",
                "hit",
                0,
                decodeWidth,
                decodeHeight,
                _thumbQueue.InFlight,
                _thumbQueue.Cap,
                thread,
                thread,
                thread));
            ScheduleThumbPlan("cache-hit");
            return;
        }

        item.ThumbLoadStarted = true;
        var ticket = _thumbQueue.TicketFor(item.Id);
        var cts = new CancellationTokenSource();
        _thumbJobs[item.Id] = cts;
        var filePath = item.Path;
        var hash = item.ContentHash;
        var kind = item.Kind;
        var orphan = item.IsOrphan;
        var online = item.IsOnlineOnly || CloudFile.IsOnlineOnly(item.Path);
        var thumbPath = item.ThumbPath;
        var job = LoadThumbJobAsync(item, cts, ticket, filePath, hash, kind, orphan, online, thumbPath, decodeWidth, decodeHeight);
        _ = job.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Dispose(),
            cts,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task LoadThumbJobAsync(
        AssetItem item,
        CancellationTokenSource cts,
        int ticket,
        string filePath,
        string? hash,
        AssetKind kind,
        bool orphan,
        bool online,
        string? thumbPath,
        int decodeWidth,
        int decodeHeight)
    {
        var startThread = Environment.CurrentManagedThreadId;
        var disk = string.IsNullOrEmpty(thumbPath) ? "generate" : "hit";
        var decodeMs = 0.0;
        var decodeThread = startThread;
        ThumbDecodeOutcome outcome;
        try
        {
            outcome = await ThumbDecodeRun.RunAsync(cts.Token, async token =>
        {
            var path = thumbPath;
            var sw = Stopwatch.StartNew();
            if (GalleryMedia.ShouldRequestMosaicThumb(orphan, filePath, hash, path, online))
            {
                disk = "generate";
                var generated = await AppServices.Thumbnails
                    .EnsureThumbnailAsync(filePath, hash, kind, cancellationToken: token)
                    .ConfigureAwait(false);
                path = generated?.Path ?? path;
            }

            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(path))
            {
                await UiDispatch.RunAsync(() =>
                {
                    if (OwnsThumbJob(item.Id, cts))
                    {
                        _thumbSkipped.Add(item.Id);
                    }
                });
                return;
            }

            var shown = PathForPixelDecode(path) ?? path;
            var decoded = await AppServices.Thumbnails
                .DecodeCappedBgraAsync(shown, decodeWidth, decodeHeight, token)
                .ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            decodeMs = sw.Elapsed.TotalMilliseconds;
            decodeThread = Environment.CurrentManagedThreadId;
            if (decoded is null)
            {
                await UiDispatch.RunAsync(() =>
                {
                    if (OwnsThumbJob(item.Id, cts))
                    {
                        _thumbSkipped.Add(item.Id);
                    }
                });
                return;
            }

            var pixels = decoded.Value;
            await UiDispatch.RunAsync(() =>
            {
                if (!OwnsThumbJob(item.Id, cts) || token.IsCancellationRequested || item.ThumbImage is not null)
                {
                    if (item.ThumbImage is not null && OwnsThumbJob(item.Id, cts))
                    {
                        _thumbJobs.Remove(item.Id);
                        _thumbQueue.TryComplete(item.Id, ticket);
                    }

                    return;
                }

                var bitmap = BgraBitmap(pixels.Bgra, pixels.Width, pixels.Height);
                if (bitmap is null)
                {
                    _thumbSkipped.Add(item.Id);
                    return;
                }

                item.ThumbPath = shown;
                _thumbMemory.Set(ThumbMemoryCache<ImageSource>.KeyFor(shown, decodeWidth, decodeHeight), bitmap);
                item.ThumbImage = bitmap;
                item.ThumbLoadStarted = true;
                _thumbJobs.Remove(item.Id);
                _thumbQueue.TryComplete(item.Id, ticket);
                ThumbTimingLog.Write(ThumbTimingLog.TileLine(
                    "decode",
                    disk,
                    decodeMs,
                    pixels.Width,
                    pixels.Height,
                    _thumbQueue.InFlight,
                    _thumbQueue.Cap,
                    startThread,
                    decodeThread,
                    Environment.CurrentManagedThreadId));
            });
            }).ConfigureAwait(false);
        }
        catch (Exception)
        {
            outcome = new ThumbDecodeOutcome(cts.IsCancellationRequested, 0);
            await UiDispatch.RunAsync(() =>
            {
                if (OwnsThumbJob(item.Id, cts))
                {
                    _thumbSkipped.Add(item.Id);
                    item.ThumbLoadStarted = false;
                }
            });
        }

        await UiDispatch.RunAsync(() =>
        {
            if (OwnsThumbJob(item.Id, cts))
            {
                _thumbJobs.Remove(item.Id);
                if (item.ThumbImage is null)
                {
                    _thumbQueue.TryAbandon(item.Id, ticket);
                    item.ThumbLoadStarted = false;
                    if (outcome.Cancelled)
                    {
                        ThumbTimingLog.Write(ThumbTimingLog.TileLine(
                            "cancel",
                            disk,
                            outcome.Milliseconds,
                            decodeWidth,
                            decodeHeight,
                            _thumbQueue.InFlight,
                            _thumbQueue.Cap,
                            startThread,
                            decodeThread,
                            Environment.CurrentManagedThreadId));
                    }
                }
            }

            ScheduleThumbPlan("job-done");
        });
    }

    private bool OwnsThumbJob(string id, CancellationTokenSource cts) =>
        _thumbJobs.TryGetValue(id, out var current) && ReferenceEquals(current, cts);

    private void CancelThumbJob(string id)
    {
        // Signal the decode and keep the queue slot until LoadThumbJobAsync returns.
        if (_thumbJobs.TryGetValue(id, out var cts))
        {
            cts.Cancel();
        }
    }

    private AssetItem? FindAssetById(string id)
    {
        foreach (var asset in ViewModel.Assets)
        {
            if (asset.Id == id)
            {
                return asset;
            }
        }

        return null;
    }

    private double RasterScale()
    {
        try
        {
            var scale = XamlRoot?.RasterizationScale ?? 1.0;
            return scale < 1 ? 1 : scale;
        }
        catch
        {
            return 1;
        }
    }

    private ThumbRect CurrentThumbViewport()
    {
        if (_mosaicScrollViewer is not null)
        {
            return new ThumbRect(
                0,
                _mosaicScrollViewer.VerticalOffset,
                Math.Max(1, _mosaicScrollViewer.ViewportWidth),
                Math.Max(1, _mosaicScrollViewer.ViewportHeight));
        }

        if (_mosaicScrollView is not null)
        {
            return new ThumbRect(
                0,
                _mosaicScrollView.VerticalOffset,
                Math.Max(1, _mosaicScrollView.ViewportWidth),
                Math.Max(1, _mosaicScrollView.ViewportHeight));
        }

        return new ThumbRect(0, 0, Math.Max(1, GrdAssets.ActualWidth), Math.Max(1, GrdAssets.ActualHeight));
    }

    private void TryHookMosaicScroll()
    {
        if (_scrollHooked)
        {
            return;
        }

        _mosaicScrollViewer = FindDescendant<ScrollViewer>(GrdAssets);
        if (_mosaicScrollViewer is not null)
        {
            _mosaicScrollViewer.ViewChanged += (_, _) => ScheduleThumbPlan("scroll");
            _scrollHooked = true;
            return;
        }

        _mosaicScrollView = FindDescendant<ScrollView>(GrdAssets);
        if (_mosaicScrollView is not null)
        {
            _mosaicScrollView.ViewChanged += (_, _) => ScheduleThumbPlan("scroll");
            _scrollHooked = true;
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found)
            {
                return found;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void LogLibraryShow(bool pageReused)
    {
        ThumbTimingLog.Write(ThumbTimingLog.LibraryShowLine(
            pageReused,
            previewKept: pageReused,
            _thumbQueue.InFlight,
            _thumbQueue.Cap,
            _thumbMemory.Count,
            Environment.CurrentManagedThreadId));
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

        if (e.PropertyName is nameof(GalleryViewModel.LoopVideo))
        {
            ApplyOverlayLoop();
        }
    }

    private async Task AssignPreviewAsync(
        int epoch,
        CancellationToken token,
        string? decodePath,
        int decodeWidth,
        int decodeHeight,
        double scale,
        string source,
        int longEdge)
    {
        CappedBgra? decoded = null;
        var decodeThread = Environment.CurrentManagedThreadId;
        var outcome = await ThumbDecodeRun.RunAsync(token, async ct =>
        {
            if (string.IsNullOrEmpty(decodePath) || AppServices.Thumbnails is null)
            {
                return;
            }

            decoded = await AppServices.Thumbnails
                .DecodeCappedBgraAsync(decodePath, decodeWidth, decodeHeight, ct)
                .ConfigureAwait(false);
            decodeThread = Environment.CurrentManagedThreadId;
        }).ConfigureAwait(false);

        if (outcome.Cancelled || epoch != Volatile.Read(ref _previewEpoch))
        {
            if (outcome.Cancelled)
            {
                ThumbTimingLog.Write(ThumbTimingLog.PreviewLine(
                    capped: true,
                    decodeWidth,
                    decodeHeight,
                    scale,
                    source,
                    longEdge,
                    outcome.Milliseconds,
                    decodeThread,
                    decodeThread));
            }

            return;
        }

        var frame = decoded;
        var milliseconds = frame?.Milliseconds ?? outcome.Milliseconds;
        await UiDispatch.RunAsync(() =>
        {
            if (epoch != _previewEpoch)
            {
                return;
            }

            ImgPreview.Source = frame is { } pixels
                ? BgraBitmap(pixels.Bgra, pixels.Width, pixels.Height)
                : null;
            ThumbTimingLog.Write(ThumbTimingLog.PreviewLine(
                capped: true,
                frame?.Width ?? decodeWidth,
                frame?.Height ?? decodeHeight,
                scale,
                source,
                longEdge,
                milliseconds,
                decodeThread,
                Environment.CurrentManagedThreadId));
        });
    }

    private async void UpdatePreview()
    {
        var epoch = Interlocked.Increment(ref _previewEpoch);
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var previewToken = _previewCts.Token;
        if (ViewModel.IsImagePreview && ViewModel.PreviewPath is not null)
        {
            var scale = RasterScale();
            var dipWidth = ImgPreview.ActualWidth > 1 ? ImgPreview.ActualWidth : 340;
            var dipHeight = ImgPreview.ActualHeight > 1 ? ImgPreview.ActualHeight : 220;
            var (decodeWidth, decodeHeight) = ThumbScheduler.DecodePixels(dipWidth, dipHeight, scale);
            var asset = ViewModel.SelectedAsset;
            var sameFile = asset is not null
                && string.Equals(asset.Path, ViewModel.PreviewPath, StringComparison.OrdinalIgnoreCase);
            var longEdge = sameFile ? ThumbScheduler.LongEdge(asset!.Width, asset.Height) : 0;
            var shown = ThumbScheduler.PreviewSource(
                ViewModel.PreviewPath,
                sameFile ? asset!.ThumbPath : null,
                longEdge,
                Math.Max(decodeWidth, decodeHeight));
            if (!string.IsNullOrEmpty(shown)
                && !string.Equals(shown, ViewModel.PreviewPath, StringComparison.OrdinalIgnoreCase)
                && (GalleryMedia.IsRemoteUri(shown) || !CloudFile.Exists(shown) || CloudFile.IsOnlineOnly(shown)))
            {
                shown = ViewModel.PreviewPath;
            }

            var source = string.Equals(shown, ViewModel.PreviewPath, StringComparison.OrdinalIgnoreCase)
                ? "original"
                : "disk-thumb";
            var decodePath = PathForPixelDecode(shown);
            _ = AssignPreviewAsync(epoch, previewToken, decodePath, decodeWidth, decodeHeight, scale, source, longEdge);
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
        SrfOverlayStill.IsReviewMode = ViewModel.IsReviewMode;
        SrfOverlayStill.Bind(ViewModel.IsGalleryOverlayOpen && gallery is { IsImage: true } ? gallery : null);

        if (gallery is { IsVideo: true, CurrentPath: not null } video
            && !AccessService.WouldHydrateOnOpen(video.CurrentPath))
        {
            var wasMaximized = ViewerMediaElement.IsPresenterMaximized(App.Window?.AppWindow);
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
                ViewerMediaElement.SuppressFullWindow(MpeOverlay);
                ViewerMediaElement.RestoreIfVideoMaximized(App.Window?.AppWindow, wasMaximized);
                SyncVideoBrowseSwipeHitTest();
            }
            catch
            {
                if (epoch == _overlayMediaEpoch)
                {
                    MpeOverlay.Source = null;
                    SyncVideoBrowseSwipeHitTest();
                }
            }
        }
        else
        {
            BtnGalleryPlay.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            MpeOverlay.Source = null;
            SyncVideoBrowseSwipeHitTest();
        }
    }

    private void EnsureOverlayPlayer()
    {
        if (MpeOverlay.MediaPlayer is null)
        {
            MpeOverlay.SetMediaPlayer(new Windows.Media.Playback.MediaPlayer());
        }

        ViewerMediaElement.SuppressFullWindow(MpeOverlay);
        ApplyOverlayLoop();
        ApplyOverlayVolume();
        VolOverlay.AttachTransport(MpeOverlay.TransportControls);
        VolOverlay.VolumeChanged -= OverlayVolume_Changed;
        VolOverlay.VolumeChanged += OverlayVolume_Changed;
        VolOverlay.SyncFromStore();
        var session = MpeOverlay.MediaPlayer?.PlaybackSession;
        if (session is not null)
        {
            session.PlaybackStateChanged -= OverlayPlayback_StateChanged;
            session.PlaybackStateChanged += OverlayPlayback_StateChanged;
        }

        SyncVideoBrowseSwipeHitTest();
    }

    private void ApplyOverlayLoop()
    {
        var player = MpeOverlay.MediaPlayer;
        if (player is null)
        {
            return;
        }

        player.IsLoopingEnabled = ViewModel.OverlayGallery?.LoopVideo ?? GalleryChrome.DefaultLoopVideo;
    }

    private void ApplyOverlayVolume() =>
        Controls.VolumeControl.ApplyToPlayer(MpeOverlay.MediaPlayer);

    private void OverlayVolume_Changed(object? sender, EventArgs e) => ApplyOverlayVolume();

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
        SyncVideoBrowseSwipeHitTest();
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

    private async void BtnGallerySetThumb_Click(object sender, RoutedEventArgs e)
    {
        var gallery = ViewModel.OverlayGallery;
        if (gallery is null || !gallery.CanSetVideoThumb)
        {
            return;
        }

        await ErrorReporter.RunAsync(
            "Set mosaic thumbnail",
            msg =>
            {
                ViewModel.InfoMessage = msg;
                ViewModel.ShowInfo = true;
                ViewModel.StatusText = msg;
            },
            async () =>
            {
                EnsureOverlayPlayer();
                var item = gallery.Current;
                var path = gallery.CurrentPath;
                var hash = item?.ContentHash;
                if (item is null || string.IsNullOrEmpty(path) || string.IsNullOrEmpty(hash))
                {
                    throw new InvalidOperationException(gallery.SetVideoThumbTooltip);
                }

                var info = await VideoFrameThumb.CaptureMosaicAsync(
                    MpeOverlay.MediaPlayer,
                    path,
                    hash,
                    AppServices.Thumbnails,
                    item.Width,
                    item.Height);
                if (info is null)
                {
                    throw new InvalidOperationException("Could not capture a frame from this video.");
                }

                await UiDispatch.RunAsync(() =>
                {
                    BitmapImage? image = null;
                    try
                    {
                        image = new BitmapImage
                        {
                            CreateOptions = BitmapCreateOptions.IgnoreImageCache,
                            UriSource = new Uri(info.Value.Path, UriKind.Absolute)
                        };
                    }
                    catch
                    {
                        image = null;
                    }

                    gallery.ApplyMosaicThumb(info.Value.Path, image);
                    ViewModel.InfoMessage = "Mosaic thumbnail updated.";
                    ViewModel.ShowInfo = true;
                    ViewModel.StatusText = ViewModel.InfoMessage;
                });
            });
    }

}
