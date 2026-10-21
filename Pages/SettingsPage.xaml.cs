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
    private Control? _highlightedCard;
    private Brush? _highlightRestoreBrush;
    private DispatcherTimer? _highlightTimer;

    public SettingsViewModel ViewModel => AppServices.Settings;

    public SettingsPage()
    {
        InitializeComponent();
        // System theme (or late ActualTheme) must flip packaged light/dark preset thumbs.
        ActualThemeChanged += (_, _) => ViewModel.RefreshBackgroundPresetSwatches();
        SizeChanged += SettingsPage_SizeChanged;
        Loaded += async (_, _) =>
        {
            ViewModel.RequestPickCloudFolder = PickCloudFolderAsync;
            ViewModel.RequestConfirm = AskConfirmAsync;
            await ErrorReporter.RunAsync("Load settings", null, ViewModel.LoadAsync);
            SyncTintPicker();
            ViewModel.RefreshBackgroundPresetSwatches();
            ApplyContentCap(ActualWidth);
            RestoreLastSection(scrollToTop: true);
        };
        Unloaded += (_, _) =>
        {
            ViewModel.RequestPickCloudFolder = null;
            ViewModel.RequestConfirm = null;
            ViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
            ClearSearchHighlight();
            _highlightTimer?.Stop();
        };
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
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
    /// Content capped at 1000px and left-aligned when the Settings page is wider than 1100px.
    /// </summary>
    private void ApplyContentCap(double pageWidth)
    {
        var capWindow = (double)Application.Current.Resources["PalaceSettingsContentCapWindow"];
        var cap = (double)Application.Current.Resources["PalaceSettingsContentCap"];
        PnlSettingsContent.MaxWidth = pageWidth > capWindow ? cap : double.PositiveInfinity;
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

        var id = item.Tag as string ?? SettingsNav.DefaultSectionId;
        SelectSection(id, persist: true, scrollToTop: true);
    }

    private void TxtSettingsSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var hit = SettingsNav.FindHit(TxtSettingsSearch.Text);
        if (hit is null)
        {
            ClearSearchHighlight();
            return;
        }

        SelectSection(hit.Value.SectionId, persist: true, scrollToTop: hit.Value.CardAutomationId is null);
        if (hit.Value.CardAutomationId is { } cardId)
        {
            HighlightCard(cardId);
        }
        else
        {
            ClearSearchHighlight();
        }
    }

    private void SelectSection(string sectionId, bool persist, bool scrollToTop)
    {
        var id = SettingsNav.Normalize(sectionId);

        // Content panes first so list highlight and content never diverge.
        foreach (var panel in SectionPanels())
        {
            panel.Visibility = panel.Name.EndsWith("_" + id, StringComparison.Ordinal)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

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

        if (scrollToTop)
        {
            ScrSettingsContent.ChangeView(null, 0, null, disableAnimation: true);
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

    private void HighlightCard(string automationId)
    {
        ClearSearchHighlight();
        // Card AutomationIds match x:Name (CardTheme, …). SettingsCard is a Control.
        var card = FindName(automationId) as Control
            ?? FindNamedControl(PnlSettingsContent, automationId);
        if (card is null)
        {
            return;
        }

        _highlightedCard = card;
        _highlightRestoreBrush = card.Background;
        if (Application.Current.Resources.TryGetValue("PalaceSettingsSearchHighlightBrush", out var brush)
            && brush is Brush highlight)
        {
            card.Background = highlight;
        }

        card.StartBringIntoView();
        _highlightTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _highlightTimer.Tick -= HighlightTimer_Tick;
        _highlightTimer.Tick += HighlightTimer_Tick;
        _highlightTimer.Stop();
        _highlightTimer.Start();
    }

    private void HighlightTimer_Tick(object? sender, object e)
    {
        _highlightTimer?.Stop();
        ClearSearchHighlight();
    }

    private void ClearSearchHighlight()
    {
        if (_highlightedCard is not null)
        {
            _highlightedCard.Background = _highlightRestoreBrush;
            _highlightedCard = null;
            _highlightRestoreBrush = null;
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
        if (e.ClickedItem is ShellPresetItem item)
        {
            await ViewModel.SelectBackgroundPresetAsync(item);
        }
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
