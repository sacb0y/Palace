using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Models;
using Palace.Services;

namespace Palace.ViewModels;

public partial class RoomsViewModel : ObservableObject
{
    private readonly CatalogService _catalog;
    private readonly ThumbnailService _thumbs;

    public RoomsViewModel(CatalogService catalog, ThumbnailService thumbs)
    {
        _catalog = catalog;
        _thumbs = thumbs;
    }

    public ObservableCollection<Room> Rooms { get; } = [];
    public ObservableCollection<RoomSection> Sections { get; } = [];

    [ObservableProperty]
    public partial Room? SelectedRoom { get; set; }

    [ObservableProperty]
    public partial string NewRoomName { get; set; } = "";

    [ObservableProperty]
    public partial string NewSectionName { get; set; } = "";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Create a room to pin references.";

    [ObservableProperty]
    public partial AssetItem? SelectedPin { get; set; }

    public async Task LoadAsync() => await RefreshAsync();

    public async Task RefreshAsync()
    {
        var selectedId = SelectedRoom?.Id;
        Rooms.Clear();
        foreach (var room in await _catalog.GetRoomsAsync(AppServices.CurrentProject.Id))
        {
            Rooms.Add(room);
        }

        SelectedRoom = Rooms.FirstOrDefault(r => r.Id == selectedId) ?? Rooms.FirstOrDefault();
        await LoadSelectedRoomAsync();
    }

    partial void OnSelectedRoomChanged(Room? value) => _ = LoadSelectedRoomAsync();

    [RelayCommand]
    private async Task CreateRoomAsync()
    {
        var name = string.IsNullOrWhiteSpace(NewRoomName) ? $"Room {Rooms.Count + 1}" : NewRoomName.Trim();
        var room = await _catalog.CreateRoomAsync(name, AppServices.CurrentProject.Id);
        NewRoomName = "";
        Rooms.Add(room);
        SelectedRoom = room;
        StatusText = $"Created {room.Name}.";
    }

    [RelayCommand]
    private async Task RenameRoomAsync()
    {
        if (SelectedRoom is null || string.IsNullOrWhiteSpace(NewRoomName))
        {
            return;
        }

        await _catalog.RenameRoomAsync(SelectedRoom.Id, NewRoomName.Trim());
        SelectedRoom.Name = NewRoomName.Trim();
        NewRoomName = "";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteRoomAsync()
    {
        if (SelectedRoom is null)
        {
            return;
        }

        await _catalog.DeleteRoomAsync(SelectedRoom.Id);
        Rooms.Remove(SelectedRoom);
        SelectedRoom = Rooms.FirstOrDefault();
        await LoadSelectedRoomAsync();
    }

    [RelayCommand]
    private async Task AddSectionAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSectionName) || Sections.Any(s => s.Title.Equals(NewSectionName.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Sections.Add(new RoomSection { Title = NewSectionName.Trim() });
        NewSectionName = "";
        await PersistOrderAsync();
    }

    [RelayCommand]
    private async Task RemovePinAsync()
    {
        await RemovePinItemAsync(SelectedPin);
    }

    [RelayCommand]
    private async Task RemovePinItemAsync(AssetItem? item)
    {
        if (SelectedRoom is null || item is null)
        {
            return;
        }

        await _catalog.RemoveFromRoomAsync(SelectedRoom.Id, item.Id);
        await LoadSelectedRoomAsync();
    }

    public async Task PersistOrderAsync()
    {
        if (SelectedRoom is null)
        {
            return;
        }

        var items = new List<RoomItem>();
        foreach (var section in Sections)
        {
            var order = 0;
            foreach (var item in section.Items)
            {
                items.Add(new RoomItem
                {
                    CollectionId = SelectedRoom.Id,
                    AssetId = item.Id,
                    Section = section.Title,
                    SortOrder = order++
                });
            }
        }

        await _catalog.SaveRoomOrderAsync(SelectedRoom.Id, items);
    }

    private async Task LoadSelectedRoomAsync()
    {
        Sections.Clear();
        if (SelectedRoom is null)
        {
            StatusText = "Create a room to pin references.";
            return;
        }

        var grouped = new Dictionary<string, RoomSection>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in await _catalog.GetRoomItemsAsync(SelectedRoom.Id))
        {
            var title = string.IsNullOrWhiteSpace(item.Section) ? "Pins" : item.Section;
            if (!grouped.TryGetValue(title, out var section))
            {
                section = new RoomSection { Title = title };
                grouped[title] = section;
                Sections.Add(section);
            }

            var asset = await _catalog.GetAssetByIdAsync(item.AssetId);
            if (asset is null)
            {
                continue;
            }

            var thumb = asset.ContentHash is null ? null : _thumbs.PathForHash(asset.ContentHash);
            section.Items.Add(new AssetItem
            {
                Id = asset.Id,
                SourceFolderId = asset.SourceFolderId,
                FileName = asset.FileName,
                Path = asset.Path,
                ThumbPath = thumb is not null && File.Exists(thumb) ? thumb : null,
                Kind = asset.Kind,
                IsOrphan = asset.IsOrphan,
                Model = asset.Model,
                Prompt = asset.Prompt
            });
        }

        if (Sections.Count == 0)
        {
            Sections.Add(new RoomSection { Title = "Pins" });
        }

        StatusText = $"{SelectedRoom.Name}: {Sections.Sum(s => s.Items.Count)} pins.";
    }
}
