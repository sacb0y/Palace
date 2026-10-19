using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Palace.Pages;

/// <summary>Library page: folder tree menu, inline folder name editor, and dragging tiles onto folders.</summary>
public sealed partial class LibraryPage
{
    private bool _lastPressWasMouse = true;

    public static IRelayCommand<FolderNode?> GetNewFolderCommand() => AppServices.Library.NewFolderCommand;

    public static IRelayCommand<FolderNode?> GetRenameFolderCommand() => AppServices.Library.RenameFolderCommand;

    public static IRelayCommand<FolderNode?> GetDeleteFolderCommand() => AppServices.Library.DeleteFolderCommand;

    public static IRelayCommand<FolderNode?> GetShowFolderInExplorerCommand() => AppServices.Library.ShowFolderInExplorerCommand;

    /// <summary>Ctrl+Shift+N: new folder in the selected folder (outside text boxes).</summary>
    private bool TryHandleNewFolderKey(KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.N
            || !IsKeyDown(VirtualKey.Control)
            || !IsKeyDown(VirtualKey.Shift)
            || IsKeyDown(VirtualKey.Menu)
            || ViewModel.IsGalleryOverlayOpen
            || FocusIsTextInput(e.OriginalSource))
        {
            return false;
        }

        ViewModel.NewFolderCommand.Execute(null);
        return true;
    }

    /// <summary>F2 renames and Delete removes (empty) the focused tree folder.</summary>
    private bool TryHandleTreeFolderKey(KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.F2 or VirtualKey.Delete)
            || e.OriginalSource is not TreeViewItem container
            || IsKeyDown(VirtualKey.Control)
            || IsKeyDown(VirtualKey.Menu))
        {
            return false;
        }

        var node = NodeOf(container) ?? TreFolders.ItemFromContainer(container) as FolderNode;
        if (node is null || node.IsEditing)
        {
            return false;
        }

        if (e.Key == VirtualKey.F2)
        {
            ViewModel.RenameFolderCommand.Execute(node);
        }
        else
        {
            ViewModel.DeleteFolderCommand.Execute(node);
        }

        return true;
    }

    private void FolderMenu_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout
            || (NodeOf(flyout.Target) ?? TreFolders.ItemFromContainer(flyout.Target) as FolderNode) is not { } node)
        {
            return;
        }

        foreach (var item in flyout.Items.OfType<MenuFlyoutItem>())
        {
            switch (Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(item))
            {
                case "MnuNewSubfolder":
                    item.IsEnabled = ViewModel.CanAddFolderIn(node);
                    break;
                case "MnuRenameFolder":
                case "MnuDeleteFolder":
                    item.IsEnabled = ViewModel.CanEditFolder(node);
                    break;
                case "MnuFolderInExplorer":
                    item.IsEnabled = !node.IsPending;
                    break;
            }
        }
    }

    private void FolderEditor_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        }
    }

    private async void FolderEditor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox box || NodeOf(box) is not { } node)
        {
            return;
        }

        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            ViewModel.CancelFolderEdit(node);
            TreFolders.Focus(FocusState.Programmatic);
        }
        else if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            var text = box.Text;
            await ErrorReporter.RunAsync("Folder name", ViewModel.Notify, async () =>
            {
                if (await ViewModel.CommitFolderEditAsync(node, text, keepOpenOnError: true))
                {
                    TreFolders.Focus(FocusState.Programmatic);
                }
            });
        }
    }

    private async void FolderEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && NodeOf(box) is { IsEditing: true } node)
        {
            var text = box.Text;
            await ErrorReporter.RunAsync("Folder name", ViewModel.Notify,
                () => ViewModel.CommitFolderEditAsync(node, text, keepOpenOnError: false));
        }
    }

    private void AssetItem_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        var item = FindAssetItem(sender);
        if (item is null
            || !AssetDrag.CanStart(_lastPressWasMouse, ViewModel.IsSelectMode, item.IsSelected, item.IsFolderHeader, ViewModel.IsGalleryOverlayOpen))
        {
            args.Cancel = true;
            return;
        }

        var ids = ViewModel.DragAssetIds(item);
        if (ids.Count == 0)
        {
            args.Cancel = true;
            return;
        }

        args.AllowedOperations = DataPackageOperation.Move;
        args.Data.RequestedOperation = DataPackageOperation.Move;
        args.Data.Properties[AssetDrag.DataKey] = AssetDrag.Serialize(ids);
        args.Data.Properties.Title = ids.Count == 1 ? item.FileName : $"{ids.Count} files";
    }

    private static FolderNode? NodeOf(object? element) =>
        element is FrameworkElement fe ? fe.DataContext as FolderNode ?? fe.Tag as FolderNode : null;

    private static FolderNode? DropNode(object sender) => NodeOf(sender);

    private void FolderItem_DragOver(object sender, DragEventArgs e)
    {
        var node = DropNode(sender);
        if (node is null
            || !ViewModel.CanDropOn(node)
            || !e.DataView.Properties.TryGetValue(AssetDrag.DataKey, out var raw))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        var count = AssetDrag.Parse(raw).Count;
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.Caption = AssetDrag.Caption(count, node.Name);
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = true;
        e.Handled = true;
    }

    private async void FolderItem_Drop(object sender, DragEventArgs e)
    {
        var node = DropNode(sender);
        if (node is null
            || !ViewModel.CanDropOn(node)
            || !e.DataView.Properties.TryGetValue(AssetDrag.DataKey, out var raw))
        {
            return;
        }

        e.Handled = true;
        var ids = AssetDrag.Parse(raw);
        if (ids.Count > 0)
        {
            await ViewModel.MoveAssetIdsToAsync(ids, node.Path);
        }
    }
}
