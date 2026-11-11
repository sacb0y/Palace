using Palace.Models;

namespace Palace.Services;

/// <summary>
/// Forward tag assign / remove / delete / alias through the shared undo journal.
/// Assign/remove/paste, delete-tag, and add-alias mutate catalog rows + journal in
/// one SQLite transaction
/// (<see cref="CatalogService.ApplyBulkAssignJournaledAsync"/> /
/// <see cref="CatalogService.ApplyBulkRemoveJournaledAsync"/> /
/// <see cref="CatalogService.ApplyDeleteTagJournaledAsync"/> /
/// <see cref="CatalogService.ApplyAddAliasJournaledAsync"/>) so a mid-loop or
/// journal failure cannot leave an unjournaled partial.
/// </summary>
public sealed partial class FileOpService
{
    /// <summary>Assign <paramref name="tagId"/> to each asset; journals <see cref="FileOpKind.TagAssign"/>.</summary>
    public Task<FileOpResult> AssignTagAsync(
        IReadOnlyList<string> assetIds,
        string tagId,
        string tagName,
        TagSource source,
        string projectId) =>
        GatedAsync(projectId, () => AssignTagsCoreAsync(assetIds, [(tagId, tagName, source)], projectId));

    /// <summary>
    /// Assign many tags across many assets in <b>one</b> <see cref="FileOpKind.TagAssign"/>
    /// batch (T7 paste). Resolves aliases→ideal; never journals <see cref="TagSource.Implied"/>.
    /// <paramref name="createdTags"/> are Tag rows inserted for this apply (CreateTag undo steps).
    /// </summary>
    public Task<FileOpResult> AssignTagsAsync(
        IReadOnlyList<string> assetIds,
        IReadOnlyList<(string TagId, string TagName, TagSource Source)> tags,
        string projectId,
        IReadOnlyList<(string TagId, string TagName)>? createdTags = null) =>
        GatedAsync(projectId, () => AssignTagsCoreAsync(assetIds, tags, projectId, createdTags));

    /// <summary>Remove <paramref name="tagId"/> from each asset; journals <see cref="FileOpKind.TagRemove"/>.</summary>
    public Task<FileOpResult> RemoveTagAsync(
        IReadOnlyList<(string AssetId, TagSource Source)> rows,
        string tagId,
        string tagName,
        string projectId) =>
        GatedAsync(projectId, () => RemoveTagCoreAsync(rows, tagId, tagName, projectId));

    /// <summary>
    /// Remove many tags across many assets in <b>one</b> <see cref="FileOpKind.TagRemove"/>
    /// batch. Resolves aliases→ideal; skips journaling Implied sources.
    /// </summary>
    public Task<FileOpResult> RemoveTagsAsync(
        IReadOnlyList<string> assetIds,
        IReadOnlyList<(string TagId, string TagName)> tags,
        string projectId) =>
        GatedAsync(projectId, () => RemoveTagsCoreAsync(assetIds, tags, projectId));

    /// <summary>Delete a tag after snapshotting; journals <see cref="FileOpKind.TagDelete"/>.</summary>
    public Task<FileOpResult> DeleteTagAsync(string tagId, string projectId) =>
        GatedAsync(projectId, () => DeleteTagCoreAsync(tagId, projectId));

    /// <summary>
    /// Tags-page batch create in <paramref name="parentId"/> (null = ungrouped).
    /// Journals <see cref="FileOpKind.TagCreate"/>.
    /// </summary>
    public Task<TagCreateBatchResult> CreateTagsAsync(
        IReadOnlyList<string> names,
        string? parentId,
        string projectId) =>
        GatedAsync(projectId, async () =>
        {
            var result = await _catalog.ApplyCreateTagsJournaledAsync(projectId, names, parentId)
                .ConfigureAwait(false);
            return result;
        });

    private async Task<FileOpResult> AssignTagsCoreAsync(
        IReadOnlyList<string> assetIds,
        IReadOnlyList<(string TagId, string TagName, TagSource Source)> tags,
        string projectId,
        IReadOnlyList<(string TagId, string TagName)>? createdTags = null)
    {
        var ids = assetIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0 || tags.Count == 0)
        {
            return FileOpResult.Fail("Select items first.");
        }

        var resolved = new List<(string TagId, string TagName, TagSource Source)>();
        foreach (var (tagId, _, source) in tags)
        {
            if (string.IsNullOrEmpty(tagId))
            {
                continue;
            }

            // T7: never journal / assign Source=Implied through the FileOp path.
            if (source == TagSource.Implied)
            {
                continue;
            }

            var ideal = await _catalog.GetIdealTagAsync(tagId).ConfigureAwait(false);
            if (ideal is null)
            {
                continue;
            }

            resolved.Add((ideal.Id, ideal.Name, source));
        }

