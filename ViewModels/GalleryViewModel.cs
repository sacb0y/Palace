using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;

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
                Title = "";
                PositionLabel = "0 / 0";
                IsImage = false;
                IsVideo = false;
                IsMissing = false;
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

        if (epoch != _loadEpoch)
        {
            return;
        }

        await UiDispatch.RunAsync(() =>
        {
            if (epoch != _loadEpoch)
            {
                return;
            }

            Current = item;
            CurrentPath = item.IsOrphan || string.IsNullOrEmpty(item.Path) || !File.Exists(item.Path)
                ? null
                : item.Path;
            Title = item.FileName;
            PositionLabel = $"{CurrentIndex + 1} / {_items.Count}";
            IsVideo = item.Kind == AssetKind.Video && CurrentPath is not null;
            IsImage = item.Kind is AssetKind.Image or AssetKind.Gif && CurrentPath is not null;
            IsMissing = CurrentPath is null;
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
    }
}
