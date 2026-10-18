using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>
/// SQLite catalog access. Split by area into partial files: <c>.Sources</c> (watched folders, cloud
/// accounts), <c>.Assets</c>, <c>.Tags</c> (global tag graph), <c>.TagAssignments</c> (asset tags,
/// filters, implied backfill), <c>.Organize</c>, <c>.Rooms</c>, <c>.Search</c> (FTS). Projects live here.
/// </summary>
public sealed partial class CatalogService
{
    private readonly PalaceDb _db;

    public CatalogService(PalaceDb db)
    {
        _db = db;
    }

    public Task<IReadOnlyList<Project>> GetProjectsAsync() =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name FROM Project ORDER BY Name COLLATE NOCASE";
            var list = new List<Project>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Project { Id = reader.GetString(0), Name = reader.GetString(1) });
            }

            return (IReadOnlyList<Project>)list;
        });

    public Task<Project> CreateProjectAsync(string name) =>
        _db.WriteAsync(conn =>
        {
            var project = new Project { Id = PalaceDb.NewId(), Name = name.Trim() };
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Project (Id, Name) VALUES ($id, $name)";
            cmd.Parameters.AddWithValue("$id", project.Id);
            cmd.Parameters.AddWithValue("$name", project.Name);
            cmd.ExecuteNonQuery();
            return project;
        });

    public Task RenameProjectAsync(string id, string name) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Project SET Name = $name WHERE Id = $id";
            cmd.Parameters.AddWithValue("$name", name.Trim());
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        });
}
