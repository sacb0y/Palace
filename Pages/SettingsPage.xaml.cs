using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Palace.Services.Cloud;
using Palace.ViewModels;
using Windows.Storage;
using Windows.System;
using Windows.UI;

namespace Palace.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _ignoreTintChanges;
    private bool _ignoreNavSelection;
    private bool _ignorePresetSelection;
    private bool _ignoreSearchClear;
    private bool _searchFiltering;
    /// <summary>Owned Borders around each SettingsNav card — highlight lives here, not in toolkit templates.</summary>
    private readonly Dictionary<string, Border> _searchHosts = new(StringComparer.Ordinal);
    private readonly List<Border> _highlightedHosts = [];
    private DispatcherTimer? _highlightTimer;
    private const string SearchHostTag = "PalaceSettingsSearchHost";

    public SettingsViewModel ViewModel => AppServices.Settings;

    /// <summary>DataTemplate-safe accessors (same pattern as LibraryPage).</summary>
    public static IRelayCommand<SourceFolderItem?> GetSelectSourceCommand() =>
        AppServices.Settings.SelectSourceCommand;

    public static IRelayCommand<SourceFolderItem?> GetOpenSourceCommand() =>
        AppServices.Settings.OpenSourceCommand;

    public static IRelayCommand<SourceFolderItem?> GetRemoveSourceCommand() =>
        AppServices.Settings.RemoveSourceCommand;

    public SettingsPage()
    {
        InitializeComponent();
        // System theme (or late ActualTheme) must flip packaged light/dark preset thumbs.
        ActualThemeChanged += (_, _) =>
        {
            ViewModel.RefreshBackgroundPresetSwatches();
            RefreshShellPreview();
        };
        SizeChanged += SettingsPage_SizeChanged;
        Loaded += async (_, _) =>
        {
            ViewModel.RequestPickCloudFolder = PickCloudFolderAsync;
            ViewModel.RequestConfirm = AskConfirmAsync;
            await ErrorReporter.RunAsync("Load settings", null, ViewModel.LoadAsync);
            SyncTintPicker();
            ViewModel.RefreshBackgroundPresetSwatches();
            SyncPresetGridSelection();
            ApplyContentCap(ActualWidth);
            EnsureSearchHighlightHosts();
            ApplyDensity(ViewModel.SelectedDensity);
            RestoreLastSection(scrollToTop: true);
            ShellBackground.Changed += ShellBackground_PreviewChanged;
            ShellBackground.LiveAmountsChanged += ShellBackground_PreviewLiveAmounts;
            RefreshShellPreview();
        };
        Unloaded += (_, _) =>
        {
            ViewModel.FlushShellPersist();
            ShellBackground.Changed -= ShellBackground_PreviewChanged;
            ShellBackground.LiveAmountsChanged -= ShellBackground_PreviewLiveAmounts;
            ShellBackdrop.DetachPreview();
            ViewModel.RequestPickCloudFolder = null;
            ViewModel.RequestConfirm = null;
            ViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
            ClearSearchHighlight();
            _highlightTimer?.Stop();
        };
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    private void ShellBackground_PreviewChanged(object? sender, EventArgs e) => RefreshShellPreview();

    private void ShellBackground_PreviewLiveAmounts(object? sender, EventArgs e)
    {
        try
        {
            ShellBackdrop.SyncLiveAmounts();
        }
        catch
        {
            RefreshShellPreview();
        }
    }

    private void RefreshShellPreview()
    {
        try
        {
            ShellBackdrop.AttachPreview(
                BrdShellPreviewGradient,
                ImgShellPreviewWallpaper,
                BrdShellPreviewGlass,
                BrdShellPreviewDim);
        }
        catch
        {
            // Preview must never take down Settings.
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Frame may reuse the page; restore content pane (not only list highlight).
        if (IsLoaded)
        {
            RestoreLastSection(scrollToTop: false);
        }
    }

    private void SettingsPage_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyContentCap(e.NewSize.Width);

    /// <summary>
    /// Content width follows the pane (not the widest child) so collapsed expanders
    /// cannot shrink sibling SettingsCards. Cap at 1000px and left-align when the
    /// Settings page is wider than 1100px.
    /// </summary>
    private void ApplyContentCap(double pageWidth)
    {
        var capWindow = (double)Application.Current.Resources["PalaceSettingsContentCapWindow"];
        var cap = (double)Application.Current.Resources["PalaceSettingsContentCap"];
        // Left nav 180 + content padding 40 (20 each side).
        var available = Math.Max(240, pageWidth - 180 - 40);
        var width = pageWidth > capWindow ? Math.Min(cap, available) : available;
        PnlSettingsContent.Width = width;
        PnlSettingsContent.MaxWidth = width;
        PnlSettingsContent.HorizontalAlignment = HorizontalAlignment.Left;
        ScrSettingsContent.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    private void RestoreLastSection(bool scrollToTop)
    {
        var stored = ApplicationData.Current.LocalSettings.Values[SettingsNav.LastSectionKey];
        SelectSection(SettingsNav.ParseStored(stored), persist: false, scrollToTop);
    }

    /// <summary>
    /// Keyboard, pointer, and Narrator/UIA selection all change SelectedItem —
    /// drive content from SelectionChanged (not ItemClick).
    /// </summary>
    private void LstSettingsNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ignoreNavSelection || LstSettingsNav.SelectedItem is not ListViewItem item)
        {
            return;
        }

        // Manual nav clears an active search filter so one section shows fully.
        if (!string.IsNullOrWhiteSpace(TxtSettingsSearch.Text))
        {
            _ignoreSearchClear = true;
            TxtSettingsSearch.Text = "";
            _ignoreSearchClear = false;
            ClearSearchFilter();
        }

        var id = item.Tag as string ?? SettingsNav.DefaultSectionId;
        SelectSection(id, persist: true, scrollToTop: true);
    }

    private void TxtSettingsSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ignoreSearchClear)
        {
            return;
        }

        ApplySearchQuery(TxtSettingsSearch.Text);
    }

    private void TxtSettingsSearch_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        if (!string.IsNullOrEmpty(TxtSettingsSearch.Text))
        {
            TxtSettingsSearch.Text = "";
            e.Handled = true;
        }

        ScrSettingsContent.Focus(FocusState.Programmatic);
    }

    private void FocusSettingsSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        TxtSettingsSearch.Focus(FocusState.Programmatic);
        TxtSettingsSearch.SelectAll();
        args.Handled = true;
    }

    private void ApplySearchQuery(string? query)
    {
        var hits = SettingsNav.FindHits(query);
        if (hits.Count == 0)
        {
            var hadFilter = _searchFiltering;
            ClearSearchFilter();
            if (hadFilter)
            {
                RestoreLastSection(scrollToTop: true);
            }
            else
            {
                ClearSearchHighlight();
            }

            return;
        }

        _searchFiltering = true;
        var sectionIds = SettingsNav.VisibleSectionIds(hits);
        var cardIds = SettingsNav.VisibleCardIds(hits);

        foreach (var panel in SectionPanels())
        {
            var id = panel.Name["PnlSection_".Length..];
            panel.Visibility = sectionIds.Contains(id) ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var target in SettingsNav.Targets)
        {
            if (FindName(target.CardAutomationId) is not UIElement card)
            {
                continue;
            }

            var show = cardIds.Contains(target.CardAutomationId);
            // Collapse the owned host (not the toolkit control) so outlines stay with the card.
            if (_searchHosts.TryGetValue(target.CardAutomationId, out var host))
            {
                host.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                card.Visibility = Visibility.Visible;
            }
            else
            {
                card.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }

            if (show)
            {
                EnsureAncestorsVisible(card);
            }
        }

        var primary = SettingsNav.FindHit(query) ?? hits[0];
        SelectNavItem(primary.SectionId, persist: true);
        HighlightMatchingCards(cardIds);
        if (primary.CardAutomationId is { } firstCard
            && (_searchHosts.TryGetValue(firstCard, out var firstHost)
                ? (UIElement)firstHost
                : FindName(firstCard) as UIElement) is { } firstElement)
        {
            firstElement.StartBringIntoView();
        }
        else
        {
            ScrSettingsContent.ChangeView(null, 0, null, disableAnimation: true);
        }
    }

    private void ClearSearchFilter()
    {
        _searchFiltering = false;
        foreach (var target in SettingsNav.Targets)
        {
            if (_searchHosts.TryGetValue(target.CardAutomationId, out var host))
            {
                host.Visibility = Visibility.Visible;
            }

            if (FindName(target.CardAutomationId) is UIElement card)
            {
                card.Visibility = Visibility.Visible;
            }
        }

        ClearSearchHighlight();
    }

    private static void EnsureAncestorsVisible(DependencyObject element)
    {
        var parent = VisualTreeHelper.GetParent(element);
        while (parent is not null)
        {
            if (parent is UIElement ue
                && (parent is CommunityToolkit.WinUI.Controls.SettingsExpander
                    || parent is CommunityToolkit.WinUI.Controls.SettingsCard
                    || (parent is Border border
                        && string.Equals(border.Tag as string, SearchHostTag, StringComparison.Ordinal))))
            {
                ue.Visibility = Visibility.Visible;
            }

            parent = VisualTreeHelper.GetParent(parent);
        }
    }

    private void SelectSection(string sectionId, bool persist, bool scrollToTop)
    {
        var id = SettingsNav.Normalize(sectionId);
        ClearSearchFilter();

        // Content panes first so list highlight and content never diverge.
        foreach (var panel in SectionPanels())
        {
            panel.Visibility = panel.Name.EndsWith("_" + id, StringComparison.Ordinal)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        SelectNavItem(id, persist);

        if (scrollToTop)
        {
            ScrSettingsContent.ChangeView(null, 0, null, disableAnimation: true);
        }
    }

    private void SelectNavItem(string sectionId, bool persist)
    {
        var id = SettingsNav.Normalize(sectionId);
        ListViewItem? match = null;
        foreach (var item in LstSettingsNav.Items.OfType<ListViewItem>())
        {
            if (string.Equals(item.Tag as string, id, StringComparison.OrdinalIgnoreCase))
            {
                match = item;
                break;
            }
        }

        if (match is not null && !ReferenceEquals(LstSettingsNav.SelectedItem, match))
        {
            _ignoreNavSelection = true;
            LstSettingsNav.SelectedItem = match;
            _ignoreNavSelection = false;
        }

        if (persist)
        {
            ApplicationData.Current.LocalSettings.Values[SettingsNav.LastSectionKey] = id;
        }
    }

    private IEnumerable<StackPanel> SectionPanels() =>
    [
        PnlSection_appearance,
        PnlSection_library,
        PnlSection_viewer,
        PnlSection_tags,
        PnlSection_input,
        PnlSection_performance,
        PnlSection_about
    ];

    /// <summary>
    /// Wrap each SettingsNav card in a Border we own. Search highlight paints that host
    /// only — toolkit SettingsExpander VSM cannot wipe it (collapsed or expanded).
    /// </summary>
    private void EnsureSearchHighlightHosts()
    {
        foreach (var target in SettingsNav.Targets)
        {
            if (_searchHosts.ContainsKey(target.CardAutomationId))
            {
                continue;
            }

            if (FindName(target.CardAutomationId) is not FrameworkElement card)
            {
                continue;
            }

            if (card.Parent is Border existing
                && string.Equals(existing.Tag as string, SearchHostTag, StringComparison.Ordinal))
            {
                _searchHosts[target.CardAutomationId] = existing;
                continue;
            }

            var host = TryWrapInSearchHost(card);
            if (host is not null)
            {
                _searchHosts[target.CardAutomationId] = host;
            }
        }
    }

    private static Border? TryWrapInSearchHost(FrameworkElement card)
    {
        var host = new Border
        {
            Tag = SearchHostTag,
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        if (card.Parent is Panel panel)
        {
            var index = panel.Children.IndexOf(card);
            if (index < 0)
            {
                return null;
            }

            panel.Children.RemoveAt(index);
            // Spacing moves to the host so the pink outline hugs the whole card/expander.
            host.Margin = card.Margin;
            card.Margin = new Thickness(0);
            host.Child = card;
            panel.Children.Insert(index, host);
            return host;
        }

        var itemsOwner = FindItemsOwner(card);
        if (itemsOwner is not null)
        {
            var index = itemsOwner.Items.IndexOf(card);
            if (index < 0)
            {
                return null;
            }

            itemsOwner.Items.RemoveAt(index);
            host.Margin = card.Margin;
            card.Margin = new Thickness(0);
            host.Child = card;
            itemsOwner.Items.Insert(index, host);
            return host;
        }

        return null;
    }

    private static ItemsControl? FindItemsOwner(FrameworkElement card)
    {
        for (var p = VisualTreeHelper.GetParent(card); p is not null; p = VisualTreeHelper.GetParent(p))
        {
            if (p is ItemsControl items && items.Items.IndexOf(card) >= 0)
            {
                return items;
            }
        }

        for (DependencyObject? p = card.Parent; p is not null;)
        {
            if (p is ItemsControl items && items.Items.IndexOf(card) >= 0)
            {
                return items;
            }

            p = p is FrameworkElement fe ? fe.Parent : VisualTreeHelper.GetParent(p);
        }

        return null;
    }

    private void HighlightMatchingCards(IReadOnlySet<string> cardIds)
    {
        ClearSearchHighlight();
        if (cardIds.Count == 0)
        {
            return;
        }

        EnsureSearchHighlightHosts();
        var highlight = CreateSearchHighlightBrush();

        foreach (var id in cardIds)
        {
            if (!_searchHosts.TryGetValue(id, out var host))
            {
                continue;
            }

            host.Background = highlight;
            host.BorderBrush = highlight;
            host.BorderThickness = new Thickness(2);
            _highlightedHosts.Add(host);
        }

        // Keep highlight while the query is active; only flash when a single timed jump remains.
        if (!_searchFiltering)
        {
            _highlightTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _highlightTimer.Tick -= HighlightTimer_Tick;
            _highlightTimer.Tick += HighlightTimer_Tick;
            _highlightTimer.Stop();
            _highlightTimer.Start();
        }
        else
        {
            _highlightTimer?.Stop();
        }
    }

    /// <summary>
    /// Fresh local pink wash. Theme lookup is best-effort; always return a new brush.
    /// </summary>
    private Brush CreateSearchHighlightBrush()
    {
        var color = Color.FromArgb(0xB3, 0xE0, 0x5A, 0x8C); // ~70% Palace pink
        if (TryResolveThemeBrush(Resources, "PalaceSettingsSearchHighlightBrush", out var fromPage)
            && fromPage is SolidColorBrush pageBrush)
        {
            color = pageBrush.Color;
            if (color.A < 0x80)
            {
                color = Color.FromArgb(0xB3, color.R, color.G, color.B);
            }
        }
        else if (Application.Current?.Resources is { } appRoot
            && TryResolveThemeBrush(appRoot, "PalaceSettingsSearchHighlightBrush", out var fromApp)
            && fromApp is SolidColorBrush appBrush)
        {
            color = appBrush.Color;
            if (color.A < 0x80)
            {
                color = Color.FromArgb(0xB3, color.R, color.G, color.B);
            }
        }

        return new SolidColorBrush(color);
    }

    private bool TryResolveThemeBrush(ResourceDictionary root, string key, out object? brush)
    {
        brush = null;
        var light = ActualTheme == ElementTheme.Light
            || (ActualTheme == ElementTheme.Default
                && Application.Current.RequestedTheme == ApplicationTheme.Light);
        var themeKeys = light ? new[] { "Light" } : new[] { "Dark", "Default" };
        return TryThemeBrushInTree(root, themeKeys, key, out brush);
    }

    private static bool TryThemeBrushInTree(
        ResourceDictionary root,
        string[] themeKeys,
        string key,
        out object? brush)
    {
        brush = null;
        foreach (var themeKey in themeKeys)
        {
            if (root.ThemeDictionaries.TryGetValue(themeKey, out var themeDict)
                && themeDict is ResourceDictionary dict
                && dict.TryGetValue(key, out var fromTheme)
                && fromTheme is not null)
            {
                brush = fromTheme;
                return true;
            }
        }

        if (root.TryGetValue(key, out var direct) && direct is not null)
        {
            brush = direct;
            return true;
        }

        foreach (var merged in root.MergedDictionaries)
        {
            if (TryThemeBrushInTree(merged, themeKeys, key, out brush))
            {
                return true;
            }
        }

        return false;
    }

    private void HighlightTimer_Tick(object? sender, object e)
    {
        _highlightTimer?.Stop();
        ClearSearchHighlight();
    }

    private void ClearSearchHighlight()
    {
        foreach (var host in _highlightedHosts)
        {
            host.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            host.BorderBrush = null;
            host.BorderThickness = new Thickness(0);
        }

        _highlightedHosts.Clear();
    }

    private void ApplyDensity(string? density)
    {
        var minHeight = AppearanceSettings.CardMinHeight(density);
        var spacing = new Thickness(0, 0, 0, AppearanceSettings.CardSpacingBottom(density));
        var padX = AppearanceSettings.CardPaddingX(density);
        var padY = AppearanceSettings.CardPaddingY(density);
        var padding = new Thickness(padX, padY, padX, padY);
        foreach (var target in SettingsNav.Targets)
        {
            if (FindName(target.CardAutomationId) is Control card)
            {
                card.MinHeight = minHeight;
                card.Padding = padding;
                // Margin lives on the owned host so density gaps stay outside the pink outline.
                if (_searchHosts.TryGetValue(target.CardAutomationId, out var host))
                {
                    card.Margin = new Thickness(0);
                    host.Margin = spacing;
                }
                else
                {
                    card.Margin = spacing;
                }
            }
        }
    }

    private static Control? FindNamedControl(DependencyObject root, string automationId)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe)
            {
                var id = fe.GetValue(AutomationProperties.AutomationIdProperty) as string;
                if (string.Equals(id, automationId, StringComparison.Ordinal) && fe is Control control)
                {
                    return control;
                }

                var nested = FindNamedControl(fe, automationId);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private void ViewModelOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsViewModel.TintHex) or nameof(SettingsViewModel.TintEnabled))
        {
            SyncTintPicker();
        }

        if (e.PropertyName is nameof(SettingsViewModel.SelectedPreset))
        {
            SyncPresetGridSelection();
        }

        if (e.PropertyName is nameof(SettingsViewModel.SelectedDensity))
        {
            ApplyDensity(ViewModel.SelectedDensity);
        }
    }

    private void SyncTintPicker()
    {
        if (!TagColor.TryParse(ViewModel.TintHex, out var color)
            && !TagColor.TryParse(ShellBackground.DefaultTintHex, out color))
        {
            color = Color.FromArgb(0x50, 0x2C, 0x3A, 0x6B);
        }

        _ignoreTintChanges = true;
        PkrShellTint.Color = color;
        DispatcherQueue.TryEnqueue(() => _ignoreTintChanges = false);
    }

    private void PkrShellTint_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_ignoreTintChanges)
        {
            return;
        }

        ViewModel.TintHex = TagColor.ToHex(args.NewColor);
    }

    private async void ShellPreset_ItemClick(object sender, ItemClickEventArgs e)
    {
        // Pointer click also selects (SelectionMode=Single); SelectionChanged applies.
        // Keep ItemClick so InvokePattern / click still applies if selection is a no-op
        // (already-selected tile re-clicked).
        if (e.ClickedItem is ShellPresetItem item)
        {
            await ViewModel.SelectBackgroundPresetAsync(item);
            SyncPresetGridSelection();
        }
    }

    /// <summary>
    /// UIA SelectionItem.Select and keyboard selection set SelectedItem —
    /// apply the background the same way as ItemClick (nav uses SelectionChanged too).
    /// </summary>
    private async void ShellPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ignorePresetSelection || sender is not GridView grid)
        {
            return;
        }

        if (grid.SelectedItem is not ShellPresetItem item)
        {
            return;
        }

        ClearOtherPresetGridSelections(grid);
        await ViewModel.SelectBackgroundPresetAsync(item);
        SyncPresetGridSelection();
    }

    private IEnumerable<GridView> ShellPresetGrids()
    {
        yield return GrdShellPresetsChrome;
        yield return GrdShellPresetsVivid;
        yield return GrdShellPresetsCreator;
        yield return GrdShellPresetsCustom;
    }

    private void ClearOtherPresetGridSelections(GridView active)
    {
        _ignorePresetSelection = true;
        try
        {
            foreach (var grid in ShellPresetGrids())
            {
                if (!ReferenceEquals(grid, active) && grid.SelectedItem is not null)
                {
                    grid.SelectedItem = null;
                }
            }
        }
        finally
        {
            _ignorePresetSelection = false;
        }
    }

    /// <summary>
    /// Align each group's SelectedItem with the model IsSelected tile (cross-group single-select).
    /// </summary>
    private void SyncPresetGridSelection()
    {
        _ignorePresetSelection = true;
        try
        {
            var selected = ViewModel.SelectedPreset;
            foreach (var grid in ShellPresetGrids())
            {
                if (selected is not null && PresetGridContains(grid, selected))
                {
                    if (!ReferenceEquals(grid.SelectedItem, selected))
                    {
                        grid.SelectedItem = selected;
                    }
                }
                else if (grid.SelectedItem is not null)
                {
                    grid.SelectedItem = null;
                }
            }
        }
        finally
        {
            _ignorePresetSelection = false;
        }
    }

    private static bool PresetGridContains(GridView grid, ShellPresetItem item)
    {
        if (grid.ItemsSource is System.Collections.IEnumerable items)
        {
            foreach (var entry in items)
            {
                if (ReferenceEquals(entry, item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private async void ShellRecentCustomRemove_Click(object sender, RoutedEventArgs e)
    {
        // WinUI RoutedEventArgs has no Handled (unlike WPF / KeyRoutedEventArgs).
        if (sender is Button { Tag: ShellPresetItem item })
        {
            await ViewModel.RemoveRecentCustomCommand.ExecuteAsync(item);
            FocusNextCustomTile(item);
        }
    }

    private async void ShellRecentCustomRemoveMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: ShellPresetItem item })
        {
            await ViewModel.RemoveRecentCustomCommand.ExecuteAsync(item);
            FocusNextCustomTile(item);
        }
    }

    private async void ShellRecentCustomLocate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton { Tag: ShellPresetItem item })
        {
            await ViewModel.LocateRecentCustomCommand.ExecuteAsync(item);
        }
    }

    private async void ShellRecentCustomLocateMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: ShellPresetItem item })
        {
            await ViewModel.LocateRecentCustomCommand.ExecuteAsync(item);
        }
    }

    private async void ShellPresetGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not GridView grid)
        {
            return;
        }

        // Prefer the focused container's item; fall back to the selected wallpaper tile.
        var focused = FocusManager.GetFocusedElement(XamlRoot) as FrameworkElement;
        ShellPresetItem? item = null;
        while (focused is not null)
        {
            if (focused is GridViewItem { Content: ShellPresetItem found })
            {
                item = found;
                break;
            }

            focused = focused.Parent as FrameworkElement;
        }

        item ??= ViewModel.SelectedPreset;

        if (e.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            if (item is not null)
            {
                e.Handled = true;
                await ViewModel.SelectBackgroundPresetAsync(item);
                SyncPresetGridSelection();
            }

            return;
        }

        if (e.Key == VirtualKey.Delete && item is { IsRecentCustom: true })
        {
            e.Handled = true;
            await ViewModel.RemoveRecentCustomCommand.ExecuteAsync(item);
            FocusNextCustomTile(item);
            return;
        }

        // App menu key / F10: Remove from recent (Shift+F10 equivalent for GridView items).
        if ((e.Key is VirtualKey.Application or VirtualKey.F10) && item is { IsRecentCustom: true })
        {
            e.Handled = true;
            if (grid.ContainerFromItem(item) is FrameworkElement container)
            {
                var flyout = new MenuFlyout();
                var remove = new MenuFlyoutItem { Text = "Remove from recent", Tag = item };
                remove.Click += ShellRecentCustomRemoveMenu_Click;
                flyout.Items.Add(remove);
                flyout.ShowAt(container);
            }
        }
    }

    private void FocusNextCustomTile(ShellPresetItem removed)
    {
        var customs = ViewModel.CustomBackgroundPresets;
        if (customs.Count == 0)
        {
            return;
        }

        var index = 0;
        for (var i = 0; i < customs.Count; i++)
        {
            if (ReferenceEquals(customs[i], removed)
                || (!string.IsNullOrWhiteSpace(removed.RecentId)
                    && string.Equals(customs[i].RecentId, removed.RecentId, StringComparison.OrdinalIgnoreCase)))
            {
                index = Math.Min(i, customs.Count - 1);
                break;
            }
        }

        // After rebuild the removed item is gone — focus Add or the tile now at that index.
        var target = index < customs.Count ? customs[index] : customs[0];
        if (GrdShellPresetsCustom.ContainerFromItem(target) is Control container)
        {
            container.Focus(FocusState.Programmatic);
        }
        else
        {
            GrdShellPresetsCustom.Focus(FocusState.Programmatic);
        }
    }

    private void InfShellRecentUndo_PointerEntered(object sender, PointerRoutedEventArgs e) =>
        ViewModel.PauseRecentCustomUndoTimer();

    private void InfShellRecentUndo_PointerExited(object sender, PointerRoutedEventArgs e) =>
        ViewModel.ResumeRecentCustomUndoTimer();

    private void InfShellRecentUndo_FocusEntered(object sender, RoutedEventArgs e) =>
        ViewModel.PauseRecentCustomUndoTimer();

    private void InfShellRecentUndo_FocusExited(object sender, RoutedEventArgs e) =>
        ViewModel.ResumeRecentCustomUndoTimer();

    private async Task<CloudEntry?> PickCloudFolderAsync(ICloudLibrary library)
    {
        var dialog = new CloudFolderPickerDialog(library, AppServices.CurrentProject.Name)
        {
            XamlRoot = XamlRoot
        };
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? dialog.Result : null;
    }

    /// <summary>Destructive Settings confirms: Cancel is the safe default.</summary>
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
}
