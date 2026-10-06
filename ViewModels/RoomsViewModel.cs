using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Helpers;
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
    public IReadOnlyList<RoomIconChoice> IconChoices => RoomIcons.All;

    [ObservableProperty]
    public partial Room? SelectedRoom { get; set; }

    [ObservableProperty]
    public partial RoomIconChoice? SelectedIcon { get; set; } = RoomIcons.Find(null);

    [ObservableProperty]
    public partial string NewRoomName { get; set; } = "";

    [ObservableProperty]
    public partial string NewSectionName { get; set; } = "";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Create a room to pin references.";

    [ObservableProperty]
    public partial AssetItem? SelectedPin { get; set; }

    private void Notify(string message) => StatusText = message;

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
        SyncSelectedIcon(SelectedRoom);
        await LoadSelectedRoomAsync();
    }

    partial void OnSelectedRoomChanged(Room? value)
    {
        SyncSelectedIcon(value);
        _ = LoadSelectedRoomAsync();
    }

    partial void OnSelectedIconChanged(RoomIconChoice? value)
    {
        if (_suppressIconWrite || SelectedRoom is null || value is null)
        {
            return;
        }

        _ = PersistSelectedIconAsync(value.Id);
    }

    [RelayCommand]
    private Task CreateRoomAsync() =>
        ErrorReporter.RunAsync("Create room", Notify, async () =>
        {
            var name = string.IsNullOrWhiteSpace(NewRoomName) ? $"Room {Rooms.Count + 1}" : NewRoomName.Trim();
            var room = await _catalog.CreateRoomAsync(name, AppServices.CurrentProject.Id, SelectedIcon?.Id);
            NewRoomName = "";
            Rooms.Add(room);
            SelectedRoom = room;
            StatusText = $"Created {room.Name}.";
        });

    [RelayCommand]
    private Task RenameRoomAsync() =>
        ErrorReporter.RunAsync("Rename room", Notify, async () =>
        {
            if (SelectedRoom is null || string.IsNullOrWhiteSpace(NewRoomName))
            {
                return;
            }

            await _catalog.RenameRoomAsync(SelectedRoom.Id, NewRoomName.Trim());
            SelectedRoom.Name = NewRoomName.Trim();
            NewRoomName = "";
            await RefreshAsync();
        });

    [RelayCommand]
    private Task DeleteRoomAsync() =>
        ErrorReporter.RunAsync("Delete room", Notify, async () =>
        {
            if (SelectedRoom is null)
            {
                return;
            }

            await _catalog.DeleteRoomAsync(SelectedRoom.Id);
            Rooms.Remove(SelectedRoom);
            SelectedRoom = Rooms.FirstOrDefault();
            await LoadSelectedRoomAsync();
        });

    [RelayCommand]
    private Task AddSectionAsync() =>
        ErrorReporter.RunAsync("Add section", Notify, async () =>
        {
            if (string.IsNullOrWhiteSpace(NewSectionName) || Sections.Any(s => s.Title.Equals(NewSectionName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            Sections.Add(new RoomSection { Title = NewSectionName.Trim() });
            NewSectionName = "";
            await PersistOrderAsync();
        });

    [RelayCommand]
    private Task RemovePinAsync() =>
        ErrorReporter.RunAsync("Remove pin", Notify, async () =>
        {
            await RemovePinItemAsync(SelectedPin);
        });

    [RelayCommand]
    private Task RemovePinItemAsync(AssetItem? item) =>
        ErrorReporter.RunAsync("Remove pin item", Notify, async () =>
        {
            if (SelectedRoom is null || item is null)
            {
                return;
            }

            await _catalog.RemoveFromRoomAsync(SelectedRoom.Id, item.Id);
            await LoadSelectedRoomAsync();
        });

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
        var sources = await _catalog.GetSourceFoldersAsync();
        var sourceCloud = sources.ToDictionary(
            source => source.Id,
            source => GalleryMedia.SourceFolderIsCloud(source.Kind, source.Path),
            StringComparer.Ordinal);
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

            sourceCloud.TryGetValue(asset.SourceFolderId, out var cloud);
            section.Items.Add(AssetItemMapper.FromAsset(asset, _thumbs, cloud));
        }

        if (Sections.Count == 0)
        {
            Sections.Add(new RoomSection { Title = "Pins" });
        }

        StatusText = $"{SelectedRoom.Name}: {Sections.Sum(s => s.Items.Count)} pins.";
    }

    private bool _suppressIconWrite;

    private void SyncSelectedIcon(Room? room)
    {
        _suppressIconWrite = true;
        SelectedIcon = RoomIcons.Find(room?.Icon);
        _suppressIconWrite = false;
    }

    private async Task PersistSelectedIconAsync(string iconId)
    {
        if (SelectedRoom is null)
        {
            return;
        }

        var icon = RoomIcons.Normalize(iconId);
        await _catalog.SetRoomIconAsync(SelectedRoom.Id, icon);
        SelectedRoom.Icon = icon;
        await RefreshAsync();
    }
}
