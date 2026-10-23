using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Palace.ViewModels;
using Windows.System;
using Windows.UI.Core;

namespace Palace.Pages;

/// <summary>Library page: Move dialog, "Move to recent" menu, and Ctrl+Z / M shortcuts.</summary>
public sealed partial class LibraryPage
{
    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    /// <summary>
    /// Ctrl+Z undoes the last file operation. Handled on the tunneling PreviewKeyDown (not a
    /// KeyboardAccelerator) so a focused TextBox / search box keeps its own text undo.
    /// </summary>
    private void LibraryRoot_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (TryHandleNewFolderKey(e) || TryHandleTreeFolderKey(e))
        {
            e.Handled = true;
            return;
        }

        if (e.Key != VirtualKey.Z
            || !IsKeyDown(VirtualKey.Control)
            || IsKeyDown(VirtualKey.Shift)
            || IsKeyDown(VirtualKey.Menu)
            || ViewModel.IsGalleryOverlayOpen
            || FocusIsTextInput(e.OriginalSource))
        {
            return;
        }

        if (ViewModel.UndoFileOpCommand.CanExecute(null) && ViewModel.CanUndoFileOp)
        {
            ViewModel.UndoFileOpCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>M on the focused mosaic opens Move to folder for the selection.</summary>
    private bool TryHandleMosaicMoveKey(KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.M
            || ViewModel.IsGalleryOverlayOpen
            || IsKeyDown(VirtualKey.Control)
            || IsKeyDown(VirtualKey.Menu)
            || IsKeyDown(VirtualKey.Shift)
            || FocusIsTextInput(e.OriginalSource))
        {
            return false;
        }

        var item = FindAssetItem(e.OriginalSource);
        if (item is { IsFolderHeader: false })
        {
            EnsureSelectedForContext(item);
        }

        ViewModel.MoveToFolderCommand.Execute(null);
        return true;
    }

