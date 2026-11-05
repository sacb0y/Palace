using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>T6: export/organize applied-only default; optional includeImplied.</summary>
public sealed class TagT6Tests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-t6-" + Guid.NewGuid().ToString("N"));
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
    public async Task ExportTags_Default_AppliedOnly()
    {
        var wolf = await _catalog.CreateTagAsync("wolf-ex");
        var canine = await _catalog.CreateTagAsync("canine-ex");
        Assert.True(await _catalog.AddImplicationAsync(wolf.Id, canine.Id));
        var asset = await AddAssetAsync("ex.png");
        await _catalog.AssignTagAsync(asset.Id, wolf.Id, TagSource.Manual);

        var exported = await _catalog.GetExportTagsAsync(asset.Id);
        Assert.Contains(exported, t => t.TagId == wolf.Id && t.Source == TagSource.Manual);
        Assert.DoesNotContain(exported, t => t.TagId == canine.Id);

        // Display still computes Implied.
        var display = await _catalog.GetAssignedTagsAsync(asset.Id);
        Assert.Contains(display, t => t.TagId == canine.Id && t.Source == TagSource.Implied);
    }

    [Fact]
    public async Task ExportTags_IncludeImplied_AddsClosure()
    {
        var wolf = await _catalog.CreateTagAsync("wolf-inc");
        var canine = await _catalog.CreateTagAsync("canine-inc");
        Assert.True(await _catalog.AddImplicationAsync(wolf.Id, canine.Id));
        var asset = await AddAssetAsync("inc.png");
        await _catalog.AssignTagAsync(asset.Id, wolf.Id, TagSource.Manual);

        var exported = await _catalog.GetExportTagsAsync(asset.Id, includeImplied: true);
        Assert.Contains(exported, t => t.TagId == wolf.Id && t.Source == TagSource.Manual);
        Assert.Contains(exported, t => t.TagId == canine.Id && t.Source == TagSource.Implied);
    }

    [Fact]
    public async Task OrganizeNames_UsesAppliedOnly_NotImplied()
    {
        var wolf = await _catalog.CreateTagAsync("WolfOrg");
        var canine = await _catalog.CreateTagAsync("CanineOrg");
        Assert.True(await _catalog.AddImplicationAsync(wolf.Id, canine.Id));
        var asset = await AddAssetAsync("org.webp");
        await _catalog.AssignTagAsync(asset.Id, wolf.Id, TagSource.Manual);

        var tags = await _catalog.GetExportTagsAsync(asset.Id, includeImplied: false);
        var file = OrganizeNames.RenderFile("{tags}.{ext}", asset, tags);
        Assert.Equal("WolfOrg.webp", file);
        Assert.DoesNotContain("CanineOrg", file, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParentExpand_CapsOrTerms()
    {
        var emptyAlias = new Dictionary<string, string>(StringComparer.Ordinal);
        var idByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["parent"] = "p"
        };
        var nameById = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["p"] = "parent"
        };
        var implying = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var children = new List<string>();
        for (var i = 0; i < TagFtsQuery.MaxParentExpandOrTerms + 10; i++)
        {
            var id = $"c{i}";
            var name = $"child{i:D3}";
            children.Add(id);
            nameById[id] = name;
            idByName[name] = id;
        }

        implying["p"] = children;

        var q = TagFtsQuery.Build("parent", emptyAlias, idByName, nameById, implying);
        var orCount = q.Count(c => c == '|') > 0
            ? q.Split(" OR ", StringSplitOptions.None).Length
            : 1;
        Assert.True(orCount <= TagFtsQuery.MaxParentExpandOrTerms, $"OR terms {orCount} exceed cap");
        Assert.StartsWith("(", q);
        Assert.Contains("\"parent\"*", q);
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
}
