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
    private readonly List<(Control Card, Brush? Background, Brush? BorderBrush, Thickness BorderThickness)> _highlightedCards = [];
    private readonly List<(Border Surface, Brush? Background)> _highlightedSurfaces = [];
    private readonly HashSet<Control> _highlightTargets = [];
    private Brush? _activeHighlightBrush;
    private DispatcherTimer? _highlightTimer;
    private DispatcherTimer? _highlightRepaintTimer;
    private int _highlightRepaintPass;
    private EventHandler<object>? _highlightLayoutHandler;
    private bool _paintingHighlight;

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
            card.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show)
            {
                EnsureAncestorsVisible(card);
            }
        }

        var primary = SettingsNav.FindHit(query) ?? hits[0];
        SelectNavItem(primary.SectionId, persist: true);
        HighlightMatchingCards(cardIds);
        if (primary.CardAutomationId is { } firstCard
            && FindName(firstCard) is UIElement firstElement)
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
                    || parent is CommunityToolkit.WinUI.Controls.SettingsCard))
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

    private void HighlightMatchingCards(IReadOnlySet<string> cardIds)
    {
        ClearSearchHighlight();
        if (cardIds.Count == 0)
        {
            return;
        }

        // Own SolidColorBrush — never share a ThemeDictionary instance that VSM / Release
        // can leave unresolved or overwrite. High alpha so Release pixel-scans see pink.
        var highlight = CreateSearchHighlightBrush();
        _activeHighlightBrush = highlight;

        foreach (var id in cardIds)
        {
            var card = FindName(id) as Control
                ?? FindNamedControl(PnlSettingsContent, id);
            if (card is null)
            {
                continue;
            }

            ApplyHighlight(card, highlight);
        }

        // Release x64 is faster than Debug: filter Visibility + toolkit VSM often
        // rebuild/repaint expander headers after the first paint. Re-apply until durable.
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => RepaintActiveHighlights());
        ScheduleHighlightRepaintPasses();

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
    /// SettingsCard Background paints the row; SettingsExpander Background does not
    /// (header chrome is an inner Border / ToggleButton). Paint those surfaces + a
    /// border ring. Hook LayoutUpdated so Release rebuilds still show pink.
    /// </summary>
    private void ApplyHighlight(Control card, Brush highlight)
    {
        _highlightedCards.Add((card, card.Background, card.BorderBrush, card.BorderThickness));
        _highlightTargets.Add(card);
        PaintHighlightOnControl(card, highlight, recordSurfaces: true);

        _highlightLayoutHandler ??= HighlightTarget_LayoutUpdated;
        card.LayoutUpdated -= _highlightLayoutHandler;
        card.LayoutUpdated += _highlightLayoutHandler;
    }

    private void HighlightTarget_LayoutUpdated(object? sender, object e)
    {
        if (_paintingHighlight
            || !_searchFiltering
            || _activeHighlightBrush is null
            || sender is not Control card)
        {
            return;
        }

        if (!_highlightTargets.Contains(card))
        {
            return;
        }

        // Toolkit VSM / template expand after Visibility flips — re-wash header Borders.
        PaintHighlightOnControl(card, _activeHighlightBrush, recordSurfaces: true);
    }

    private void ScheduleHighlightRepaintPasses()
    {
        _highlightRepaintTimer?.Stop();
        _highlightRepaintTimer ??= new DispatcherTimer();
        _highlightRepaintTimer.Tick -= HighlightRepaintTimer_Tick;
        _highlightRepaintTimer.Tick += HighlightRepaintTimer_Tick;
        // Passes at ~50ms and ~200ms cover Release MSIX VSM settle (Sacb0y scanned 0.2s + 3s).
        // WinUI DispatcherTimer has no Tag — track pass with a field.
        _highlightRepaintPass = 0;
        _highlightRepaintTimer.Interval = TimeSpan.FromMilliseconds(50);
        _highlightRepaintTimer.Start();
    }

    private void HighlightRepaintTimer_Tick(object? sender, object e)
    {
        if (_highlightRepaintTimer is null)
        {
            return;
        }

        RepaintActiveHighlights();
        if (_highlightRepaintPass == 0)
        {
            _highlightRepaintPass = 1;
            _highlightRepaintTimer.Interval = TimeSpan.FromMilliseconds(150);
            return;
        }

        _highlightRepaintTimer.Stop();
    }

    private void RepaintActiveHighlights()
    {
        if (_activeHighlightBrush is null || _highlightTargets.Count == 0)
        {
            return;
        }

        foreach (var card in _highlightTargets.ToArray())
        {
            PaintHighlightOnControl(card, _activeHighlightBrush, recordSurfaces: true);
        }
    }

    private void PaintHighlightOnControl(Control card, Brush highlight, bool recordSurfaces)
    {
        _paintingHighlight = true;
        try
        {
            card.Background = highlight;
            card.BorderBrush = highlight;
            if (card.BorderThickness.Left < 2)
            {
                card.BorderThickness = new Thickness(2);
            }

            // Expander header plates live under ToggleButton Borders — Background on the
            // SettingsExpander itself paints zero header pixels (CommunityToolkit template).
            var isExpander = card is CommunityToolkit.WinUI.Controls.SettingsExpander
                || card.GetType().Name.Contains("Expander", StringComparison.Ordinal);
            if (!isExpander)
            {
                return;
            }

            card.UpdateLayout();
            foreach (var surface in FindExpanderHeaderSurfaces(card))
            {
                if (recordSurfaces
                    && !_highlightedSurfaces.Any(s => ReferenceEquals(s.Surface, surface)))
                {
                    _highlightedSurfaces.Add((surface, surface.Background));
                }

                surface.Background = highlight;
            }
        }
        finally
        {
            _paintingHighlight = false;
        }
    }

    /// <summary>
    /// Borders under the expander header ToggleButton / Expander (skip ItemsRepeater).
    /// Also takes any early Border with a Background before items — Release trees may
    /// not flag ToggleButton yet on the first pass after Visibility flips.
    /// </summary>
    private static List<Border> FindExpanderHeaderSurfaces(DependencyObject root)
    {
        var results = new List<Border>();
        WalkExpanderHeader(root, underHeaderToggle: false, depth: 0, results);
        return results;
    }

    private static void WalkExpanderHeader(
        DependencyObject node,
        bool underHeaderToggle,
        int depth,
        List<Border> results)
    {
        if (depth > 14)
        {
            return;
        }

        // Items are below the header — do not wash nested SettingsCards.
        if (node is Microsoft.UI.Xaml.Controls.ItemsRepeater)
        {
            return;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            var inHeader = underHeaderToggle
                || child is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton
                || child is Microsoft.UI.Xaml.Controls.Expander;

            if (child is Border border)
            {
                // Prefer header ToggleButton borders; also take shallow Borders that
                // already have a plate brush (template root) when ToggleButton is late.
                if (inHeader || (depth <= 4 && border.Background is not null))
                {
                    results.Add(border);
                }
            }

            WalkExpanderHeader(child, inHeader, depth + 1, results);
        }
    }

    /// <summary>
    /// Fresh local pink wash for search hits. Theme lookup is best-effort for color;
    /// the returned brush is always a new SolidColorBrush so Release/VSM cannot
    /// silently leave us with a missing shared ThemeResource.
    /// </summary>
    private Brush CreateSearchHighlightBrush()
    {
        var color = Color.FromArgb(0xB3, 0xE0, 0x5A, 0x8C); // ~70% Palace pink
        if (TryResolveThemeBrush(Resources, "PalaceSettingsSearchHighlightBrush", out var fromPage)
            && fromPage is SolidColorBrush pageBrush)
        {
            color = pageBrush.Color;
            // Bump alpha if theme brush is too faint for Release pixel scans.
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
        else
        {
            System.Diagnostics.Debug.WriteLine(
                "Settings search highlight: theme brush missing; using hard-coded pink.");
        }

        return new SolidColorBrush(color);
    }

    private bool TryResolveThemeBrush(ResourceDictionary root, string key, out object? brush)
    {
        brush = null;
        var light = ActualTheme == ElementTheme.Light
            || (ActualTheme == ElementTheme.Default
                && Application.Current.RequestedTheme == ApplicationTheme.Light);
        // Prefer Dark over Default when present (WASDK 2.x); Light alone for light chrome.
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
        _highlightRepaintTimer?.Stop();
        _activeHighlightBrush = null;

        foreach (var card in _highlightTargets)
        {
            if (_highlightLayoutHandler is not null)
            {
                card.LayoutUpdated -= _highlightLayoutHandler;
            }
        }

        _highlightTargets.Clear();

        foreach (var (card, background, borderBrush, borderThickness) in _highlightedCards)
        {
            card.Background = background;
            card.BorderBrush = borderBrush;
            card.BorderThickness = borderThickness;
        }

        _highlightedCards.Clear();

        foreach (var (surface, background) in _highlightedSurfaces)
        {
            surface.Background = background;
        }

        _highlightedSurfaces.Clear();
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
                card.Margin = spacing;
                card.Padding = padding;
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
