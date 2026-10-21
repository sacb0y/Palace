using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Palace.ViewModels;

/// <summary>Library: gallery overlay and separate gallery windows.</summary>
public partial class LibraryViewModel
{
    [RelayCommand]
    private void OpenOverlay(AssetItem? item)
    {
        if (item is { IsFolderHeader: true })
        {
            SelectFolderGroup(item);
            return;
        }

        if (!TryCreateGallery(item, out var gallery))
        {
            return;
        }

        OverlayGallery = gallery;
        IsGalleryOverlayOpen = true;
        HydrateAfterLibraryOpen(item is { IsFolderHeader: true } ? gallery.Current : item ?? gallery.Current);
    }

    [RelayCommand]
    private void CloseOverlay()
    {
        IsGalleryOverlayOpen = false;
        OverlayGallery?.StopGifPlayback();
        OverlayGallery = null;
    }

    [RelayCommand]
    private void OpenInNewWindow(AssetItem? item)
    {
        if (item is { IsFolderHeader: true })
        {
            SelectFolderGroup(item);
            return;
        }

        if (RequestOpenGalleryWindow is null || !TryCreateGallery(item, out var gallery))
        {
            return;
        }

        RequestOpenGalleryWindow(gallery);
        HydrateAfterLibraryOpen(item is { IsFolderHeader: true } ? gallery.Current : item ?? gallery.Current);
    }

    private bool TryCreateGallery(AssetItem? item, out GalleryViewModel gallery)
    {
        gallery = null!;
        var start = item is { IsFolderHeader: false } ? item
            : SelectedAsset is { IsFolderHeader: false } ? SelectedAsset
            : _selection.LastOrDefault(asset => !asset.IsFolderHeader);
        var snapshot = Assets.Where(asset => !asset.IsFolderHeader).ToList();
        if (!GalleryMedia.CanOpen(snapshot.Count, start is not null))
        {
            Notify("Select an asset first.");
            return false;
        }

        var index = GalleryMedia.StartIndex(snapshot.Select(a => a.Id).ToList(), start!.Id);
        gallery = new GalleryViewModel(snapshot, index < 0 ? 0 : index, _catalog);
        return true;
    }

    private static void HydrateAfterLibraryOpen(AssetItem? item)
    {
        if (item is null || !AccessService.WouldHydrateOnOpen(item.Path))
        {
            return;
        }

        _ = AppServices.Hydration.HydrateAfterOpenAsync(item.Id);
    }

    /// <summary>
    /// Overlay long-press: toggle this item's selection + check badge
    /// (video long-press is select, not 2×).
    /// </summary>
    public void ToggleOverlaySelectionFromHold()
    {
        if (!SelectMode.ViewerHoldTogglesSelection(IsGalleryOverlayOpen)
            || OverlayGallery?.Current is not { IsFolderHeader: false } current)
        {
            return;
        }

        IsSelectMode = true;
        SelectAsset(current, toggle: true);
    }

    /// <summary>Review swipe right / Keep: leave file in place and advance.</summary>
    [RelayCommand]
    private void ReviewKeep()
    {
        if (OverlayGallery is null)
        {
            return;
        }

        if (OverlayGallery.CanGoNext)
        {
            OverlayGallery.GoNextCommand.Execute(null);
        }
        else
        {
            CloseOverlay();
        }
    }

    /// <summary>Review swipe up / Skip: undecided, no move, advance.</summary>
    [RelayCommand]
    private void ReviewSkip() => ReviewKeep();

    /// <summary>
    /// Review swipe left / Reject: move current file into <c>_rejected</c>
    /// under its watched source, then advance (Undo restores).
    /// </summary>
    [RelayCommand]
    private Task ReviewRejectAsync() =>
        ErrorReporter.RunAsync("Reject", Notify, async () =>
        {
            if (OverlayGallery?.Current is not { IsFolderHeader: false } current
                || string.IsNullOrEmpty(current.Id))
            {
                return;
            }

            var projectId = AppServices.CurrentProject.Id;
            var sources = await _catalog.GetSourceFoldersAsync(projectId);
            var root = FileOpService.ResolveRoot(current.Path, sources);
            if (root is null || string.IsNullOrWhiteSpace(root.Path))
            {
                Notify("Cannot reject: file is outside a watched folder.");
                return;
            }

            var dest = TouchGestures.RejectedFolderPath(root.Path);
            if (!Directory.Exists(dest))
            {
                var created = await _fileOps.CreateFolderAsync(
                    root.Path,
                    TouchGestures.RejectedFolderName,
                    projectId);
                if (!created.Ok && !Directory.Exists(dest))
                {
                    Notify(created.Message);
                    return;
                }
            }

            var assets = await _catalog.GetAssetsByIdsAsync([current.Id]);
            var canAdvance = OverlayGallery.CanGoNext;
            await MoveAssetsToAsync(assets, dest, closeOverlay: false);
            if (canAdvance && OverlayGallery is not null)
            {
                OverlayGallery.GoNextCommand.Execute(null);
            }
            else
            {
                CloseOverlay();
            }
        });
}
