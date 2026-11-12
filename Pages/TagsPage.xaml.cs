using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Palace.ViewModels;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace Palace.Pages;

public sealed partial class TagsPage : Page
{
    public TagsViewModel ViewModel => AppServices.Tags;

    public LibraryViewModel Library => AppServices.Library;

    private bool _ignoreColorChanges;
    private bool _colorDirty;
    private bool _priorityArmed;
    private static readonly ThumbSlotGate TileDecodeGate = new(4);
    private CancellationTokenSource _tileCts = new();
    private int _tileGeneration;
    private readonly Dictionary<Image, AssetItem> _realizedTiles = [];
    private readonly Dictionary<Image, long> _tileTagCallbacks = [];
    private AssetItem? _selectedMosaicAsset;
    private GalleryViewModel? _hookedOverlay;
    private int _overlayMediaEpoch;
    private RangeSelectSession? _rangeSelect;
    private long _rangePressMs;
    private Windows.Foundation.Point _rangePressPoint;
    private uint _rangePointerId;
    private Pointer? _rangePointer;
    private bool _rangeCaptured;
    private bool _rangeConsumedTap;
    private bool _lastPressWasTouch;
    private bool _suppressTouchContextFlyout;
    private ViewerOverlayAutoHide? _viewerOverlayAutoHide;
    private Frame? _shellContentFrame;
    private NavigationView? _shellNav;
    private bool _tagLayoutApplying;
    private bool _tagLayoutQueued;
    private double _lastTagContentWidth = double.NaN;
    private bool _lastTagShowDetails;

