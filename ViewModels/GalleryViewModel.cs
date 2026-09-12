using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Palace.Services.Cloud;

namespace Palace.ViewModels;

public partial class GalleryViewModel : ObservableObject
{
    private readonly CatalogService _catalog;
    private readonly IReadOnlyList<AssetItem> _items;
    private int _loadEpoch;

    public GalleryViewModel(IReadOnlyList<AssetItem> items, int startIndex, CatalogService catalog)
    {
        _catalog = catalog;
        _items = items.Count == 0 ? [] : items.ToList();
        var last = Math.Max(0, _items.Count - 1);
        CurrentIndex = _items.Count == 0 ? 0 : Math.Clamp(startIndex, 0, last);
        _ = LoadCurrentAsync();
    }

    public ObservableCollection<AssignedTagItem> Tags { get; } = [];

    [ObservableProperty]
    public partial int CurrentIndex { get; set; }

    [ObservableProperty]
    public partial AssetItem? Current { get; set; }

    [ObservableProperty]
    public partial string? CurrentPath { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string PositionLabel { get; set; } = "";

    [ObservableProperty]
    public partial bool IsImage { get; set; }

    [ObservableProperty]
    public partial bool IsVideo { get; set; }

    [ObservableProperty]
    public partial bool IsMissing { get; set; }

    [ObservableProperty]
    public partial bool CanGoPrevious { get; set; }

    [ObservableProperty]
    public partial bool CanGoNext { get; set; }

    [ObservableProperty]
    public partial string? PreviewImageUri { get; set; }

    [ObservableProperty]
    public partial bool IsCloudPreview { get; set; }

    [ObservableProperty]
    public partial string PreviewStatus { get; set; } = "";

    [ObservableProperty]
    public partial bool CanDownloadOriginal { get; set; }

    [ObservableProperty]
    public partial bool CanOpenInExplorer { get; set; }

    partial void OnCurrentIndexChanged(int value) => _ = LoadCurrentAsync();

    [RelayCommand]
    private void GoPrevious()
    {
        if (CurrentIndex > 0)
        {
            CurrentIndex--;
        }
    }

    [RelayCommand]
    private void GoNext()
    {
        if (CurrentIndex < _items.Count - 1)
        {
            CurrentIndex++;
        }
    }

    [RelayCommand]
    private async Task DownloadOriginalAsync()
    {
        var item = Current;
        if (item is null || string.IsNullOrEmpty(item.CloudItemId))
        {
            return;
        }

        var folder = await AppServices.Access.PickFolderAsync();
        if (folder is null)
        {
            return;
        }

        try
        {
            var url = await ResolveOriginalUrlAsync(item);
            if (string.IsNullOrEmpty(url))
            {
                PreviewStatus = "Could not get a download link.";
                return;
            }

            var dest = Path.Combine(folder.Path, item.FileName);
            dest = PathSafe.UniquePath(dest);
            await using var stream = await CloudOAuth.GetStreamAsync(url, null, CancellationToken.None);
            if (stream is null)
            {
                PreviewStatus = "Download failed.";
                return;
            }

            await using var output = File.Create(dest);
            await stream.CopyToAsync(output);
            PreviewStatus = "Downloaded " + Path.GetFileName(dest) + ".";
        }
        catch (Exception)
        {
            PreviewStatus = "Download failed.";
        }
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        var path = Current?.Path;
        if (string.IsNullOrEmpty(path) || !CloudFile.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true
        });
    }

