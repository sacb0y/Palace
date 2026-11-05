using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// T5: implications are edges only — Implied is computed at display/filter time,
/// never stored on AssetTag. Add/RemoveImplication no longer rewrite AssetTag.
/// </summary>
public sealed class TagImplicationRecalcTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-impl-recalc-" + Guid.NewGuid().ToString("N"));
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
    public async Task AddImplication_ComputesTransitiveImplied_AtDisplay_NotStored()
    {
        var wolf = await _catalog.CreateTagAsync("wolf");
        var canine = await _catalog.CreateTagAsync("canine");
        var mammal = await _catalog.CreateTagAsync("mammal");
        var asset = await AddAssetAsync("wolf.png");

        Assert.True(await _catalog.AddImplicationAsync(canine.Id, mammal.Id));
        await _catalog.AssignTagAsync(asset.Id, wolf.Id, TagSource.Manual);
        Assert.True(await _catalog.AddImplicationAsync(wolf.Id, canine.Id));

        var stored = await SourcesByTagAsync(asset.Id);
        Assert.Equal(new[] { wolf.Id }, stored.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.False(await FtsHasAsync(asset.Id, "canine"));
        Assert.False(await FtsHasAsync(asset.Id, "mammal"));
        Assert.True(await FtsHasAsync(asset.Id, "wolf"));

        var display = await _catalog.GetAssignedTagsAsync(asset.Id);
        Assert.Equal(TagSource.Manual, display.Single(t => t.TagId == wolf.Id).Source);
        Assert.Equal(TagSource.Implied, display.Single(t => t.TagId == canine.Id).Source);
        Assert.Equal(TagSource.Implied, display.Single(t => t.TagId == mammal.Id).Source);
    }

    [Fact]
    public async Task RemoveImplication_StopsComputingChain_KeepsApplied()
    {
        var wolf = await _catalog.CreateTagAsync("wolf");
        var canine = await _catalog.CreateTagAsync("canine");
        var mammal = await _catalog.CreateTagAsync("mammal");
        var asset = await AddAssetAsync("pack.png");

        Assert.True(await _catalog.AddImplicationAsync(wolf.Id, canine.Id));
        Assert.True(await _catalog.AddImplicationAsync(canine.Id, mammal.Id));
        await _catalog.AssignTagAsync(asset.Id, wolf.Id, TagSource.Manual);
        await _catalog.AssignTagAsync(asset.Id, mammal.Id, TagSource.Manual);

        await _catalog.RemoveImplicationAsync(wolf.Id, canine.Id);

        var stored = await SourcesByTagAsync(asset.Id);
        Assert.Equal(TagSource.Manual, stored[wolf.Id]);
        Assert.Equal(TagSource.Manual, stored[mammal.Id]);
        Assert.False(stored.ContainsKey(canine.Id));

        var display = await _catalog.GetAssignedTagsAsync(asset.Id);
        Assert.DoesNotContain(display, t => t.TagId == canine.Id);
        Assert.Equal(TagSource.Manual, display.Single(t => t.TagId == mammal.Id).Source);
    }

    [Fact]
    public async Task ParentFilter_FindsChildrenThatImplyParent()
    {
        var wolf = await _catalog.CreateTagAsync("wolf");
        var canine = await _catalog.CreateTagAsync("canine");
        Assert.True(await _catalog.AddImplicationAsync(wolf.Id, canine.Id));
        var asset = await AddAssetAsync("pack.png");
        await _catalog.AssignTagAsync(asset.Id, wolf.Id, TagSource.Manual);

        var hits = await _catalog.GetAssetsForTagsAsync([canine.Id]);
        Assert.Contains(hits, a => a.Id == asset.Id);
    }

    [Fact]
    public async Task RemoveImplication_DoesNotDeleteAppliedTags()
    {
        var parent = await _catalog.CreateTagAsync("parent");
        var child = await _catalog.CreateTagAsync("child");
        var asset = await AddAssetAsync("both.png");

        Assert.True(await _catalog.AddImplicationAsync(parent.Id, child.Id));
        await _catalog.AssignTagAsync(asset.Id, parent.Id, TagSource.Manual);
        await _catalog.AssignTagAsync(asset.Id, child.Id, TagSource.Manual);

        await _catalog.RemoveImplicationAsync(parent.Id, child.Id);

        var after = await SourcesByTagAsync(asset.Id);
        Assert.Equal(TagSource.Manual, after[parent.Id]);
        Assert.Equal(TagSource.Manual, after[child.Id]);
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
