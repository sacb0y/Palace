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
    private async Task PopulateMosaicAsync(
        IReadOnlyList<AssetItem> items,
        int epoch,
        string doneStatus,
        bool clearSelection = true)
    {
        await UiDispatch.RunAsync(() =>
        {
            if (epoch != _filterEpoch)
            {
                return;
            }

            if (clearSelection)
            {
                ClearSelection();
            }

            Assets.Clear();
            MosaicReset?.Invoke();
        });

        var total = items.Count;
        // "of N" counts the full ordered mosaic (collapsed sections still count).
        var assetTotal = _mosaicOrder.Count > 0
            ? _mosaicOrder.Count(item => !item.IsFolderHeader)
            : items.Count(item => !item.IsFolderHeader);

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
            MosaicRestoreReady?.Invoke();
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

        SectionToggleStarting?.Invoke();
        var collapsed = FolderSectionPrefs.ToggleCollapsed(_collapsedSections, path);
        _collapsedSections = collapsed;
        header.IsFolderCollapsed = FolderSectionPrefs.IsCollapsed(collapsed, path);
        foreach (var item in _mosaicOrder)
        {
            if (item.IsFolderHeader
                && string.Equals(ResolveHeaderPath(item), path, StringComparison.OrdinalIgnoreCase))
            {
                item.IsFolderCollapsed = header.IsFolderCollapsed;
            }
        }

        PendingSectionFocusPath = path;
        SaveCollapsedSections(_sectionBrowsePath, collapsed);
        _ = RepublishSectionDisplayAsync();
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

    /// <summary>
    /// Select every non-header asset in the full ordered mosaic (includes collapsed
    /// sections). Library grid Ctrl+A; turns Select mode on when needed (same as
    /// hold-to-select: select first, then enter mode so exit/Esc keeps selection).
    /// </summary>
    public void SelectAllMosaicAssets()
    {
        IReadOnlyList<AssetItem> source = _mosaicOrder.Count > 0 ? _mosaicOrder : Assets;
        SetSelection(MosaicDelete.OrderedNonHeaderTargets(source, a => a.IsFolderHeader));
        if (!IsSelectMode)
        {
            IsSelectMode = true;
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
        List<AssetItem> Ordered,
        List<AssetItem> Display,
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
            return new MosaicBuild(mapped, mapped, "", false, false, []);
        }

        var groups = FolderGroups.GroupByTopFolders(mapped, folderName, folderPath, item => item.Path);
        if (!FolderGroups.NeedsHeaders(groups))
        {
            return new MosaicBuild(mapped, mapped, folderPath, false, flatten, collapsed);
        }

        if (flatten)
        {
            return new MosaicBuild(mapped, mapped, folderPath, true, true, collapsed);
        }

        var livePaths = groups
            .Select(group => FolderGroups.CombineUnder(folderPath, group.RelativeSegments))
            .ToList();
        collapsed = FolderSectionPrefs.PruneCollapsed(collapsed, livePaths);

        // Full ordered list always includes every section's tiles (collapse is view-only).
        var ordered = FolderGroups.InterleaveHeaders(
            groups,
            group => ToFolderHeader(group, folderPath, collapsed));
        var display = FolderSectionPrefs.FilterCollapsedDisplay(
            ordered,
            item => item.IsFolderHeader,
            item => item.IsFolderCollapsed);
        return new MosaicBuild(ordered, display, folderPath, true, false, collapsed);
    }

    private async Task RepublishSectionDisplayAsync()
    {
        if (_mosaicOrder.Count == 0)
        {
            await ApplyFilterAsync();
            return;
        }

        var display = FolderSectionPrefs.FilterCollapsedDisplay(
            _mosaicOrder,
            item => item.IsFolderHeader,
            item => item.IsFolderCollapsed);
        var epoch = ++_filterEpoch;
        var assetCount = _mosaicOrder.Count(item => !item.IsFolderHeader);
        var doneStatus = SelectedFolder is not null
            ? $"{assetCount} in {SelectedFolder.Name}"
            : $"{assetCount} assets";
        // Keep selection (collapsed tiles stay selected in the ordered list).
        await PopulateMosaicAsync(display, epoch, doneStatus, clearSelection: false);
        await UiDispatch.RunAsync(() =>
        {
            // Re-stamp IsSelected on the new display surface.
            var selected = new HashSet<AssetItem>(_selection);
            foreach (var asset in Assets)
            {
                asset.IsSelected = selected.Contains(asset);
            }
        });
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
        FolderSectionPrefs.EnsureLegacyKeysPruned(
            settings.Keys.OfType<string>(),
            key => settings.TryGetValue(key, out var value) ? value : null,
            key => settings.Remove(key),
            (key, value) => settings[key] = value);

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
        // Every Assets repopulate path resets _mosaicOrder here (or clears it below).
        // Assets must remain a subset of _mosaicOrder (shared AssetItem references).
        _mosaicOrder = build.Ordered;
        Debug.Assert(
            build.Display.All(item => build.Ordered.Contains(item)),
            "Display mosaic must be a subset of the ordered mosaic.");
        var was = _suppressFilter;
        _suppressFilter = true;
        ShowFolderSectionsChrome = build.ShowSections;
        FlattenSubfolders = build.Flatten;
        _suppressFilter = was;
    }

    private void SaveFlatten(string folderPath, bool flatten) =>
        FolderSectionPrefs.TrySave(() =>
            ApplicationData.Current.LocalSettings.Values[FolderSectionPrefs.FlattenKey(folderPath)] = flatten);

    private static void SaveCollapsedSections(string folderPath, IReadOnlySet<string> collapsed) =>
        FolderSectionPrefs.TrySave(() =>
            ApplicationData.Current.LocalSettings.Values[FolderSectionPrefs.CollapsedKey(folderPath)] =
                FolderSectionPrefs.SerializeCollapsed(collapsed));

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
