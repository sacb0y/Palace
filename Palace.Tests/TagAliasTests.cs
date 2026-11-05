using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// T4 / Hydrus siblings: TagAlias table, rewrite-on-assign, search expands alias→ideal.
/// Distinct from TagMembership (groups) and TagSiblings (group children).
/// </summary>
public sealed class TagAliasTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-tag-alias-" + Guid.NewGuid().ToString("N"));
    private PalaceDb _db = null!;
    private CatalogService _catalog = null!;
    private SourceFolder _source = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _db = new PalaceDb(Path.Combine(_dir, "catalog", "palace.db"));
        _catalog = new CatalogService(_db);
        var projectId = (await _catalog.GetProjectsAsync())[0].Id;
        _source = new SourceFolder
        {
            Id = PalaceDb.NewId(),
            Path = Path.Combine(_dir, "watched"),
            ProjectId = projectId
        };
        Directory.CreateDirectory(_source.Path);
        await _catalog.UpsertSourceFolderAsync(_source);
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Db_CreatesTagAliasTable_AndAddAliasPersists()
    {
        var doggy = await _catalog.CreateTagAsync("doggy");
        var dog = await _catalog.CreateTagAsync("dog");

        Assert.True(await _catalog.AddAliasAsync(doggy.Id, dog.Id));
        Assert.True(await _catalog.AddAliasAsync(doggy.Id, dog.Id), "Same edge is idempotent.");
        var pup = await _catalog.CreateTagAsync("pup");
        Assert.False(await _catalog.AddAliasAsync(doggy.Id, pup.Id), "n→1: alias already bound elsewhere.");

        var aliases = await _catalog.GetAliasesAsync();
        Assert.Contains(aliases, a => a.AliasTagId == doggy.Id && a.IdealTagId == dog.Id);

        Assert.Equal(dog.Id, await _catalog.ResolveIdealTagIdAsync(doggy.Id));
        Assert.Equal(dog.Id, await _catalog.ResolveIdealTagIdAsync(dog.Id));
    }

    [Fact]
    public async Task AddAlias_RejectsSelf_AndCycle()
    {
        var a = await _catalog.CreateTagAsync("alpha");
        var b = await _catalog.CreateTagAsync("beta");

        Assert.False(await _catalog.AddAliasAsync(a.Id, a.Id));
        Assert.True(await _catalog.AddAliasAsync(b.Id, a.Id));
        Assert.False(await _catalog.AddAliasAsync(a.Id, b.Id));
    }

    [Fact]
    public async Task AddAlias_ResolvesTransitively_InCatalog()
    {
        var a = await _catalog.CreateTagAsync("a-name");
        var b = await _catalog.CreateTagAsync("b-name");
        var c = await _catalog.CreateTagAsync("c-name");

        Assert.True(await _catalog.AddAliasAsync(a.Id, b.Id));
        Assert.True(await _catalog.AddAliasAsync(b.Id, c.Id));
        Assert.Equal(c.Id, await _catalog.ResolveIdealTagIdAsync(a.Id));
    }

    [Fact]
    public async Task Assign_RewritesAliasToIdeal_ThenAppliesImplications()
    {
        var doggy = await _catalog.CreateTagAsync("doggy");
        var dog = await _catalog.CreateTagAsync("dog");
        var animal = await _catalog.CreateTagAsync("animal");
        Assert.True(await _catalog.AddAliasAsync(doggy.Id, dog.Id));
        Assert.True(await _catalog.AddImplicationAsync(dog.Id, animal.Id));

        var asset = await AddAssetAsync("pet.png");
        await _catalog.AssignTagAsync(asset.Id, doggy.Id, TagSource.Manual);

        var assigned = await SourcesByTagAsync(asset.Id);
        Assert.False(assigned.ContainsKey(doggy.Id), "Alias must not be stored on assign (v1 rewrite).");
        Assert.Equal(TagSource.Manual, assigned[dog.Id]);
        Assert.Equal(TagSource.Implied, assigned[animal.Id]);
    }

    [Fact]
    public async Task GetOrCreate_ResolvesAliasNameToIdeal()
    {
        var doggy = await _catalog.CreateTagAsync("doggy");
        var dog = await _catalog.CreateTagAsync("dog");
        Assert.True(await _catalog.AddAliasAsync(doggy.Id, dog.Id));

        var resolved = await _catalog.GetOrCreateTagAsync("doggy");
        Assert.Equal(dog.Id, resolved.Id);
        Assert.Equal("dog", resolved.Name);
    }

    [Fact]
    public async Task Search_AliasQuery_FindsIdealTaggedAsset()
    {
        var doggy = await _catalog.CreateTagAsync("doggy");
        var dog = await _catalog.CreateTagAsync("dog");
        Assert.True(await _catalog.AddAliasAsync(doggy.Id, dog.Id));

        var asset = await AddAssetAsync("hound.png");
        await _catalog.AssignTagAsync(asset.Id, dog.Id, TagSource.Manual);

        Assert.False(await FtsHasAsync(asset.Id, "doggy"), "FTS stores ideal name only after rewrite.");
        Assert.True(await FtsHasAsync(asset.Id, "dog"));

        var hits = await _catalog.SearchAsync("doggy");
        Assert.Contains(hits, a => a.Id == asset.Id);
    }

    private async Task<Asset> AddAssetAsync(string fileName)
    {
        var path = Path.Combine(_source.Path, fileName);
        await File.WriteAllTextAsync(path, "pixels");
        var asset = new Asset
        {
            Id = PalaceDb.NewId(),
            SourceFolderId = _source.Id,
            Path = path,
            FileName = fileName,
            Kind = AssetKind.Image,
            DateAdded = PalaceDb.NowIso()
        };
        await _catalog.UpsertAssetAsync(asset, "");
        return (await _catalog.GetAssetByIdAsync(asset.Id))!;
    }

    private Task<Dictionary<string, TagSource>> SourcesByTagAsync(string assetId) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TagId, Source FROM AssetTag WHERE AssetId = $id";
            cmd.Parameters.AddWithValue("$id", assetId);
            var map = new Dictionary<string, TagSource>(StringComparer.Ordinal);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                map[reader.GetString(0)] = Enum.Parse<TagSource>(reader.GetString(1));
            }

            return map;
        });

    private Task<bool> FtsHasAsync(string assetId, string tagName) =>
        _db.ReadAsync(conn =>
        {
            using var row = conn.CreateCommand();
            row.CommandText = "SELECT RowId FROM Asset WHERE Id = $id";
            row.Parameters.AddWithValue("$id", assetId);
            var rowId = Convert.ToInt64(row.ExecuteScalar());

            using var fts = conn.CreateCommand();
            fts.CommandText = "SELECT COUNT(*) FROM AssetFts WHERE AssetFts MATCH $q AND rowid = $row";
            fts.Parameters.AddWithValue("$q", "Tags:" + tagName);
            fts.Parameters.AddWithValue("$row", rowId);
            return Convert.ToInt64(fts.ExecuteScalar()) > 0;
        });
}