    public TagsPage()
    {
        InitializeComponent();
        ViewModel.RequestOrganizeChoice = AskOrganizeChoiceAsync;
        ViewModel.RequestConfirm = AskConfirmAsync;
        ViewModel.RequestOpenGallery = GalleryWindow.Show;
        ViewModel.RequestFocusRename = FocusRenameBox;
        ViewModel.RequestFocusAliasAdd = FocusAliasAddBox;
        ViewModel.RequestFocusImplies = FocusImpliesSection;
        ViewModel.MosaicReset = OnMosaicReset;
        ViewModel.MosaicChunkAppended = OnMosaicChunkAppended;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TagsViewModel.OverlayGallery) or nameof(TagsViewModel.IsGalleryOverlayOpen))
            {
                HookOverlayGallery();
                UpdateOverlayMedia();
                SyncViewerOverlayAutoHide();
                if (ViewModel.IsGalleryOverlayOpen)
                {
                    GrdGalleryOverlay.Focus(FocusState.Programmatic);
                }
            }

            if (e.PropertyName is nameof(TagsViewModel.ShowDetails))
            {
                RefreshTagColumnLayout();
            }
        };
        SizeChanged += (_, _) => RefreshTagColumnLayout();
        Loaded += async (_, _) =>
        {
            HookShellNavLayoutTracking();
            RefreshTagColumnLayout();
            GrdTagAssets.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(GrdTagAssets_KeyDown), true);
            GrdTagAssets.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(GrdTagAssets_RangeMoved), true);
            GrdTagAssets.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(GrdTagAssets_RangeReleased), true);
            GrdTagAssets.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(GrdTagAssets_RangeCaptureLost), true);
            GrdTagAssets.AddHandler(UIElement.HoldingEvent, new HoldingEventHandler(GrdTagAssets_RangeHolding), true);
            CardViewerDetails.AttachInFlow(GrdGalleryOverlay);
            ViewerMediaElement.SuppressFullWindow(MpeOverlay);
            SrfOverlayStill.ReviewSwipeCommitted += OnOverlayStillSwipe;
            _videoBrowseSwipe = new VideoBrowseSwipeBinder(
                MpeOverlay,
                VideoBrowseSwipeAllowedNow,
                OnOverlayStillSwipe);
            _videoBrowseSwipe.Hook();
            RefreshRealizedTiles();
            HookOverlayGallery();
            await ErrorReporter.RunAsync("Load tags", null, ViewModel.RefreshAsync);
            _priorityArmed = true;
        };
        Unloaded += (_, _) =>
        {
            UnhookShellNavLayoutTracking();
            _shellContentFrame = null;
            if (ReferenceEquals(ViewModel.MosaicReset, (Action)OnMosaicReset))
            {
                ViewModel.MosaicReset = null;
                ViewModel.MosaicChunkAppended = null;
            }

            if (ReferenceEquals(ViewModel.RequestFocusImplies, (Action<string?>)FocusImpliesSection))
            {
                ViewModel.RequestFocusImplies = null;
            }

            if (_hookedOverlay is not null)
            {
                _hookedOverlay.PropertyChanged -= OverlayGallery_PropertyChanged;
                _hookedOverlay = null;
            }
        };
    }

    private void GrdTagColumns_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RefreshTagColumnLayout();

    /// <summary>
    /// Drive sidebar / board / details from the shell Frame slot (excludes nav pane).
    /// Never from this grid's own ActualWidth — star-to-content under ∞ measure
    /// latched ScrTagDetails past the HWND @ ~1024 DIP.
    /// </summary>
    private void RefreshTagColumnLayout()
    {
        if (_tagLayoutApplying)
        {
            _tagLayoutQueued = true;
            return;
        }

        _tagLayoutApplying = true;
        try
        {
            do
            {
                _tagLayoutQueued = false;
                ApplyTagColumnLayout();
            }
            while (_tagLayoutQueued);
        }
        finally
        {
            _tagLayoutApplying = false;
        }
    }

    private void ApplyTagColumnLayout()
    {
        var content = ResolveTagContentWidthDip();
        var showDetails = ViewModel.ShowDetails;
        if (content < MosaicLayoutWidth.MinUsable)
        {
            return;
        }

        if (Math.Abs(content - _lastTagContentWidth) < 0.5
            && showDetails == _lastTagShowDetails
            && ColTagSidebar.Width.IsAbsolute)
        {
            return;
        }

        var (sidebar, board, details) = TagPageLayout.Allocate(content, showDetails);
        ColTagSidebar.MinWidth = 0;
        ColTagBoard.MinWidth = 0;
        ColTagDetails.MinWidth = 0;
        ColTagSidebar.Width = new GridLength(sidebar);
        ColTagBoard.Width = new GridLength(board);
        ColTagDetails.Width = new GridLength(details);
        _lastTagContentWidth = content;
        _lastTagShowDetails = showDetails;
    }

    private double ResolveTagContentWidthDip()
    {
        EnsureShellContentFrameReference();
        var slot = _shellContentFrame?.ActualWidth ?? 0;
        if (double.IsFinite(slot) && slot >= MosaicLayoutWidth.MinUsable)
        {
            return TagPageLayout.ContentWidthFromHostDip(slot);
        }

        var page = ActualWidth;
        if (double.IsFinite(page) && page >= MosaicLayoutWidth.MinUsable)
        {
            return TagPageLayout.ContentWidthFromHostDip(page);
        }

        return 0;
    }

    private void EnsureShellContentFrameReference()
    {
        if (_shellContentFrame is not null)
        {
            return;
        }

        DependencyObject? node = this;
        while (node is not null)
        {
            if (node is Frame frame)
            {
                _shellContentFrame = frame;
                return;
            }

            node = VisualTreeHelper.GetParent(node);
        }
    }

    private void HookShellNavLayoutTracking()
    {
        EnsureShellContentFrameReference();
        if (_shellContentFrame is not null)
        {
            _shellContentFrame.SizeChanged -= ShellContentFrame_SizeChanged;
            _shellContentFrame.SizeChanged += ShellContentFrame_SizeChanged;
        }

        if (_shellNav is not null)
        {
            return;
        }

        DependencyObject? node = this;
        while (node is not null)
        {
            if (node is NavigationView nav)
            {
                _shellNav = nav;
                nav.PaneOpened += ShellNav_PaneLayoutChanged;
                nav.PaneClosed += ShellNav_PaneLayoutChanged;
                nav.DisplayModeChanged += ShellNav_DisplayModeChanged;
                return;
            }

            node = VisualTreeHelper.GetParent(node);
        }
    }

    private void UnhookShellNavLayoutTracking()
    {
        if (_shellContentFrame is not null)
        {
            _shellContentFrame.SizeChanged -= ShellContentFrame_SizeChanged;
        }

        if (_shellNav is null)
        {
            return;
        }

        _shellNav.PaneOpened -= ShellNav_PaneLayoutChanged;
        _shellNav.PaneClosed -= ShellNav_PaneLayoutChanged;
        _shellNav.DisplayModeChanged -= ShellNav_DisplayModeChanged;
        _shellNav = null;
    }

    private void ShellContentFrame_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RefreshTagColumnLayout();

    private void ShellNav_PaneLayoutChanged(NavigationView sender, object args) =>
        RefreshTagColumnLayout();

    private void ShellNav_DisplayModeChanged(
        NavigationView sender,
        NavigationViewDisplayModeChangedEventArgs args) =>
        RefreshTagColumnLayout();

    public static IRelayCommand<AssetItem?> GetOpenAssetCommand() => AppServices.Tags.OpenAssetCommand;

    public static IRelayCommand<TagChipItem?> GetStarChipCommand() => AppServices.Tags.StarChipCommand;

    public static IRelayCommand<TagChipItem?> GetFilterChipCommand() => AppServices.Tags.FilterChipCommand;

    public static IRelayCommand<TagChipItem?> GetRenameChipCommand() => AppServices.Tags.RenameChipCommand;

    public static IRelayCommand<TagChipItem?> GetRemoveChipFromGroupCommand() => AppServices.Tags.RemoveChipFromGroupCommand;

    public static IRelayCommand<TagChipItem?> GetDeleteChipCommand() => AppServices.Tags.DeleteChipCommand;

    public static IRelayCommand<string?> GetApplyPresetColorCommand() => AppServices.Tags.ApplyPresetColorCommand;

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

    public static IRelayCommand<TagAliasRow?> GetRemoveAliasCommand() => AppServices.Tags.RemoveAliasRowCommand;

    public static IRelayCommand GetCreateGroupCommand() => AppServices.Tags.CreateGroupCommand;

    private void TagChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TagChipItem chip })
        {
            return;
        }

        var ctrl = IsKeyDown(VirtualKey.Control) || IsKeyDown(VirtualKey.LeftControl) || IsKeyDown(VirtualKey.RightControl);
        var shift = IsKeyDown(VirtualKey.Shift) || IsKeyDown(VirtualKey.LeftShift) || IsKeyDown(VirtualKey.RightShift);
        ViewModel.SelectChip(chip, toggle: ctrl, extend: shift && !ctrl);
    }

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void TagChip_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // Mouse-only CanDrag so touch/pen still pans the chip ScrollViewer (AssetDrag pattern).
        if (sender is UIElement el)
        {
            el.CanDrag = e.Pointer.PointerDeviceType == PointerDeviceType.Mouse;
        }
    }

    private void TagChip_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement el)
        {
            el.CanDrag = false;
        }
    }

    private async void TagChip_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: TagChipItem chip } || string.IsNullOrEmpty(chip.TagId))
        {
            args.Cancel = true;
            return;
        }

        if (!chip.IsSelected)
        {
            ViewModel.SelectChip(chip);
        }

        var ids = ViewModel.ChipPayloadIds(chip);
        if (ids.Count == 0)
        {
            args.Cancel = true;
            return;
        }

        // Custom ghost — never SetContentFromDataPackage (blank file icon).
        var deferral = args.GetDeferral();
        try
        {
            ViewModel.SetChipDragSources(ids);
            args.Data.Properties[TagMembershipAdd.DataKey] = TagMembershipAdd.Serialize(ids);
            args.AllowedOperations = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            var badge = TagMembershipAdd.MultiBadge(ids.Count);
            if (!string.IsNullOrEmpty(badge))
            {
                args.Data.Properties.Title = badge;
            }

            var bitmap = await TryBuildTagDragGhostAsync(chip, ids.Count);
            if (bitmap is not null)
            {
                args.DragUI.SetContentFromBitmapImage(bitmap);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void TagChip_DropCompleted(UIElement sender, DropCompletedEventArgs e) =>
        ViewModel.ClearChipDragSources();

    /// <summary>dg-01a/b: pill ghost + optional "N tags" badge via RenderTargetBitmap.</summary>
    private async Task<BitmapImage?> TryBuildTagDragGhostAsync(TagChipItem chip, int count)
    {
        try
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (TagColor.TryParse(chip.EffectiveColor, out var color))
            {
                row.Children.Add(new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(color),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            row.Children.Add(new TextBlock
            {
                Text = chip.Name ?? "",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeBrush("TextFillColorPrimaryBrush", Microsoft.UI.Colors.White)
            });

            var chipBorder = new Border
            {
                Background = ThemeBrush("ControlFillColorDefaultBrush", Color.FromArgb(0xE0, 0x2C, 0x2C, 0x2C)),
                BorderBrush = ThemeBrush("ControlStrokeColorDefaultBrush", Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(10, 4, 10, 4),
                Child = row,
                Opacity = 0.92
            };

            UIElement visual = chipBorder;
            var badgeText = TagMembershipAdd.MultiBadge(count);
            if (!string.IsNullOrEmpty(badgeText))
            {
                var grid = new Grid();
                grid.Children.Add(chipBorder);
                grid.Children.Add(new Border
                {
                    Background = ThemeBrush("AccentFillColorDefaultBrush", Color.FromArgb(0xFF, 0xED, 0x9F, 0xBC)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 2, 6, 2),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, -8, -10, 0),
                    Child = new TextBlock
                    {
                        Text = badgeText,
                        FontSize = 10,
                        Foreground = ThemeBrush("TextOnAccentFillColorPrimaryBrush", Microsoft.UI.Colors.Black)
                    }
                });
                visual = grid;
            }

            visual.XamlRoot = XamlRoot;
            visual.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = visual.DesiredSize;
            if (size.Width < 1 || size.Height < 1)
            {
                return null;
            }

            visual.Arrange(new Windows.Foundation.Rect(0, 0, size.Width, size.Height));
            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(visual);
            var pixels = await rtb.GetPixelsAsync();
            if (rtb.PixelWidth == 0 || rtb.PixelHeight == 0 || pixels.Length == 0)
            {
                return null;
            }

            var pixelBytes = new byte[pixels.Length];
            using (var reader = DataReader.FromBuffer(pixels))
            {
                reader.ReadBytes(pixelBytes);
            }

            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            var dpi = (XamlRoot?.RasterizationScale ?? 1.0) * 96.0;
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)rtb.PixelWidth,
                (uint)rtb.PixelHeight,
                dpi,
                dpi,
                pixelBytes);
            await encoder.FlushAsync();
            stream.Seek(0);

            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            return image;
        }
        catch (Exception ex)
        {
            ErrorReporter.LogOnly("Tag drag ghost", ex);
            return null;
        }
    }

    private static Brush ThemeBrush(string key, Color fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true
            && value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }

    /// <summary>dg-05 solid colour dot — PathIcon EllipseGeometry (not font glyphs).</summary>
    private static PathIcon TagGroupColorDot(Color color) =>
        new()
        {
            Width = 10,
            Height = 10,
            Foreground = new SolidColorBrush(color),
            Data = new EllipseGeometry
            {
                Center = new Windows.Foundation.Point(5, 5),
                RadiusX = 4,
                RadiusY = 4
            }
        };

    private void TagGroup_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TagBoardGroup group }
            || !group.IsUserGroupRow
            || string.IsNullOrEmpty(group.GroupId)
            || !e.DataView.Properties.TryGetValue(TagMembershipAdd.DataKey, out var raw))
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
            ViewModel.ClearGroupDropChrome();
            return;
        }

        var ids = TagMembershipAdd.Parse(raw);
        var (added, already, cycle) = ViewModel.PreviewAddToGroup(group.GroupId!, ids);
        var allow = TagMembershipAdd.AllowsDrop(added);
        e.AcceptedOperation = allow
            ? Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy
            : Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
        // Caption + ± glyph; ghost content already set — never show blank file icon.
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
        e.DragUIOverride.IsGlyphVisible = true;
        e.DragUIOverride.Caption = TagMembershipAdd.DragHint(group.Name, added, already, cycle);
        ViewModel.SetGroupDropChrome(group.GroupId, allow);
        e.Handled = true;
    }

    private void TagGroup_DragLeave(object sender, DragEventArgs e) =>
        ViewModel.ClearGroupDropChrome();

    private async void TagGroup_Drop(object sender, DragEventArgs e)
    {
        ViewModel.ClearGroupDropChrome();
        ViewModel.ClearChipDragSources();
        if (sender is not FrameworkElement { Tag: TagBoardGroup group }
            || !group.IsUserGroupRow
            || string.IsNullOrEmpty(group.GroupId)
            || !e.DataView.Properties.TryGetValue(TagMembershipAdd.DataKey, out var raw))
        {
            return;
        }

        var ids = TagMembershipAdd.Parse(raw);
        var (added, _, _) = ViewModel.PreviewAddToGroup(group.GroupId!, ids);
        if (!TagMembershipAdd.AllowsDrop(added))
        {
            return;
        }

        await ErrorReporter.RunAsync(
            "Add tags to group",
            null,
            () => ViewModel.AddTagsToGroupCoreAsync(ids, group.GroupId!));
    }

    private void FocusRenameBox()
    {
        // Chip context menus steal focus on close; wait a tick so Rename lands in the name box.
        _ = FocusRenameBoxAfterMenuAsync();
    }

    private async Task FocusRenameBoxAfterMenuAsync()
    {
        await Task.Delay(80);
        await UiDispatch.RunAsync(() =>
        {
            if (!ViewModel.ShowDetails || !IsLoaded)
            {
                return;
            }

            TxtRenameTag.Focus(FocusState.Programmatic);
            TxtRenameTag.SelectAll();
        });
    }

    /// <summary>it-01 Edit implication rule… — scroll details to Implicit tags and select the edge.</summary>
    private void FocusImpliesSection(string? impliedTagId)
    {
        _ = FocusImpliesSectionAsync(impliedTagId);
    }

    private async Task FocusImpliesSectionAsync(string? impliedTagId)
    {
        // Wait for nav + details paint after Library → Tags.
        await Task.Delay(120);
        await UiDispatch.RunAsync(() =>
        {
            if (!ViewModel.ShowDetails || !IsLoaded)
            {
                return;
            }

            LstImpliedTags.StartBringIntoView();
            if (string.IsNullOrWhiteSpace(impliedTagId))
            {
                AsbAddImplied.Focus(FocusState.Programmatic);
                return;
            }

            var pick = ViewModel.ImpliedTags
                .FirstOrDefault(t => string.Equals(t.TagId, impliedTagId, StringComparison.Ordinal));
            if (pick is not null)
            {
                LstImpliedTags.SelectedItem = pick;
                LstImpliedTags.ScrollIntoView(pick);
                LstImpliedTags.Focus(FocusState.Programmatic);
            }
            else
            {
                AsbAddImplied.Focus(FocusState.Programmatic);
            }
        });
    }

    private void TxtRenameTag_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            if (ViewModel.SaveRenameCommand.CanExecute(null))
            {
                ViewModel.SaveRenameCommand.Execute(null);
            }

            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            ViewModel.RevertRename();
            e.Handled = true;
        }
    }

    private void TagsPage_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Z
            || !InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down)
            || InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down)
            || InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down)
            || ViewModel.IsGalleryOverlayOpen
            || FocusIsTextInput(e.OriginalSource))
        {
            return;
        }

        if (Library.UndoFileOpCommand.CanExecute(null) && Library.CanUndoFileOp)
        {
            Library.UndoFileOpCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void TxtNewTagName_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter
            || InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down))
        {
            return;
        }

        if (ViewModel.CreateNewTagsCommand.CanExecute(null))
        {
            ViewModel.CreateNewTagsCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void TxtTagDescription_LostFocus(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SaveDescriptionCommand.CanExecute(null))
        {
            ViewModel.SaveDescriptionCommand.Execute(null);
        }
    }

    private void InfTagsUndo_PointerEntered(object sender, PointerRoutedEventArgs e) =>
        Library.PauseUndoToastTimer();

    private void InfTagsUndo_PointerExited(object sender, PointerRoutedEventArgs e) =>
        Library.ResumeUndoToastTimer();

    private void InfTagsUndo_FocusEntered(object sender, RoutedEventArgs e) =>
        Library.PauseUndoToastTimer();

    private void InfTagsUndo_FocusExited(object sender, RoutedEventArgs e) =>
        Library.ResumeUndoToastTimer();

    private void NbTagPriority_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_priorityArmed || !IsLoaded || ViewModel.SelectedNode?.TagId is null)
        {
            return;
        }

        ViewModel.CommitPriorityFromBox(args.NewValue);
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
        if (add)
        {
            FillAddToGroupMenu(sub, chip);
            return;
        }

        var groups = ViewModel.GroupDestinations(chip, add: false);
        sub.IsEnabled = groups.Count > 0;
        foreach (var group in groups)
        {
            var item = new MenuFlyoutItem
            {
                Text = group.Name,
                Command = ViewModel.MoveChipToGroupCommand,
                CommandParameter = new TagChipGroupMove
                {
                    Chip = chip,
                    GroupId = group.TagId,
                    GroupName = group.Name ?? ""
                }
            };
            AutomationProperties.SetAutomationId(
                item,
                "MnuMoveToGroup_" + string.Concat((group.Name ?? "").Where(char.IsLetterOrDigit)));
            AutomationProperties.SetName(item, group.Name);
            sub.Items.Add(item);
        }
    }

    /// <summary>dg-05: colour dots, checked+disabled existing, New group… one undoable step.</summary>
    private void FillAddToGroupMenu(MenuFlyoutSubItem sub, TagChipItem chip)
    {
        var picks = ViewModel.AddGroupMenuPicks(chip);
        sub.IsEnabled = true;
        foreach (var pick in picks)
        {
            // Icon lives on MenuFlyoutItem (not MenuFlyoutItemBase).
            MenuFlyoutItem item = pick.IsMember
                ? new ToggleMenuFlyoutItem
                {
                    Text = pick.Name,
                    IsChecked = true,
                    IsEnabled = false
                }
                : new MenuFlyoutItem
                {
                    Text = pick.Name,
                    IsEnabled = pick.IsEnabled,
                    Command = ViewModel.AddChipToGroupCommand,
                    CommandParameter = new TagChipGroupMove
                    {
                        Chip = chip,
                        GroupId = pick.TagId,
                        GroupName = pick.Name ?? ""
                    }
                };

            if (TagColor.TryParse(pick.Color, out var color))
            {
                // EllipseGeometry PathIcon — Segoe Fluent Icons U+25CF draws a hollow box.
                item.Icon = TagGroupColorDot(color);
            }

            AutomationProperties.SetAutomationId(
                item,
                "MnuAddToGroup_" + string.Concat((pick.Name ?? "").Where(char.IsLetterOrDigit)));
            AutomationProperties.SetName(item, pick.Name);
            sub.Items.Add(item);
        }

        if (sub.Items.Count > 0)
        {
            sub.Items.Add(new MenuFlyoutSeparator());
        }

        var create = new MenuFlyoutItem
        {
            Text = "New group…",
            Command = ViewModel.CreateGroupAndAddChipsCommand,
            CommandParameter = chip
        };
        create.Icon = new FontIcon { Glyph = "\uE710", FontSize = 16 };
        AutomationProperties.SetAutomationId(create, "MnuAddToGroup_New");
        AutomationProperties.SetName(create, "New group");
        sub.Items.Add(create);
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

        _tileGeneration++;
        _tileCts.Cancel();
        _tileCts = new CancellationTokenSource();
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
        // Defer off Measure/ProcessBindings — same stowed-exception trap as Library
        // mosaic (StorageFile.GetFileFromPathAsync on UI STA during layout).
        var pending = item;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _ = LoadTileThumbAsync(pending);
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
        var token = _tileCts.Token;
        var generation = _tileGeneration;
        ThumbSlot? slot = null;
        try
        {
            slot = await TileDecodeGate.EnterAsync(token);
            if (item.ThumbImage is not null || generation != _tileGeneration)
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
                var generated = await AppServices.Thumbnails.EnsureThumbnailAsync(
                    item.Path,
                    item.ContentHash,
                    item.Kind,
                    cancellationToken: token);
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

            var scale = 1.0;
            try
            {
                scale = XamlRoot?.RasterizationScale ?? 1;
            }
            catch
            {
                // XamlRoot is unavailable during early bind.
            }

            var (boxWidth, boxHeight) = ThumbScheduler.DecodePixels(148, 148, scale);
            var image = await LibraryPage.DecodeCappedSourceAsync(path, boxWidth, boxHeight, token);
            if (image is null)
            {
                return;
            }

            await UiDispatch.RunAsync(() =>
            {
                if (item.ThumbImage is not null
                    || generation != _tileGeneration
                    || slot is not { IsCurrent: true })
                {
                    return;
                }

                item.ThumbImage = image;
            });
        }
        catch (OperationCanceledException)
        {
            // Mosaic reset cancelled this tile. The slot was already released.
        }
        catch
        {
            item.ThumbLoadStarted = false;
        }
        finally
        {
            slot?.Dispose();
        }
    }

    private void TagAsset_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var item = FindAssetItem(sender) ?? FindAssetItem(e.OriginalSource);
        if (item is null)
        {
            return;
        }

        if (_rangeConsumedTap)
        {
            _rangeConsumedTap = false;
            e.Handled = true;
            return;
        }

        if (ViewModel.IsSelectMode)
        {
            ViewModel.ToggleMosaicAsset(item);
            _selectedMosaicAsset = item;
            e.Handled = true;
            return;
        }

        EnsureSelectedForContext(item);
    }

    private void TagAsset_GotFocus(object sender, RoutedEventArgs e)
    {
        var item = FindAssetItem(sender);
        if (item is null)
        {
            return;
        }

        if (ViewModel.IsSelectMode)
        {
            _selectedMosaicAsset = item;
            return;
        }

        StampMosaicSelection(item);
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
        _suppressTouchContextFlyout = false;
        _lastPressWasTouch = false;
        var item = FindAssetItem(sender) ?? FindAssetItem(e.OriginalSource);
        if (item is not null)
        {
            EnsureSelectedForContext(item);
        }
    }

    private void TagAssetMenu_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout)
        {
            return;
        }

        if (_suppressTouchContextFlyout
            || TouchGestures.SuppressesContextForTouch(_lastPressWasTouch, hasPointerPosition: true))
        {
            _suppressTouchContextFlyout = false;
            flyout.Hide();
            return;
        }

        if (flyout.Target is FrameworkElement target)
        {
            var item = FindAssetItem(target);
            if (item is not null)
            {
                EnsureSelectedForContext(item);
            }
        }
    }

    private void TagAssetMore_Click(object sender, RoutedEventArgs e)
    {
        _suppressTouchContextFlyout = false;
        _lastPressWasTouch = false;
        if (sender is not FrameworkElement fe)
        {
            return;
        }

        var item = FindAssetItem(fe);
        if (item is null)
        {
            return;
        }

        EnsureSelectedForContext(item);
        for (var cur = (DependencyObject)fe; cur is not null; cur = VisualTreeHelper.GetParent(cur))
        {
            if (cur is FrameworkElement { ContextFlyout: { } flyout } host)
            {
                flyout.ShowAt(fe);
                return;
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
        if (ViewModel.IsSelectMode && item.IsSelected)
        {
            _selectedMosaicAsset = item;
            FocusMosaicTile(item);
            return;
        }

        StampMosaicSelection(item);
        FocusMosaicTile(item);
    }

    private void StampMosaicSelection(AssetItem item)
    {
        ViewModel.SetMosaicSelection([item]);
        _selectedMosaicAsset = ViewModel.Assets.FirstOrDefault(asset => asset.Id == item.Id) ?? item;
    }

    private static RangeSelectPointer ToRangePointer(PointerDeviceType type) => type switch
    {
        PointerDeviceType.Touch => RangeSelectPointer.Touch,
        PointerDeviceType.Pen => RangeSelectPointer.Pen,
        PointerDeviceType.Mouse => RangeSelectPointer.Mouse,
        _ => RangeSelectPointer.Other
    };

    private void TagAsset_RangePressed(object sender, PointerRoutedEventArgs e)
    {
        _rangeConsumedTap = false;
        _lastPressWasTouch = e.Pointer.PointerDeviceType == PointerDeviceType.Touch;
        if (!ViewModel.IsSelectMode)
        {
            return;
        }

        var item = FindAssetItem(sender) ?? FindAssetItem(e.OriginalSource);
        if (item is null
            || !RangeSelect.AllowsGesture(true, overlayOpen: false, item.IsFolderHeader))
        {
            return;
        }

        var index = ViewModel.Assets.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        var point = e.GetCurrentPoint(GrdTagAssets);
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)
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

    private void GrdTagAssets_RangeMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_rangeSelect is null || e.Pointer.PointerId != _rangePointerId)
        {
            return;
        }

        var point = e.GetCurrentPoint(GrdTagAssets);
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

        ApplyRangeHover(e);
        e.Handled = true;
    }

    private void GrdTagAssets_RangeHolding(object sender, HoldingRoutedEventArgs e)
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

            ViewModel.IsSelectMode = true;
            ViewModel.SetMosaicSelection([holdItem]);
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
        ViewModel.ApplyMosaicRange(_rangeSelect.AnchorIndex, _rangeSelect.AnchorIndex);
        StampRangeAnchor();
        e.Handled = true;
    }

    private void GrdTagAssets_RangeReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_rangeSelect is null || e.Pointer.PointerId != _rangePointerId)
        {
            return;
        }

        if (_rangeSelect.IsActive)
        {
            ApplyRangeHover(e);
            _rangeConsumedTap = true;
            e.Handled = true;
        }

        EndRangeSelect(e.Pointer);
    }

    private void GrdTagAssets_RangeCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_rangeSelect is not null && e.Pointer.PointerId == _rangePointerId)
        {
            EndRangeSelect(null);
        }
    }

    private void ApplyRangeHover(PointerRoutedEventArgs e)
    {
        if (_rangeSelect is null)
        {
            return;
        }

        var hover = HitMosaicItem(e);
        var hoverIndex = hover is null ? _rangeSelect.EndIndex : ViewModel.Assets.IndexOf(hover);
        var flags = ViewModel.Assets.Select(asset => asset.IsFolderHeader).ToList();
        _rangeSelect.Highlight(flags, hoverIndex);
        ViewModel.ApplyMosaicRange(_rangeSelect.AnchorIndex, _rangeSelect.EndIndex);
        StampRangeAnchor();
    }

    private void StampRangeAnchor()
    {
        if (_rangeSelect is null)
        {
            return;
        }

        var end = (uint)_rangeSelect.EndIndex < (uint)ViewModel.Assets.Count
            ? ViewModel.Assets[_rangeSelect.EndIndex]
            : null;
        if (end is { IsFolderHeader: false })
        {
            _selectedMosaicAsset = end;
        }
    }

    private AssetItem? HitMosaicItem(PointerRoutedEventArgs e)
    {
        var local = e.GetCurrentPoint(GrdTagAssets).Position;
        var origin = GrdTagAssets.TransformToVisual(null).TransformPoint(default);
        var (x, y) = RangeSelect.ToWindowPoint(local.X, local.Y, origin.X, origin.Y);
        var windowPoint = new Windows.Foundation.Point(x, y);
        foreach (var hit in VisualTreeHelper.FindElementsInHostCoordinates(windowPoint, GrdTagAssets))
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

        _rangeCaptured = GrdTagAssets.CapturePointer(pointer);
        _rangePointer = pointer;
    }

    private void EndRangeSelect(Pointer? pointer)
    {
        if (pointer is not null || _rangePointer is not null)
        {
            try
            {
                GrdTagAssets.ReleasePointerCapture(pointer ?? _rangePointer!);
            }
            catch (ArgumentException)
            {
                // Not captured.
            }
        }

        _rangeSelect = null;
        _rangePointerId = 0;
        _rangePointer = null;
        _rangeCaptured = false;
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

    private void FocusAliasAddBox()
    {
        AsbAddAlias.Focus(FocusState.Programmatic);
    }

    private void LstAliases_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TagAliasRow row)
        {
            ViewModel.SelectAliasRowCommand.Execute(row);
        }
    }

    private void LstAliases_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstAliases.SelectedItem is TagAliasRow row)
        {
            ViewModel.SelectAliasRowCommand.Execute(row);
        }
    }

    private void AsbAddAlias_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            ViewModel.AddAliasNamesCommand.Execute(null);
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

        if (e.PropertyName is nameof(GalleryViewModel.LoopVideo))
        {
            ApplyOverlayLoop();
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
            var wasMaximized = ViewerMediaElement.IsPresenterMaximized(App.Window?.AppWindow);
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
            BtnGalleryPlay.Visibility = Visibility.Collapsed;
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

    private VideoBrowseSwipeBinder? _videoBrowseSwipe;

    private void OnOverlayStillSwipe(TouchGestures.SwipeAction action)
    {
        // Tags overlay has no Review mode — browse prev/next only.
        switch (action)
        {
            case TouchGestures.SwipeAction.BrowsePrevious:
                ViewModel.OverlayGallery?.GoPreviousCommand.Execute(null);
                break;
            case TouchGestures.SwipeAction.BrowseNext:
                ViewModel.OverlayGallery?.GoNextCommand.Execute(null);
                break;
        }
    }

    private bool VideoBrowseSwipeAllowedNow()
    {
        // Playing OK — binder routes scrub-bar vs page (docs/specs/swipe-nav.md).
        return ViewModel.OverlayGallery?.IsVideo == true
            && ViewerSettings.TouchGesturesEnabled;
    }

    private void SyncVideoBrowseSwipeHitTest() => _videoBrowseSwipe?.SyncAllowed();

    private void OverlayPlayback_StateChanged(
        Windows.Media.Playback.MediaPlaybackSession sender,
        object args) =>
        DispatcherQueue.TryEnqueue(SyncVideoBrowseSwipeHitTest);

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
        SyncVideoBrowseSwipeHitTest();
    }

    private void ViewerToolbar_Holding(object sender, HoldingRoutedEventArgs e)
    {
        // Gaps between toolbar buttons must not open the asset context menu.
        e.Handled = true;
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
            if (current is TextBox or RichEditBox or PasswordBox or AutoSuggestBox or NumberBox)
            {
                return true;
            }

            if (current is ComboBox { IsEditable: true })
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

    private async void BtnGallerySetThumb_Click(object sender, RoutedEventArgs e)
    {
        var gallery = ViewModel.OverlayGallery;
        if (gallery is null || !gallery.CanSetVideoThumb)
        {
            return;
        }

        await ErrorReporter.RunAsync(
            "Set mosaic thumbnail",
            msg => ViewModel.StatusText = msg,
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

                var (boxWidth, boxHeight) = ThumbScheduler.DecodePixels(148, 148, 1);
                var image = await LibraryPage.DecodeCappedSourceAsync(info.Value.Path, boxWidth, boxHeight, CancellationToken.None);
                await UiDispatch.RunAsync(() =>
                {
                    gallery.ApplyMosaicThumb(info.Value.Path, image);
                    ViewModel.StatusText = "Mosaic thumbnail updated.";
                });
            });
    }

}
