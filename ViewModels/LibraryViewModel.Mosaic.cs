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

/// <summary>Library: mosaic population, folder headers, row height and sort.</summary>
public partial class LibraryViewModel
{
    private async Task PopulateMosaicAsync(IReadOnlyList<AssetItem> items, int epoch, string doneStatus)
    {
        await UiDispatch.RunAsync(() =>
        {
            if (epoch != _filterEpoch)
            {
                return;
            }

            ClearSelection();
            Assets.Clear();
            MosaicReset?.Invoke();
        });

        var total = items.Count;
        var assetTotal = 0;
        for (var n = 0; n < total; n++)
        {
            if (!items[n].IsFolderHeader)
            {
                assetTotal++;
            }
        }

        var shown = 0;
        for (var i = 0; i < total; i += MosaicChunkSize)
        {
            if (epoch != _filterEpoch)
            {
                return;
            }

            var end = Math.Min(i + MosaicChunkSize, total);
            await UiDispatch.RunAsync(() =>
            {
                if (epoch != _filterEpoch)
                {
                    return;
                }

                for (var n = i; n < end; n++)
                {
                    Assets.Add(items[n]);
                    if (!items[n].IsFolderHeader)
                    {
                        shown++;
                    }
                }

                StatusText = $"Showing {shown} of {assetTotal}";
                MosaicChunkAppended?.Invoke();
            });

            await UiDispatch.YieldAsync();
        }

        if (epoch != _filterEpoch)
        {
            return;
        }

        await UiDispatch.RunAsync(() =>
        {
            if (epoch != _filterEpoch)
            {
                return;
            }

            StatusText = doneStatus;
        });
    }

    [RelayCommand]
    private void ActivateFolderHeader(AssetItem? header) => SelectFolderGroup(header);

    public void SelectFolderGroup(AssetItem? header)
    {
        if (header is null)
        {
            return;
        }

        var path = header.FolderGroupPath;
        if (string.IsNullOrEmpty(path))
        {
            path = FolderGroups.FolderPathFromMosaicTag(header.MosaicTag);
        }

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var node = FindNode(FolderTree, path);
        if (node is not null)
        {
            SelectedFolder = node;
        }
    }

    private List<AssetItem> BuildMosaicItems(
        IReadOnlyList<Asset> assets,
        bool groupFolders,
        string folderName,
        string folderPath)
    {
        var mapped = assets.Select(ToItem).ToList();
        if (!groupFolders)
        {
            return mapped;
        }

        var groups = FolderGroups.GroupByTopFolders(mapped, folderName, folderPath, item => item.Path);
        if (!FolderGroups.NeedsHeaders(groups))
        {
            return mapped;
        }

        return FolderGroups.InterleaveHeaders(groups, group => ToFolderHeader(group, folderPath));
    }

    private static AssetItem ToFolderHeader(FolderGroup<AssetItem> group, string selectedPath) =>
        new()
        {
            IsFolderHeader = true,
            FolderGroupTitle = group.Title,
            FolderGroupPath = FolderGroups.CombineUnder(selectedPath, group.RelativeSegments),
            FileName = group.Title
        };

    private AssetItem ToItem(Asset asset)
    {
        _sourceIsCloud.TryGetValue(asset.SourceFolderId, out var sourceCloud);
        return AssetItemMapper.FromAsset(asset, _thumbs, sourceCloud);
    }

    private void StampSourceCloud(IReadOnlyList<SourceFolder> sources)
    {
        _sourceIsCloud = sources.ToDictionary(
            source => source.Id,
            source => GalleryMedia.SourceFolderIsCloud(source.Kind, source.Path),
            StringComparer.Ordinal);
    }

    private void LoadRowHeight()
    {
        var key = RowHeightKey(AppServices.CurrentProject.Id);
        MosaicRowHeight = ApplicationData.Current.LocalSettings.Values[key] switch
        {
            double d => Math.Clamp(d, 96, 280),
            float f => Math.Clamp(f, 96, 280),
            int i => Math.Clamp(i, 96, 280),
            _ => 140
        };
    }

    public Task ReloadMosaicSortAsync()
    {
        var previous = MosaicSort;
        LoadMosaicSort();
        return MosaicSort == previous ? Task.CompletedTask : ApplyFilterAsync();
    }

    private void LoadMosaicSort()
    {
        var key = MosaicSortOrder.SettingsKey(AppServices.CurrentProject.Id);
        var next = MosaicSortOrder.Parse(ApplicationData.Current.LocalSettings.Values[key]);
        _suppressFilter = true;
        MosaicSort = next;
        _suppressFilter = false;
    }

    private static string RowHeightKey(string projectId) => $"MosaicRowHeight_{projectId}";
}
