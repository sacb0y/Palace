using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
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
    /// </summary>
    private async Task<string?> AskMoveDestinationAsync()
    {
        var roots = ViewModel.LocalFolderRoots;
        if (roots.Count == 0)
        {
            ViewModel.InfoMessage = "Add a local folder to the Library first. Files can only move inside your Library folders.";
            ViewModel.ShowInfo = true;
            return null;
        }

        var rootPaths = roots.Select(r => r.Path).ToList();
        ViewModel.RefreshMoveRecents();
        var all = new List<MoveDestinationItem>();
        all.AddRange(ViewModel.MoveRecents);
        foreach (var root in roots)
        {
            AddFolderRows(all, root, rootPaths);
        }

        var shown = new System.Collections.ObjectModel.ObservableCollection<MoveDestinationItem>(all);
        var filter = new TextBox { PlaceholderText = "Filter folders" };
        AutomationProperties.SetAutomationId(filter, "TxtMoveFilter");
        AutomationProperties.SetName(filter, "Filter folders");
        var list = new ListView
        {
            ItemsSource = shown,
            ItemTemplate = Resources["MoveDestinationTemplate"] as DataTemplate,
            SelectionMode = ListViewSelectionMode.Single,
            Height = 300
        };
        AutomationProperties.SetAutomationId(list, "LstMoveDestinations");
        AutomationProperties.SetName(list, "Destination folders");

        var newName = new TextBox { PlaceholderText = "New folder name", MinWidth = 220 };
        AutomationProperties.SetAutomationId(newName, "TxtMoveNewFolder");
        AutomationProperties.SetName(newName, "New folder name");
        var create = new Button { Content = "New folder" };
        AutomationProperties.SetAutomationId(create, "BtnMoveNewFolder");
        AutomationProperties.SetName(create, "New folder");
        var browse = new HyperlinkButton { Content = "Browse…" };
        AutomationProperties.SetAutomationId(browse, "BtnMoveBrowse");
        var error = new TextBlock
        {
            Foreground = Application.Current.Resources["SystemFillColorCriticalBrush"] as Microsoft.UI.Xaml.Media.Brush,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetAutomationId(error, "TxtMoveError");
        AutomationProperties.SetLiveSetting(error, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);

        var createRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        createRow.Children.Add(newName);
        createRow.Children.Add(create);
        var hint = new TextBlock
        {
            Text = "New folders go inside the selected folder.",
            Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush
        };
        var panel = new StackPanel { Spacing = 8, MinWidth = 420 };
        panel.Children.Add(filter);
        panel.Children.Add(list);
        panel.Children.Add(createRow);
        panel.Children.Add(hint);
        panel.Children.Add(error);
        panel.Children.Add(browse);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Move to folder",
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
        filter.TextChanged += (_, _) =>
        {
            var text = filter.Text.Trim();
            shown.Clear();
            foreach (var row in all.Where(r => text.Length == 0
                         || r.Display.Contains(text, StringComparison.CurrentCultureIgnoreCase)))
            {
                shown.Add(row);
            }
        };

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

            var parentRow = all.FindIndex(r => !r.IsRecent && FolderNames.SamePath(r.Path, parent));
            var row = MoveDestinationItem.For(result.Path, rootPaths, isRecent: false);
            all.Insert(parentRow >= 0 ? parentRow + 1 : all.Count, row);
            filter.Text = "";
            shown.Clear();
            foreach (var r in all)
            {
                shown.Add(r);
            }

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

            if (FileOpService.ResolveRoot(folder.Path, await AppServices.Catalog.GetSourceFoldersAsync(AppServices.CurrentProject.Id)) is null)
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
        if (browsed is not null)
        {
            return browsed;
        }

        if (choice == ContentDialogResult.Primary || _moveDialogAccepted)
        {
            return (list.SelectedItem as MoveDestinationItem)?.Path;
        }

        return null;
    }

    private bool _moveDialogAccepted;

    private static void AddFolderRows(List<MoveDestinationItem> rows, FolderNode node, IReadOnlyList<string> roots)
    {
        rows.Add(MoveDestinationItem.For(node.Path, roots, isRecent: false));
        foreach (var child in node.Children)
        {
            AddFolderRows(rows, child, roots);
        }
    }
}
