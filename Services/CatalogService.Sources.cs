using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>Catalog: watched source folders and cloud accounts.</summary>
public sealed partial class CatalogService
{
    public Task<IReadOnlyList<SourceFolder>> GetSourceFoldersAsync(string? projectId = null) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SelectSourceSql + """
                WHERE ($project IS NULL OR ProjectId = $project)
                ORDER BY Path
                """;
            cmd.Parameters.AddWithValue("$project", (object?)projectId ?? DBNull.Value);
            return (IReadOnlyList<SourceFolder>)ReadSources(cmd);
        });

    public Task<SourceFolder?> GetSourceFolderAsync(string id) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SelectSourceSql + " WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            var list = ReadSources(cmd);
            return list.Count > 0 ? list[0] : null;
        });

    public Task<SourceFolder?> FindSourceByPathAsync(string path) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SelectSourceSql;
            foreach (var source in ReadSources(cmd))
            {
                if (source.Kind != SourceKind.Local)
                {
                    continue;
                }

                if (PathSafe.IsUnderRoot(path, source.Path))
                {
                    return source;
                }
            }

            return (SourceFolder?)null;
        });

    public Task UpsertSourceFolderAsync(SourceFolder folder) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO SourceFolder (Id, Path, AccessToken, AutoOrganizeNewFiles, FolderTemplate, FileTemplate,
                                          DestinationPolicy, DestinationPath, ProjectId, Kind, CloudAccountId, CloudRootItemId)
                VALUES ($id, $path, $token, $auto, $folder, $file, $policy, $dest, $project, $kind, $cloud, $root)
                ON CONFLICT(Id) DO UPDATE SET
                    Path = excluded.Path,
                    AccessToken = excluded.AccessToken,
                    AutoOrganizeNewFiles = excluded.AutoOrganizeNewFiles,
                    FolderTemplate = excluded.FolderTemplate,
                    FileTemplate = excluded.FileTemplate,
                    DestinationPolicy = excluded.DestinationPolicy,
                    DestinationPath = excluded.DestinationPath,
                    ProjectId = excluded.ProjectId,
                    Kind = excluded.Kind,
                    CloudAccountId = excluded.CloudAccountId,
                    CloudRootItemId = excluded.CloudRootItemId;
                """;
            cmd.Parameters.AddWithValue("$id", folder.Id);
            cmd.Parameters.AddWithValue("$path", folder.Path);
            cmd.Parameters.AddWithValue("$token", (object?)folder.AccessToken ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$auto", folder.AutoOrganizeNewFiles ? 1 : 0);
            cmd.Parameters.AddWithValue("$folder", (object?)folder.FolderTemplate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$file", (object?)folder.FileTemplate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$policy", folder.DestinationPolicy.ToString());
            cmd.Parameters.AddWithValue("$dest", (object?)folder.DestinationPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$project", (object?)folder.ProjectId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$kind", folder.Kind.ToString());
            cmd.Parameters.AddWithValue("$cloud", (object?)folder.CloudAccountId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$root", (object?)folder.CloudRootItemId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        });

    public Task DeleteSourceFolderAsync(string id) =>
        _db.WriteAsync(conn =>
        {
            using var tx = conn.BeginTransaction();
            using (var rows = conn.CreateCommand())
            {
                rows.Transaction = tx;
                rows.CommandText = "SELECT RowId FROM Asset WHERE SourceFolderId = $id";
                rows.Parameters.AddWithValue("$id", id);
                using var reader = rows.ExecuteReader();
                var rowIds = new List<long>();
                while (reader.Read())
                {
                    rowIds.Add(reader.GetInt64(0));
                }

                reader.Close();
                foreach (var rowId in rowIds)
                {
                    DeleteFtsRow(conn, rowId, tx);
                }
            }

            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM SourceFolder WHERE Id = $id";
                del.Parameters.AddWithValue("$id", id);
                del.ExecuteNonQuery();
            }

            tx.Commit();
        });

    public Task<IReadOnlyList<CloudAccount>> GetCloudAccountsAsync() =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Provider, AccountId, DisplayName, VaultKey FROM CloudAccount ORDER BY Provider, DisplayName";
            return (IReadOnlyList<CloudAccount>)ReadCloudAccounts(cmd);
        });

    public Task<CloudAccount?> GetCloudAccountAsync(string id) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Provider, AccountId, DisplayName, VaultKey FROM CloudAccount WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            var list = ReadCloudAccounts(cmd);
            return list.Count > 0 ? list[0] : null;
        });

    public Task<CloudAccount?> GetCloudAccountByProviderAsync(CloudProvider provider) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Provider, AccountId, DisplayName, VaultKey FROM CloudAccount WHERE Provider = $p LIMIT 1";
            cmd.Parameters.AddWithValue("$p", provider.ToString());
            var list = ReadCloudAccounts(cmd);
            return list.Count > 0 ? list[0] : null;
        });

    public Task UpsertCloudAccountAsync(CloudAccount account) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO CloudAccount (Id, Provider, AccountId, DisplayName, VaultKey)
                VALUES ($id, $provider, $account, $name, $vault)
                ON CONFLICT(Id) DO UPDATE SET
                    Provider = excluded.Provider,
                    AccountId = excluded.AccountId,
                    DisplayName = excluded.DisplayName,
                    VaultKey = excluded.VaultKey;
                """;
            cmd.Parameters.AddWithValue("$id", account.Id);
            cmd.Parameters.AddWithValue("$provider", account.Provider.ToString());
            cmd.Parameters.AddWithValue("$account", account.AccountId);
            cmd.Parameters.AddWithValue("$name", (object?)account.DisplayName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$vault", account.VaultKey);
            cmd.ExecuteNonQuery();
        });

    public Task DeleteCloudAccountAsync(string id) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM CloudAccount WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        });

    private const string SelectSourceSql = """
        SELECT Id, Path, AccessToken, AutoOrganizeNewFiles, FolderTemplate, FileTemplate,
               DestinationPolicy, DestinationPath, ProjectId, Kind, CloudAccountId, CloudRootItemId
        FROM SourceFolder
        """ + "\n";

    private static List<SourceFolder> ReadSources(SqliteCommand cmd)
    {
        var list = new List<SourceFolder>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SourceFolder
            {
                Id = reader.GetString(0),
                Path = reader.GetString(1),
                AccessToken = reader.IsDBNull(2) ? null : reader.GetString(2),
                AutoOrganizeNewFiles = reader.GetInt32(3) != 0,
                FolderTemplate = reader.IsDBNull(4) ? null : reader.GetString(4),
                FileTemplate = reader.IsDBNull(5) ? null : reader.GetString(5),
                DestinationPolicy = Enum.TryParse<DestinationPolicy>(reader.GetString(6), out var p) ? p : DestinationPolicy.InSource,
                DestinationPath = reader.IsDBNull(7) ? null : reader.GetString(7),
                ProjectId = reader.IsDBNull(8) ? null : reader.GetString(8),
                Kind = reader.FieldCount > 9 && !reader.IsDBNull(9) && Enum.TryParse<SourceKind>(reader.GetString(9), out var kind)
                    ? kind
                    : SourceKind.Local,
                CloudAccountId = reader.FieldCount > 10 && !reader.IsDBNull(10) ? reader.GetString(10) : null,
                CloudRootItemId = reader.FieldCount > 11 && !reader.IsDBNull(11) ? reader.GetString(11) : null
            });
        }

        return list;
    }

    private static List<CloudAccount> ReadCloudAccounts(SqliteCommand cmd)
    {
        var list = new List<CloudAccount>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new CloudAccount
            {
                Id = reader.GetString(0),
                Provider = Enum.TryParse<CloudProvider>(reader.GetString(1), out var provider) ? provider : CloudProvider.OneDrive,
                AccountId = reader.GetString(2),
                DisplayName = reader.IsDBNull(3) ? null : reader.GetString(3),
                VaultKey = reader.GetString(4)
            });
        }

        return list;
    }
}