    private async Task LoadCurrentAsync()
    {
        var epoch = Interlocked.Increment(ref _loadEpoch);
        if (_items.Count == 0)
        {
            await UiDispatch.RunAsync(() =>
            {
                if (epoch != _loadEpoch)
                {
                    return;
                }

                Current = null;
                CurrentPath = null;
                PreviewImageUri = null;
                Title = "";
                PositionLabel = "0 / 0";
                IsImage = false;
                IsVideo = false;
                IsMissing = false;
                IsCloudPreview = false;
                PreviewStatus = "";
                CanDownloadOriginal = false;
                CanOpenInExplorer = false;
                CanGoPrevious = false;
                CanGoNext = false;
                Tags.Clear();
            });
            return;
        }

        var item = _items[CurrentIndex];
        IReadOnlyList<AssignedTag> assigned = [];
        try
        {
            assigned = await _catalog.GetAssignedTagsAsync(item.Id);
        }
        catch
        {
            assigned = [];
        }

        var onDisk = CloudFile.TryGetAttributes(item.Path, out var diskAttrs);
        var onlineOnly = onDisk && CloudFile.IsOnlineOnly(diskAttrs);
        var apiOnly = AssetItemMapper.IsApiOnly(item);
        string? previewUrl = null;
        string? previewCache = null;
        if (apiOnly && !string.IsNullOrEmpty(item.CloudItemId))
        {
            (previewUrl, previewCache) = await LoadCloudPreviewAsync(item);
        }

        if (epoch != _loadEpoch)
        {
            return;
        }

        var playableVideo = GalleryMedia.IsPlayableLocalVideo(
            item.Kind, apiOnly, onlineOnly, onDisk, item.Path);
        var stillPath = GalleryMedia.OverlayStillPath(
            item.IsOrphan,
            onlineOnly,
            onDisk,
            item.Path,
            item.ThumbPath,
            previewCache);

        await UiDispatch.RunAsync(() =>
        {
            if (epoch != _loadEpoch)
            {
                return;
            }

            Current = item;
            CurrentPath = playableVideo ? item.Path : stillPath;
            PreviewImageUri = previewUrl ?? stillPath;
            Title = item.FileName;
            PositionLabel = $"{CurrentIndex + 1} / {_items.Count}";
            IsCloudPreview = apiOnly && (previewUrl is not null || previewCache is not null);
            PreviewStatus = IsCloudPreview
                ? "Online preview — the original stays in the cloud."
                : onlineOnly
                    ? "Online-only file. Opening downloads it."
                    : "";
            IsVideo = playableVideo;
            IsImage = !playableVideo && (!string.IsNullOrEmpty(PreviewImageUri) || !string.IsNullOrEmpty(stillPath));
            IsMissing = !IsVideo && CurrentPath is null && string.IsNullOrEmpty(PreviewImageUri);
            CanDownloadOriginal = apiOnly && !string.IsNullOrEmpty(item.CloudItemId);
            CanOpenInExplorer = onDisk && !apiOnly;
            CanGoPrevious = CurrentIndex > 0;
            CanGoNext = CurrentIndex < _items.Count - 1;

            Tags.Clear();
            foreach (var tag in assigned)
            {
                Tags.Add(new AssignedTagItem
                {
                    TagId = tag.TagId,
                    TagName = tag.TagName,
                    Display = tag.TagName,
                    Source = tag.Source,
                    SourceLabel = tag.Source == TagSource.Implied ? "Implied" : tag.Source.ToString(),
                    EffectiveColor = tag.EffectiveColor
                });
            }
        });

        if (!apiOnly && onDisk && onlineOnly)
        {
            _ = HydrateThenReloadAsync(item.Id, epoch);
        }
    }

    private async Task HydrateThenReloadAsync(string assetId, int epoch)
    {
        try
        {
            await AppServices.Hydration.HydrateAfterOpenAsync(assetId);
        }
        catch
        {
            return;
        }

        if (epoch != _loadEpoch)
        {
            return;
        }

        var current = _items.ElementAtOrDefault(CurrentIndex);
        if (current is null)
        {
            return;
        }

        var refreshed = await _catalog.GetAssetByIdAsync(assetId);
        if (refreshed is null || refreshed.IsOnlineOnly)
        {
            return;
        }

        current.IsOnlineOnly = refreshed.IsOnlineOnly;
        current.ContentHash = refreshed.ContentHash;
        current.ThumbPath = AppServices.Thumbnails.ExistingPathForHash(refreshed.ContentHash) ?? current.ThumbPath;
        current.Width = refreshed.Width ?? current.Width;
        current.Height = refreshed.Height ?? current.Height;
        await LoadCurrentAsync();
    }

    private async Task<(string? Url, string? CachedPath)> LoadCloudPreviewAsync(AssetItem item)
    {
        try
        {
            var source = await _catalog.GetSourceFolderAsync(item.SourceFolderId);
            if (source is null)
            {
                return default;
            }

            var library = await AppServices.CloudLibraries.ForSourceAsync(source);
            if (library is null || string.IsNullOrEmpty(item.CloudItemId))
            {
                return default;
            }

            var url = await library.GetLargePreviewUrlAsync(item.CloudItemId);
            var asset = await _catalog.GetAssetByIdAsync(item.Id);
            var hash = asset?.ContentHash;
            if (string.IsNullOrEmpty(hash))
            {
                return (url, null);
            }

            var existing = AppServices.Thumbnails.ExistingPathForHash(hash, ThumbnailService.LargePreviewSuffix)
                ?? AppServices.Thumbnails.ExistingPathForHash(hash);
            if (existing is not null)
            {
                return (url, existing);
            }

            await using var stream = await library.OpenLargePreviewAsync(item.CloudItemId);
            if (stream is null)
            {
                return (url, null);
            }

            var cached = await AppServices.Thumbnails.CacheJpegAsync(hash, stream, ThumbnailService.LargePreviewSuffix);
            return (url, cached?.Path);
        }
        catch
        {
            return default;
        }
    }

    private async Task<string?> ResolveOriginalUrlAsync(AssetItem item)
    {
        var source = await _catalog.GetSourceFolderAsync(item.SourceFolderId);
        if (source is null || string.IsNullOrEmpty(item.CloudItemId))
        {
            return null;
        }

        var library = await AppServices.CloudLibraries.ForSourceAsync(source);
        return library is null ? null : await library.GetOriginalDownloadUrlAsync(item.CloudItemId);
    }
}