        // Deduplicate by ideal id (last source wins).
        var unique = new Dictionary<string, (string TagId, string TagName, TagSource Source)>(StringComparer.Ordinal);
        foreach (var row in resolved)
        {
            unique[row.TagId] = row;
        }

        if (unique.Count == 0)
        {
            return FileOpResult.Fail("No tags to assign.");
        }

        // Only journal CreateTag for ids that survived alias→ideal resolve.
        List<(string TagId, string TagName)>? created = null;
        if (createdTags is { Count: > 0 })
        {
            created = createdTags
                .Where(c => !string.IsNullOrEmpty(c.TagId) && unique.ContainsKey(c.TagId))
                .GroupBy(c => c.TagId, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToList();
            if (created.Count == 0)
            {
                created = null;
            }
        }

        var batch = await _catalog.ApplyBulkAssignJournaledAsync(
            projectId,
            ids,
            unique.Values.ToList(),
            created).ConfigureAwait(false);
        return ToFileOpResult(batch);
    }

    private async Task<FileOpResult> RemoveTagCoreAsync(
        IReadOnlyList<(string AssetId, TagSource Source)> rows,
        string tagId,
        string tagName,
        string projectId)
    {
        var list = rows.Where(r => !string.IsNullOrEmpty(r.AssetId)).ToList();
        if (list.Count == 0 || string.IsNullOrEmpty(tagId))
        {
            return FileOpResult.Fail("Select items first.");
        }

        var ideal = await _catalog.GetIdealTagAsync(tagId).ConfigureAwait(false);
        if (ideal is null)
        {
            return FileOpResult.Fail("That tag is gone.");
        }

        var batch = await _catalog.ApplyBulkRemoveJournaledAsync(
            projectId,
            list.Select(r => (r.AssetId, r.Source)).ToList(),
            [(ideal.Id, ideal.Name)]).ConfigureAwait(false);
        return ToFileOpResult(batch);
    }

    private async Task<FileOpResult> RemoveTagsCoreAsync(
        IReadOnlyList<string> assetIds,
        IReadOnlyList<(string TagId, string TagName)> tags,
        string projectId)
    {
        var ids = assetIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0 || tags.Count == 0)
        {
            return FileOpResult.Fail("Select items first.");
        }

        var resolved = new List<(string TagId, string TagName)>();
        foreach (var (tagId, _) in tags)
        {
            if (string.IsNullOrEmpty(tagId))
            {
                continue;
            }

            var ideal = await _catalog.GetIdealTagAsync(tagId).ConfigureAwait(false);
            if (ideal is null)
            {
                continue;
            }

            resolved.Add((ideal.Id, ideal.Name));
        }

        var unique = new Dictionary<string, (string TagId, string TagName)>(StringComparer.Ordinal);
        foreach (var row in resolved)
        {
            unique[row.TagId] = row;
        }

        if (unique.Count == 0)
        {
            return FileOpResult.Fail("No tags to remove.");
        }

        var rows = ids.Select(id => (id, TagSource.Manual)).ToList();
        var batch = await _catalog.ApplyBulkRemoveJournaledAsync(
            projectId,
            rows,
            unique.Values.ToList()).ConfigureAwait(false);
        return ToFileOpResult(batch);
    }

    private static FileOpResult ToFileOpResult(BulkTagBatchResult batch) =>
        batch.Ok
            ? new FileOpResult
            {
                Ok = true,
                Message = batch.Message,
                BatchId = batch.BatchId,
                Summary = batch.Summary
            }
            : FileOpResult.Fail(batch.Message);

    /// <summary>Add alias→ideal; journals migrated AssetTag rows for Undo.</summary>
    public Task<FileOpResult> AddAliasAsync(string aliasTagId, string idealTagId, string projectId) =>
        GatedAsync(projectId, () => AddAliasCoreAsync(aliasTagId, idealTagId, projectId));

    private async Task<FileOpResult> AddAliasCoreAsync(string aliasTagId, string idealTagId, string projectId)
    {
        var batch = await _catalog.ApplyAddAliasJournaledAsync(projectId, aliasTagId, idealTagId)
            .ConfigureAwait(false);
        return ToFileOpResult(batch);
    }

    private async Task<FileOpResult> DeleteTagCoreAsync(string tagId, string projectId)
    {
        var batch = await _catalog.ApplyDeleteTagJournaledAsync(projectId, tagId).ConfigureAwait(false);
        return ToFileOpResult(batch);
    }
}
