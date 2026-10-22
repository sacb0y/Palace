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
    /// Context-menu <c>Select</c>: enter select mode with the overlay current
    /// asset selected (viewer long-press opens the menu, not a badge).
    /// Review-mode long-press is a no-op (<see cref="TouchGestures.ViewerHoldOpensContext"/>).
    /// </summary>
    public void SelectOverlayCurrentFromMenu()
    {
        if (!IsGalleryOverlayOpen
            || OverlayGallery?.Current is not { IsFolderHeader: false } current)
        {
            return;
        }

        IsSelectMode = true;
        SelectAsset(current, toggle: false);
    }

    // Review Keep / Skip / Reject + IsReviewMode live in LibraryViewModel.Review.cs
    // (session, skip undo, sibling _rejected, progress). Do not re-add stubs here —
    // RelayCommand / ObservableProperty names must stay unique across partials.
}
