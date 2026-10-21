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

    [RelayCommand]
    private void ToggleFolderSection(AssetItem? header)
    {
        if (header is not { IsFolderHeader: true })
        {
            return;
        }

        var path = ResolveHeaderPath(header);
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(_sectionBrowsePath))
        {
            return;
        }

        var collapsed = FolderSectionPrefs.ToggleCollapsed(_collapsedSections, path);
        _collapsedSections = collapsed;
        SaveCollapsedSections(_sectionBrowsePath, collapsed);
        _ = ApplyFilterAsync();
    }

    [RelayCommand]
    private void ToggleFlattenSubfolders()
    {
        if (string.IsNullOrEmpty(_sectionBrowsePath) || !ShowFolderSectionsChrome)
        {
            return;
        }

        FlattenSubfolders = !FlattenSubfolders;
    }

    public void SelectFolderGroup(AssetItem? header)
    {
        if (header is null)
        {
            return;
        }

        var path = ResolveHeaderPath(header);
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

    private static string? ResolveHeaderPath(AssetItem header)
    {
        var path = header.FolderGroupPath;
        if (string.IsNullOrEmpty(path))
        {
            path = FolderGroups.FolderPathFromMosaicTag(header.MosaicTag);
        }

        return string.IsNullOrEmpty(path) ? null : path;
    }

    private readonly record struct MosaicBuild(
        List<AssetItem> Items,
        string BrowsePath,
        bool ShowSections,
        bool Flatten,
        HashSet<string> Collapsed);

    private MosaicBuild BuildMosaicItems(
        IReadOnlyList<Asset> assets,
        bool groupFolders,
        string folderName,
        string folderPath,
        bool flatten,
        HashSet<string> collapsed)
    {
        var mapped = assets.Select(ToItem).ToList();
        if (!groupFolders)
        {
            return new MosaicBuild(mapped, "", false, false, []);
        }

        var groups = FolderGroups.GroupByTopFolders(mapped, folderName, folderPath, item => item.Path);
        if (!FolderGroups.NeedsHeaders(groups))
        {
            return new MosaicBuild(mapped, folderPath, false, flatten, collapsed);
        }

        if (flatten)
        {
            return new MosaicBuild(mapped, folderPath, true, true, collapsed);
        }

        var items = FolderGroups.InterleaveHeaders(
            groups,
            group => ToFolderHeader(group, folderPath, collapsed),
            group =>
            {
                var path = FolderGroups.CombineUnder(folderPath, group.RelativeSegments);
                return !FolderSectionPrefs.IsCollapsed(collapsed, path);
            });
        return new MosaicBuild(items, folderPath, true, false, collapsed);
    }

    private static AssetItem ToFolderHeader(
        FolderGroup<AssetItem> group,
        string selectedPath,
        IReadOnlySet<string> collapsed)
    {
        var path = FolderGroups.CombineUnder(selectedPath, group.RelativeSegments);
        return new AssetItem
        {
            IsFolderHeader = true,
            FolderGroupTitle = group.Title,
            FolderGroupPath = path,
            FolderItemCount = group.Items.Count,
            IsFolderCollapsed = FolderSectionPrefs.IsCollapsed(collapsed, path),
            FileName = group.Title
        };
    }

    private static (bool Flatten, HashSet<string> Collapsed) ReadSectionPrefs(string folderPath)
    {
        var settings = ApplicationData.Current.LocalSettings.Values;
        var flatten = FolderSectionPrefs.ParseBool(
            settings.TryGetValue(FolderSectionPrefs.FlattenKey(folderPath), out var f) ? f : null);
        var collapsed = FolderSectionPrefs.ParseCollapsed(
            settings.TryGetValue(FolderSectionPrefs.CollapsedKey(folderPath), out var c) ? c : null);
        return (flatten, collapsed);
    }

    private void ApplyMosaicBuildChrome(MosaicBuild build)
    {
        _sectionBrowsePath = build.BrowsePath;
        _collapsedSections = build.Collapsed;
        var was = _suppressFilter;
        _suppressFilter = true;
        ShowFolderSectionsChrome = build.ShowSections;
        FlattenSubfolders = build.Flatten;
        _suppressFilter = was;
    }

    private void SaveFlatten(string folderPath, bool flatten) =>
        ApplicationData.Current.LocalSettings.Values[FolderSectionPrefs.FlattenKey(folderPath)] = flatten;

    private static void SaveCollapsedSections(string folderPath, IReadOnlySet<string> collapsed) =>
        ApplicationData.Current.LocalSettings.Values[FolderSectionPrefs.CollapsedKey(folderPath)] =
            FolderSectionPrefs.SerializeCollapsed(collapsed);

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
        _localSourceIds = sources.Where(source => source.Kind == SourceKind.Local)
            .Select(source => source.Id)
            .ToHashSet(StringComparer.Ordinal);
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
        if (AppServices.CurrentProject is not { } project)
        {
            return;
        }

        var settings = ApplicationData.Current.LocalSettings.Values;
        var next = MosaicSortOrder.Load(key => settings.TryGetValue(key, out var v) ? v : null, project.Id, MosaicSortViewNow);
        var wasSuppressed = _suppressFilter;
        _suppressFilter = true;
        MosaicSort = next;
        _suppressFilter = wasSuppressed;
    }

    /// <summary>Folders and Tag browse remember their own sort.</summary>
    private MosaicSortView MosaicSortViewNow => IsTagBrowse ? MosaicSortView.TagBrowse : MosaicSortView.Folders;

    private static string RowHeightKey(string projectId) => $"MosaicRowHeight_{projectId}";
}
