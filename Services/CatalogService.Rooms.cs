using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>Catalog: rooms (collections) and their items.</summary>
public sealed partial class CatalogService
{
    public Task<IReadOnlyList<Room>> GetRoomsAsync(string? projectId = null) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Name, Kind, SortOrder, ProjectId, Icon
                FROM Collection
                WHERE Kind = 'Room' AND ($project IS NULL OR ProjectId = $project)
                ORDER BY SortOrder, Name
                """;
            cmd.Parameters.AddWithValue("$project", (object?)projectId ?? DBNull.Value);
            var list = new List<Room>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Room
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Kind = reader.GetString(2),
                    SortOrder = reader.GetInt32(3),
                    ProjectId = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Icon = RoomIcons.Normalize(reader.IsDBNull(5) ? null : reader.GetString(5))
                });
            }

            return (IReadOnlyList<Room>)list;
        });

    public Task<Room> CreateRoomAsync(string name, string projectId, string? icon = null) =>
        _db.WriteAsync(conn =>
        {
            var room = new Room
            {
                Id = PalaceDb.NewId(),
                Name = name.Trim(),
                Kind = "Room",
                Icon = RoomIcons.Normalize(icon),
                SortOrder = 0,
                ProjectId = projectId
            };
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Collection (Id, Name, Kind, SortOrder, ProjectId, Icon) VALUES ($id, $name, 'Room', $sort, $project, $icon)";
            cmd.Parameters.AddWithValue("$id", room.Id);
            cmd.Parameters.AddWithValue("$name", room.Name);
            cmd.Parameters.AddWithValue("$sort", room.SortOrder);
            cmd.Parameters.AddWithValue("$project", projectId);
            cmd.Parameters.AddWithValue("$icon", room.Icon);
            cmd.ExecuteNonQuery();
            return room;
        });

    public Task SetRoomIconAsync(string id, string? icon) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Collection SET Icon = $icon WHERE Id = $id";
            cmd.Parameters.AddWithValue("$icon", RoomIcons.Normalize(icon));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        });

    public Task RenameRoomAsync(string id, string name) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Collection SET Name = $name WHERE Id = $id";
            cmd.Parameters.AddWithValue("$name", name.Trim());
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        });

    public Task DeleteRoomAsync(string id) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Collection WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        });

    public Task AddToRoomAsync(string roomId, string assetId, string section, int sortOrder) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO CollectionItem (CollectionId, AssetId, Section, SortOrder)
                VALUES ($room, $asset, $section, $sort)
                ON CONFLICT(CollectionId, AssetId) DO UPDATE SET Section = excluded.Section
                """;
            cmd.Parameters.AddWithValue("$room", roomId);
            cmd.Parameters.AddWithValue("$asset", assetId);
            cmd.Parameters.AddWithValue("$section", section);
            cmd.Parameters.AddWithValue("$sort", sortOrder);
            cmd.ExecuteNonQuery();
        });

    public Task RemoveFromRoomAsync(string roomId, string assetId) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM CollectionItem WHERE CollectionId = $room AND AssetId = $asset";
            cmd.Parameters.AddWithValue("$room", roomId);
            cmd.Parameters.AddWithValue("$asset", assetId);
            cmd.ExecuteNonQuery();
        });

    public Task<IReadOnlyList<RoomItem>> GetRoomItemsAsync(string roomId) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT CollectionId, AssetId, Section, SortOrder
                FROM CollectionItem WHERE CollectionId = $id
                ORDER BY Section, SortOrder
                """;
            cmd.Parameters.AddWithValue("$id", roomId);
            var list = new List<RoomItem>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new RoomItem
                {
                    CollectionId = reader.GetString(0),
                    AssetId = reader.GetString(1),
                    Section = reader.IsDBNull(2) ? "Pins" : reader.GetString(2),
                    SortOrder = reader.GetInt32(3)
                });
            }

            return (IReadOnlyList<RoomItem>)list;
        });

    public Task SaveRoomOrderAsync(string roomId, IReadOnlyList<RoomItem> items) =>
        _db.WriteAsync(conn =>
        {
            foreach (var item in items)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    UPDATE CollectionItem SET Section = $section, SortOrder = $sort
                    WHERE CollectionId = $room AND AssetId = $asset
                    """;
                cmd.Parameters.AddWithValue("$section", item.Section ?? "Pins");
                cmd.Parameters.AddWithValue("$sort", item.SortOrder);
                cmd.Parameters.AddWithValue("$room", roomId);
                cmd.Parameters.AddWithValue("$asset", item.AssetId);
                cmd.ExecuteNonQuery();
            }
        });
}