    /// <summary>Fill the tile context menu's "Move to recent" sub-menu (hidden when empty).</summary>
    private void PopulateMoveRecents(MenuFlyout flyout)
    {
        var sub = flyout.Items.OfType<MenuFlyoutSubItem>()
            .FirstOrDefault(item => AutomationProperties.GetAutomationId(item) == "MnuMoveToRecent");
        if (sub is null)
        {
            return;
        }

        ViewModel.RefreshMoveRecents();
        sub.Items.Clear();
        var index = 0;
        foreach (var recent in ViewModel.MoveRecents)
        {
            var item = new MenuFlyoutItem
            {
                Text = recent.Display,
                Command = ViewModel.MoveToRecentCommand,
                CommandParameter = recent.Path
            };
            ToolTipService.SetToolTip(item, recent.Path);
            AutomationProperties.SetAutomationId(item, $"MnuMoveRecent_{index++}");
            sub.Items.Add(item);
        }

        sub.Visibility = sub.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Move dialog: pick a folder inside the project's watched folders (recents first), make a new
    /// folder under the picked one, or Browse… with the system picker as a fallback.
    /// Folders created in this dialog session are passed back so Move can fold them into one Undo.
    /// </summary>
    private async Task<MoveDestinationChoice?> AskMoveDestinationAsync()
    {
        var roots = ViewModel.LocalFolderRoots;
        if (roots.Count == 0)
        {
            ViewModel.Notify(
                "Add a local folder to the Library first. Files can only move inside your Library folders.",
                InfoBarSeverity.Warning);
            return null;
        }

        var createdInDialog = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var rootPaths = roots.Select(r => r.Path).ToList();
        ViewModel.RefreshMoveRecents();
        var recents = ViewModel.MoveRecents.ToList();
        var allFolders = new List<MoveDestinationItem>();
        foreach (var root in roots)
        {
            AddFolderRows(allFolders, root, rootPaths);
        }

        var shown = new System.Collections.ObjectModel.ObservableCollection<object>();
        void RebuildShown(string filterText)
        {
            shown.Clear();
            var text = filterText.Trim();
            bool Match(MoveDestinationItem row) =>
                text.Length == 0 || row.Display.Contains(text, StringComparison.CurrentCultureIgnoreCase);

            var recentHits = recents.Where(Match).ToList();
            if (recentHits.Count > 0)
            {
                shown.Add(new MoveDestinationHeader("Recent"));
                foreach (var row in recentHits)
                {
                    shown.Add(row);
                }
            }

            var folderHits = allFolders.Where(Match).ToList();
            if (folderHits.Count > 0)
            {
                shown.Add(new MoveDestinationHeader("All folders"));
                foreach (var row in folderHits)
                {
                    shown.Add(row);
                }
            }
        }

        RebuildShown("");

        var selection = await ViewModel.SelectedAssetsAsync();
        var title = selection.Count == 1
            ? $"Move {FileOpService.Quote(selection[0].FileName)}"
            : $"Move {selection.Count} items";
        title += SelectMode.CollapsedClause(
            ViewModel.CountCollapsedAmongTargetIds(selection.Select(t => t.Id)));

        var caption = new TextBlock
        {
            Text = "Choose a destination",
            Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush
        };
        var filter = new TextBox { PlaceholderText = "Filter folders" };
        AutomationProperties.SetAutomationId(filter, "TxtMoveFilter");
        AutomationProperties.SetName(filter, "Filter folders");
        var list = new ListView
        {
            ItemsSource = shown,
            ItemTemplateSelector = new MoveDestinationTemplateSelector
            {
                DestinationTemplate = Resources["MoveDestinationTemplate"] as DataTemplate,
                HeaderTemplate = Resources["MoveDestinationHeaderTemplate"] as DataTemplate
            },
            SelectionMode = ListViewSelectionMode.Single,
            MinHeight = 120,
            MaxHeight = 300
        };
        ApplyAccentListSelection(list);
        AutomationProperties.SetAutomationId(list, "LstMoveDestinations");
        AutomationProperties.SetName(list, "Destination folders");

        var newName = new TextBox
        {
            PlaceholderText = "New folder name",
            Description = "Goes inside the selected folder, or the current folder"
        };
        AutomationProperties.SetAutomationId(newName, "TxtMoveNewFolder");
        AutomationProperties.SetName(newName, "New folder name");
        var create = new Button { Content = "New folder", VerticalAlignment = VerticalAlignment.Bottom };
        AutomationProperties.SetAutomationId(create, "BtnMoveNewFolder");
        AutomationProperties.SetName(create, "New folder");
        var createGrid = new Grid { ColumnSpacing = 8 };
        createGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        createGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        createGrid.Children.Add(newName);
        Grid.SetColumn(create, 1);
        createGrid.Children.Add(create);

        var browse = new HyperlinkButton { Content = "Browse…" };
        AutomationProperties.SetAutomationId(browse, "BtnMoveBrowse");
        var error = new TextBlock
        {
            Foreground = Application.Current.Resources["SystemFillColorCriticalBrush"] as Microsoft.UI.Xaml.Media.Brush,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetAutomationId(error, "TxtMoveError");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);

        var panel = new StackPanel { Spacing = 8, MinWidth = 420 };
        panel.Children.Add(caption);
        panel.Children.Add(filter);
        panel.Children.Add(list);
        panel.Children.Add(createGrid);
        panel.Children.Add(error);
        panel.Children.Add(browse);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            PrimaryButtonText = "Move here",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            Content = panel
        };
        dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;

        void ShowError(string? message)
        {
            error.Text = message ?? "";
            error.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
            if (!string.IsNullOrEmpty(message))
            {
                var peer = FrameworkElementAutomationPeer.FromElement(error)
                    ?? FrameworkElementAutomationPeer.CreatePeerForElement(error);
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
        }

        string? browsed = null;
        list.SelectionChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled = list.SelectedItem is MoveDestinationItem;
            ShowError(null);
        };
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is MoveDestinationItem)
            {
                browsed = null;
                dialog.Hide();
                _moveDialogAccepted = true;
            }
        };
        filter.TextChanged += (_, _) => RebuildShown(filter.Text);

        async Task CreateAsync()
        {
            var parent = (list.SelectedItem as MoveDestinationItem)?.Path
                ?? ViewModel.SelectedFolder?.Path
                ?? rootPaths[0];
            var result = await ViewModel.CreateFolderAsync(parent, newName.Text);
            if (!result.Ok || result.Path is null)
            {
                ShowError(result.Message);
                return;
            }

            createdInDialog.Add(Path.GetFullPath(result.Path));
            var parentRow = allFolders.FindIndex(r => FolderNames.SamePath(r.Path, parent));
            var row = MoveDestinationItem.For(result.Path, rootPaths, isRecent: false);
            allFolders.Insert(parentRow >= 0 ? parentRow + 1 : allFolders.Count, row);
            filter.Text = "";
            RebuildShown("");
            list.SelectedItem = row;
            list.ScrollIntoView(row);
            newName.Text = "";
            ShowError(null);
        }

        create.Click += async (_, _) => await ErrorReporter.RunAsync("New folder", ShowError, CreateAsync);
        newName.KeyDown += async (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                await ErrorReporter.RunAsync("New folder", ShowError, CreateAsync);
            }
        };
        browse.Click += async (_, _) => await ErrorReporter.RunAsync("Browse for a folder", ShowError, async () =>
        {
            var folder = await AppServices.Access.PickFolderAsync();
            if (folder is null)
            {
                return;
            }

            if (FileOpService.ResolveWritableRoot(
                    folder.Path,
                    await AppServices.Catalog.GetSourceFoldersAsync(),
                    AppServices.CurrentProject.Id,
                    out _) is null)
            {
                ShowError("That folder isn't inside your Library folders. Add it to the Library first, or pick a folder inside one.");
                return;
            }

            browsed = folder.Path;
            _moveDialogAccepted = true;
            dialog.Hide();
        });

        _moveDialogAccepted = false;
        var choice = await dialog.ShowAsync();
        string? path = browsed;
        if (path is null && (choice == ContentDialogResult.Primary || _moveDialogAccepted))
        {
            path = (list.SelectedItem as MoveDestinationItem)?.Path;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var full = Path.GetFullPath(path);
        var absorb = createdInDialog.Contains(full) ? full : null;
        return new MoveDestinationChoice(full, absorb);
    }

    private bool _moveDialogAccepted;

    private static void ApplyAccentListSelection(ListView list)
    {
        // Prefer ThemeResource from the list so Light gets TextOnAccent on Default fill
        // (AccentText on Tertiary is dark-on-mid and fails contrast).
        var light = list.ActualTheme == ElementTheme.Light
            || (list.ActualTheme == ElementTheme.Default
                && Application.Current.RequestedTheme == ApplicationTheme.Light);

        var resources = Application.Current.Resources;
        var selectedKey = light ? "AccentFillColorDefaultBrush" : "AccentFillColorTertiaryBrush";
        var foregroundKey = light ? "TextOnAccentFillColorPrimaryBrush" : "AccentTextFillColorPrimaryBrush";

        if (TryThemeBrush(resources, list.ActualTheme, selectedKey, out var selected))
        {
            list.Resources["ListViewItemBackgroundSelected"] = selected;
        }

        if (TryThemeBrush(resources, list.ActualTheme, "AccentFillColorSecondaryBrush", out var over))
        {
            list.Resources["ListViewItemBackgroundSelectedPointerOver"] = over;
        }

        if (TryThemeBrush(resources, list.ActualTheme, foregroundKey, out var foreground))
        {
            list.Resources["ListViewItemForegroundSelected"] = foreground;
        }
    }

    private static bool TryThemeBrush(
        ResourceDictionary root,
        ElementTheme theme,
        string key,
        out object brush)
    {
        brush = null!;
        var light = theme == ElementTheme.Light
            || (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Light);
        // Prefer Dark over Default when present (WASDK 2.x); fall back to Default.
        var themeKeys = light ? new[] { "Light" } : new[] { "Dark", "Default" };

        if (TryThemeBrushInTree(root, themeKeys, key, out brush))
        {
            return true;
        }

        return root.TryGetValue(key, out brush!) && brush is not null;
    }

    private static bool TryThemeBrushInTree(
        ResourceDictionary root,
        string[] themeKeys,
        string key,
        out object brush)
    {
        brush = null!;
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

        foreach (var merged in root.MergedDictionaries)
        {
            if (TryThemeBrushInTree(merged, themeKeys, key, out brush))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddFolderRows(List<MoveDestinationItem> rows, FolderNode node, IReadOnlyList<string> roots)
    {
        rows.Add(MoveDestinationItem.For(node.Path, roots, isRecent: false));
        foreach (var child in node.Children)
        {
            AddFolderRows(rows, child, roots);
        }
    }
}

/// <summary>Non-selectable section header in the Move dialog list (public for x:DataType).</summary>
public sealed class MoveDestinationHeader
{
    public MoveDestinationHeader(string title) => Title = title;
    public string Title { get; }
    public override string ToString() => Title;
}

public sealed class MoveDestinationTemplateSelector : DataTemplateSelector
{
    public DataTemplate? DestinationTemplate { get; set; }
    public DataTemplate? HeaderTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) =>
        item is MoveDestinationHeader ? HeaderTemplate : DestinationTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
