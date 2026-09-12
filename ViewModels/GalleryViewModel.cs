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

    [ObservableProperty]
    public partial ImageScaling Scaling { get; set; } = ImageScaling.Fit;

    [ObservableProperty]
    public partial string DetailsText { get; set; } = "";

    [ObservableProperty]
    public partial string HdrStatus { get; set; } = "";

    [ObservableProperty]
    public partial bool CanScale { get; set; }

    [ObservableProperty]
    public partial bool IsScaleFit { get; set; } = true;

    [ObservableProperty]
    public partial bool IsScaleActual { get; set; }

    [ObservableProperty]
    public partial bool IsScaleFill { get; set; }

    [ObservableProperty]
    public partial HdrProbe CurrentProbe { get; set; } = HdrProbe.None;

    /// <summary>
    /// Bumped once after Current / probe / path / preview / IsImage are all
    /// written so the still surface starts one refresh, not one per field.
    /// </summary>
    [ObservableProperty]
    public partial int StillRevision { get; set; }

    public bool HdrPresented { get; private set; }

    public bool DisplayIsHdr { get; private set; }

    public float DisplayPeakNits { get; private set; }

    public float ContentMaxNits { get; private set; }

    public float ContentAvgNits { get; private set; }

    public float ContentMinNits { get; private set; }

    public float ContentMaxScrgb { get; private set; }

    private int _headerPixelWidth;
    private int _headerPixelHeight;
    private int _nativePixelWidth;
    private int _nativePixelHeight;
    private bool _nativeHdrStats;

    [ObservableProperty]
    public partial string? PreviewModel { get; set; }

    [ObservableProperty]
    public partial string? PreviewPrompt { get; set; }

    [ObservableProperty]
    public partial string? PreviewNegative { get; set; }

    [ObservableProperty]
    public partial string? PreviewSeed { get; set; }

    [ObservableProperty]
    public partial bool ShowGeneration { get; set; }

    [ObservableProperty]
    public partial bool ShowImageInfo { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowDetails { get; set; } = true;

    [ObservableProperty]
    public partial string ImageInfoText { get; set; } = "";

    partial void OnCurrentIndexChanged(int value) => _ = LoadCurrentAsync();

    partial void OnScalingChanged(ImageScaling value)
    {
        IsScaleFit = value == ImageScaling.Fit;
        IsScaleActual = value == ImageScaling.Actual;
        IsScaleFill = value == ImageScaling.Fill;
    }

    [RelayCommand]
    private void SetScaleActual() => Scaling = ImageScaling.Actual;

    [RelayCommand]
    private void SetScaleFit() => Scaling = ImageScaling.Fit;

    [RelayCommand]
    private void SetScaleFill() => Scaling = ImageScaling.Fill;

    public bool TryHandleScaleShortcut(bool controlDown, int keyCode)
    {
        if (!controlDown || !CanScale)
        {
            return false;
        }

        var next = GalleryScale.FromKeyCode(keyCode);
        if (next is null)
        {
            return false;
        }

        Scaling = next.Value;
        return true;
    }

    public bool TryHandleViewerShortcut(bool controlDown, int keyCode)
    {
        if (TryHandleScaleShortcut(controlDown, keyCode))
        {
            return true;
        }

        if (GalleryScale.TogglesImageInfo(controlDown, keyCode))
        {
            ShowImageInfo = !ShowImageInfo;
            return true;
        }

        if (GalleryScale.TogglesDetails(controlDown, keyCode))
        {
            ShowDetails = !ShowDetails;
            return true;
        }

        return false;
    }

    [RelayCommand]
    private void ToggleImageInfo() => ShowImageInfo = !ShowImageInfo;

    [RelayCommand]
    private void ToggleDetails() => ShowDetails = !ShowDetails;

    public void SetHdrPresentResult(
        bool presented,
        bool displayHdr,
        float displayPeakNits = 0,
        float maxNits = 0,
        float avgNits = 0,
        float minNits = 0,
        float maxScrgb = 0,
        int pixelWidth = 0,
        int pixelHeight = 0,
        bool statsAreNative = false)
    {
        HdrPresented = presented;
        DisplayIsHdr = displayHdr;
        DisplayPeakNits = displayPeakNits;
        if (GalleryPresent.ReplaceHdrStats(_nativeHdrStats, statsAreNative))
        {
            ContentMaxNits = maxNits;
            ContentAvgNits = avgNits;
            ContentMinNits = minNits;
            if (maxScrgb > 0 || statsAreNative)
            {
                ContentMaxScrgb = maxScrgb;
            }

            _nativeHdrStats = statsAreNative && (maxNits > 0 || maxScrgb > 0);
        }

        if (pixelWidth > 0 && pixelHeight > 0)
        {
            _nativePixelWidth = pixelWidth;
            _nativePixelHeight = pixelHeight;
        }

        RefreshHdrStatus();
    }

    public void SetHdrStats(float maxNits, float avgNits, float minNits, float maxScrgb)
    {
        ContentMaxNits = maxNits;
        ContentAvgNits = avgNits;
        ContentMinNits = minNits;
        ContentMaxScrgb = maxScrgb;
        _nativeHdrStats = maxNits > 0 || maxScrgb > 0;
        RefreshImageInfo();
    }

    private void RefreshHdrStatus()
    {
        HdrStatus = GalleryPresent.StatusLine(
            CurrentProbe,
            HdrPresented,
            DisplayIsHdr,
            GalleryPeak.Enabled,
            GalleryPeak.Nits) ?? "";
        RefreshImageInfo();
    }

    private void RefreshImageInfo()
    {
        var item = Current;
        var pixels = GalleryPresent.FilePixelSize(
            _nativePixelWidth,
            _nativePixelHeight,
            _headerPixelWidth,
            _headerPixelHeight,
            item?.Width,
            item?.Height);
        ImageInfoText = GalleryPresent.ImageInfoText(new GalleryImageInfo(
            item?.FileName,
            item?.FileSize,
            pixels?.Width,
            pixels?.Height,
            CurrentProbe,
            HdrStatus,
            HdrPresented && ContentMaxNits > 0 ? ContentMaxNits : null,
            HdrPresented && ContentMaxNits > 0 ? ContentAvgNits : null,
            HdrPresented && ContentMaxNits > 0 ? ContentMinNits : null,
            HdrPresented && DisplayPeakNits > 0 ? DisplayPeakNits : null,
            HdrPresented && ContentMaxScrgb > 0 ? ContentMaxScrgb : null));
    }

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
        await UiDispatch.RunAsync(() =>
        {
            if (epoch == _loadEpoch)
            {
                ApplyGenerationPreview(GenerationFields.ForPreview(null, null, null, null));
            }
        });
        if (epoch != _loadEpoch)
        {
            return;
        }

        if (_items.Count == 0)
        {
            await UiDispatch.RunAsync(() =>
            {
                if (epoch != _loadEpoch)
                {
                    return;
                }

                Current = null;
                CurrentProbe = HdrProbe.None;
                _headerPixelWidth = 0;
                _headerPixelHeight = 0;
                _nativePixelWidth = 0;
                _nativePixelHeight = 0;
                _nativeHdrStats = false;
                ContentMaxScrgb = 0;
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
                CanScale = false;
                DetailsText = "";
                HdrStatus = "";
                HdrPresented = false;
                RefreshImageInfo();
                ApplyGenerationPreview(GenerationFields.ForPreview(null, null, null, null));
                Tags.Clear();
                StillRevision++;
            });
            return;
        }

        var item = _items[CurrentIndex];
        IReadOnlyList<AssignedTag> assigned = [];
        GenerationFields.Snapshot generation = GenerationFields.ForPreview(null, null, null, null);
        try
        {
            assigned = await _catalog.GetAssignedTagsAsync(item.Id);
        }
        catch
        {
            assigned = [];
        }

        try
        {
            var asset = await _catalog.GetAssetByIdAsync(item.Id);
            generation = GenerationFields.ForPreview(
                asset?.Model, asset?.Prompt, asset?.NegativePrompt, asset?.Seed);
        }
        catch
        {
            generation = GenerationFields.ForPreview(null, null, null, null);
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

        var probe = HdrProbe.None;
        (int Width, int Height)? headerPixels = null;
        if (item.Kind == AssetKind.Image && onDisk && !onlineOnly && !apiOnly && !item.IsOrphan)
        {
            probe = HdrFile.ProbePath(item.Path);
            headerPixels = ImageDimensions.TryRead(item.Path);
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
            CurrentProbe = probe;
            _headerPixelWidth = headerPixels?.Width ?? 0;
            _headerPixelHeight = headerPixels?.Height ?? 0;
            _nativePixelWidth = 0;
            _nativePixelHeight = 0;
            _nativeHdrStats = false;
            ContentMaxNits = 0;
            ContentAvgNits = 0;
            ContentMinNits = 0;
            ContentMaxScrgb = 0;
            DisplayPeakNits = 0;
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
            CanScale = !playableVideo && (!string.IsNullOrEmpty(PreviewImageUri) || !string.IsNullOrEmpty(stillPath));
            HdrPresented = false;
            var detailsPixels = GalleryPresent.FilePixelSize(
                null, null, _headerPixelWidth, _headerPixelHeight, item.Width, item.Height);
            DetailsText = GalleryPresent.DetailsLine(
                item.FileName, detailsPixels?.Width, detailsPixels?.Height, item.Kind, item.FileSize);
            HdrStatus = GalleryPresent.StatusLine(probe, false, false, GalleryPeak.Enabled, GalleryPeak.Nits) ?? "";
            RefreshImageInfo();
            ApplyGenerationPreview(generation);

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

            StillRevision++;
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

    private void ApplyGenerationPreview(GenerationFields.Snapshot generation)
    {
        PreviewModel = generation.Model;
        PreviewPrompt = generation.Prompt;
        PreviewNegative = generation.Negative;
        PreviewSeed = generation.Seed;
        ShowGeneration = generation.HasAny;
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
