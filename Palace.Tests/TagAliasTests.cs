using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
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

        Assert.True((await _catalog.AddAliasAsync(doggy.Id, dog.Id)).Ok);
        Assert.True((await _catalog.AddAliasAsync(doggy.Id, dog.Id)).Ok, "Same edge is idempotent.");
        var pup = await _catalog.CreateTagAsync("pup");
        Assert.False((await _catalog.AddAliasAsync(doggy.Id, pup.Id)).Ok, "n→1: alias already bound elsewhere.");

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

        Assert.False((await _catalog.AddAliasAsync(a.Id, a.Id)).Ok);
        Assert.True((await _catalog.AddAliasAsync(b.Id, a.Id)).Ok);
        Assert.False((await _catalog.AddAliasAsync(a.Id, b.Id)).Ok);
    }

    [Fact]
    public async Task AddAlias_ResolvesTransitively_InCatalog()
    {
        var a = await _catalog.CreateTagAsync("a-name");
        var b = await _catalog.CreateTagAsync("b-name");
        var c = await _catalog.CreateTagAsync("c-name");

        Assert.True((await _catalog.AddAliasAsync(a.Id, b.Id)).Ok);
        Assert.True((await _catalog.AddAliasAsync(b.Id, c.Id)).Ok);
        Assert.Equal(c.Id, await _catalog.ResolveIdealTagIdAsync(a.Id));
    }

    [Fact]
    public async Task Assign_RewritesAliasToIdeal_ThenAppliesImplications()
    {
        var doggy = await _catalog.CreateTagAsync("doggy");
        var dog = await _catalog.CreateTagAsync("dog");
        var animal = await _catalog.CreateTagAsync("animal");
        Assert.True((await _catalog.AddAliasAsync(doggy.Id, dog.Id)).Ok);
        Assert.True(await _catalog.AddImplicationAsync(dog.Id, animal.Id));

        var asset = await AddAssetAsync("pet.png");
        await _catalog.AssignTagAsync(asset.Id, doggy.Id, TagSource.Manual);

        var stored = await SourcesByTagAsync(asset.Id);
        Assert.False(stored.ContainsKey(doggy.Id), "Alias must not be stored on assign (v1 rewrite).");
        Assert.Equal(TagSource.Manual, stored[dog.Id]);
        Assert.False(stored.ContainsKey(animal.Id), "T5: implied not materialized.");

        var display = await _catalog.GetAssignedTagsAsync(asset.Id);
        Assert.Equal(TagSource.Implied, display.Single(t => t.TagId == animal.Id).Source);
    }

    [Fact]
    public async Task GetOrCreate_ResolvesAliasNameToIdeal()
    {
        var doggy = await _catalog.CreateTagAsync("doggy");
        var dog = await _catalog.CreateTagAsync("dog");
        Assert.True((await _catalog.AddAliasAsync(doggy.Id, dog.Id)).Ok);

        var resolved = await _catalog.GetOrCreateTagAsync("doggy");
        Assert.Equal(dog.Id, resolved.Id);
        Assert.Equal("dog", resolved.Name);
    }

    [Fact]
    public async Task Search_AliasQuery_FindsIdealTaggedAsset()
    {
        var doggy = await _catalog.CreateTagAsync("doggy");
        var dog = await _catalog.CreateTagAsync("dog");
        Assert.True((await _catalog.AddAliasAsync(doggy.Id, dog.Id)).Ok);

        var asset = await AddAssetAsync("hound.png");
        await _catalog.AssignTagAsync(asset.Id, dog.Id, TagSource.Manual);

        Assert.False(await FtsHasAsync(asset.Id, "doggy"), "FTS stores ideal name only after rewrite.");
        Assert.True(await FtsHasAsync(asset.Id, "dog"));

        var hits = await _catalog.SearchAsync("doggy");
        Assert.Contains(hits, a => a.Id == asset.Id);
    }

    [Fact]
    public async Task ExportImport_RoundTripsAliasOf()
    {
        var doggy = await _catalog.CreateTagAsync("doggy");
        var dog = await _catalog.CreateTagAsync("dog");
        Assert.True((await _catalog.AddAliasAsync(doggy.Id, dog.Id)).Ok);

        var json = await _catalog.ExportTagsJsonAsync();
        Assert.Contains("\"aliasOf\": \"dog\"", json, StringComparison.Ordinal);

        // Fresh catalog: import into empty-ish tag set via second service on same DB is messy —
        // parse + re-import on same catalog is idempotent.
        var plan = await _catalog.ImportTagsJsonAsync(json);
        Assert.Equal(0, plan.AliasRejected);
        var aliases = await _catalog.GetAliasesAsync();
        Assert.Contains(aliases, a => a.AliasTagId == doggy.Id && a.IdealTagId == dog.Id);
    }

    [Fact]
    public async Task Import_AliasOf_CreatesEdge_AndDoesNotMaterializeImplied()
    {
        var json = """
            {
              "schema": "palace-tags/1",
              "tags": [
                { "name": "animal" },
                { "name": "dog", "implies": ["animal"] },
                { "name": "doggy", "aliasOf": "dog" }
              ]
            }
            """;

        var plan = await _catalog.ImportTagsJsonAsync(json);
        Assert.Equal(1, plan.AliasAdded);
        Assert.True(plan.ImplicationAdded >= 1);

        var aliases = await _catalog.GetAliasesAsync();
        var doggy = Assert.Single(await _catalog.GetTagsAsync(), t => t.Name == "doggy");
        var dog = Assert.Single(await _catalog.GetTagsAsync(), t => t.Name == "dog");
        Assert.Contains(aliases, a => a.AliasTagId == doggy.Id && a.IdealTagId == dog.Id);

        var asset = await AddAssetAsync("pet2.png");
        await _catalog.AssignTagAsync(asset.Id, doggy.Id, TagSource.Manual);

        var stored = await SourcesByTagAsync(asset.Id);
        Assert.Equal(TagSource.Manual, stored[dog.Id]);
        Assert.DoesNotContain(TagSource.Implied, stored.Values);
        Assert.DoesNotContain(doggy.Id, stored.Keys);
    }

    [Fact]
    public async Task Import_BadAliasEdge_DoesNotUndoPriorCreates()
    {
        var dog = await _catalog.CreateTagAsync("dog");
        var doggy = await _catalog.CreateTagAsync("doggy");
        Assert.True((await _catalog.AddAliasAsync(doggy.Id, dog.Id)).Ok);

        // Rebind doggy→pup must reject; new tag "pup" still created (per-edge, not whole-file atomic).
        var json = """
            {
              "schema": "palace-tags/1",
              "tags": [
                { "name": "pup" },
                { "name": "doggy", "aliasOf": "pup" }
              ]
            }
            """;
        var plan = await _catalog.ImportTagsJsonAsync(json);
        Assert.Equal(1, plan.AliasRejected);
        Assert.Contains(await _catalog.GetTagsAsync(), t => t.Name == "pup");
        var aliases = await _catalog.GetAliasesAsync();
        Assert.Contains(aliases, a => a.AliasTagId == doggy.Id && a.IdealTagId == dog.Id);
    }

    [Fact]
    public async Task TagFilter_AliasAndIdeal_MatchSameAssets()
    {
        var doggy = await _catalog.CreateTagAsync("doggy");
        var dog = await _catalog.CreateTagAsync("dog");
        Assert.True((await _catalog.AddAliasAsync(doggy.Id, dog.Id)).Ok);

        var asset = await AddAssetAsync("match.png");
        await _catalog.AssignTagAsync(asset.Id, dog.Id, TagSource.Manual);

        var memberships = (await _catalog.GetMembershipsAsync()).ToList();
        var aliasMap = (await _catalog.GetAliasesAsync())
            .ToDictionary(a => a.AliasTagId, a => a.IdealTagId, StringComparer.Ordinal);

        var viaAlias = TagFilter.ExpandEach([doggy.Id], memberships, aliasMap);
        var viaIdeal = TagFilter.ExpandEach([dog.Id], memberships, aliasMap);
        var projectId = _source.ProjectId;
        var aliasHits = await _catalog.GetAssetsForTagFilterAsync(viaAlias, TagFilterMode.Any, projectId);
        var idealHits = await _catalog.GetAssetsForTagFilterAsync(viaIdeal, TagFilterMode.Any, projectId);

        Assert.Contains(aliasHits, a => a.Id == asset.Id);
        Assert.Equal(
            idealHits.Select(a => a.Id).OrderBy(x => x),
            aliasHits.Select(a => a.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task EmptyDb_ExportImport_RoundTripsAliases()
    {
        // Doktor gap: empty catalog → import alias graph → export → second empty DB import.
        var json = """
            {
              "schema": "palace-tags/1",
              "tags": [
                { "name": "doggy", "aliasOf": "dog" },
                { "name": "pup", "aliasOf": "dog" },
                { "name": "dog" }
              ]
            }
            """;

        var plan = await _catalog.ImportTagsJsonAsync(json);
        Assert.Equal(2, plan.AliasAdded);
        Assert.Equal(0, plan.AliasRejected);

        var exported = await _catalog.ExportTagsJsonAsync();
        Assert.Contains("\"aliasOf\": \"dog\"", exported, StringComparison.Ordinal);

        var dir2 = Path.Combine(Path.GetTempPath(), "palace-alias-empty2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir2);
        try
        {
            using var db2 = new PalaceDb(Path.Combine(dir2, "catalog", "palace.db"));
            var catalog2 = new CatalogService(db2);
            var plan2 = await catalog2.ImportTagsJsonAsync(exported);
            Assert.Equal(2, plan2.AliasAdded);
            Assert.Equal(0, plan2.AliasRejected);

            var aliases = await catalog2.GetAliasesAsync();
            var dog = Assert.Single(await catalog2.GetTagsAsync(), t => t.Name == "dog");
            var doggy = Assert.Single(await catalog2.GetTagsAsync(), t => t.Name == "doggy");
            var pup = Assert.Single(await catalog2.GetTagsAsync(), t => t.Name == "pup");
            Assert.Contains(aliases, a => a.AliasTagId == doggy.Id && a.IdealTagId == dog.Id);
            Assert.Contains(aliases, a => a.AliasTagId == pup.Id && a.IdealTagId == dog.Id);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir2, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task Import_AliasOf_MigratesExistingAssetTagOnAlias()
    {
        // Doktor gap: AssetTag already on the alias name before import binds alias→ideal.
        var doggy = await _catalog.CreateTagAsync("doggy");
        var asset = await AddAssetAsync("pre-alias.png");
        await _catalog.AssignTagAsync(asset.Id, doggy.Id, TagSource.Manual);
        Assert.Equal(TagSource.Manual, (await SourcesByTagAsync(asset.Id))[doggy.Id]);

        var json = """
            {
              "schema": "palace-tags/1",
              "tags": [
                { "name": "dog" },
                { "name": "doggy", "aliasOf": "dog" }
              ]
            }
            """;
        var plan = await _catalog.ImportTagsJsonAsync(json);
        Assert.Equal(1, plan.AliasAdded);

        var dog = Assert.Single(await _catalog.GetTagsAsync(), t => t.Name == "dog");
        var stored = await SourcesByTagAsync(asset.Id);
        Assert.Equal(TagSource.Manual, stored[dog.Id]);
        Assert.DoesNotContain(doggy.Id, stored.Keys);
        Assert.DoesNotContain(TagSource.Implied, stored.Values);
    }

    [Fact]
    public async Task TagFilter_AliasAndIdeal_MatchSameAssets_AllAndNone()
    {
        // Doktor gap: Any already covered; All / None must also treat alias ≡ ideal.
        var doggy = await _catalog.CreateTagAsync("doggy");
        var dog = await _catalog.CreateTagAsync("dog");
        var subject = await _catalog.CreateTagAsync("n8-subject");
        Assert.True((await _catalog.AddAliasAsync(doggy.Id, dog.Id)).Ok);

        var both = await AddAssetAsync("both.png");
        var dogOnly = await AddAssetAsync("dog-only.png");
        var subjectOnly = await AddAssetAsync("subject-only.png");
        await _catalog.AssignTagAsync(both.Id, dog.Id, TagSource.Manual);
        await _catalog.AssignTagAsync(both.Id, subject.Id, TagSource.Manual);
        await _catalog.AssignTagAsync(dogOnly.Id, dog.Id, TagSource.Manual);
        await _catalog.AssignTagAsync(subjectOnly.Id, subject.Id, TagSource.Manual);

        var memberships = (await _catalog.GetMembershipsAsync()).ToList();
        var aliasMap = (await _catalog.GetAliasesAsync())
            .ToDictionary(a => a.AliasTagId, a => a.IdealTagId, StringComparer.Ordinal);
        var projectId = _source.ProjectId;

        var allViaAlias = TagFilter.ExpandEach([doggy.Id, subject.Id], memberships, aliasMap);
        var allViaIdeal = TagFilter.ExpandEach([dog.Id, subject.Id], memberships, aliasMap);
        var allAliasHits = await _catalog.GetAssetsForTagFilterAsync(allViaAlias, TagFilterMode.All, projectId);
        var allIdealHits = await _catalog.GetAssetsForTagFilterAsync(allViaIdeal, TagFilterMode.All, projectId);
        Assert.Equal(
            allIdealHits.Select(a => a.Id).OrderBy(x => x),
            allAliasHits.Select(a => a.Id).OrderBy(x => x));
        Assert.Equal([both.Id], allAliasHits.Select(a => a.Id).OrderBy(x => x));

        var noneViaAlias = TagFilter.ExpandEach([doggy.Id], memberships, aliasMap);
        var noneViaIdeal = TagFilter.ExpandEach([dog.Id], memberships, aliasMap);
        var noneAliasHits = await _catalog.GetAssetsForTagFilterAsync(noneViaAlias, TagFilterMode.None, projectId);
        var noneIdealHits = await _catalog.GetAssetsForTagFilterAsync(noneViaIdeal, TagFilterMode.None, projectId);
        Assert.Equal(
            noneIdealHits.Select(a => a.Id).OrderBy(x => x),
            noneAliasHits.Select(a => a.Id).OrderBy(x => x));
        Assert.Contains(noneAliasHits, a => a.Id == subjectOnly.Id);
        Assert.DoesNotContain(noneAliasHits, a => a.Id == both.Id);
        Assert.DoesNotContain(noneAliasHits, a => a.Id == dogOnly.Id);
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
