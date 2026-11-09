using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>
/// Forward tag assign / remove / delete through the shared undo journal.
/// Bulk assign/remove/paste mutate AssetTag + journal in one SQLite transaction
/// (<see cref="CatalogService.ApplyBulkAssignJournaledAsync"/> /
/// <see cref="CatalogService.ApplyBulkRemoveJournaledAsync"/>) so a mid-loop or
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
    /// </summary>
    public Task<FileOpResult> AssignTagsAsync(
        IReadOnlyList<string> assetIds,
        IReadOnlyList<(string TagId, string TagName, TagSource Source)> tags,
        string projectId) =>
        GatedAsync(projectId, () => AssignTagsCoreAsync(assetIds, tags, projectId));

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

    private async Task<FileOpResult> AssignTagsCoreAsync(
        IReadOnlyList<string> assetIds,
        IReadOnlyList<(string TagId, string TagName, TagSource Source)> tags,
        string projectId)
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

        var batch = await _catalog.ApplyBulkAssignJournaledAsync(
            projectId,
            ids,
            unique.Values.ToList()).ConfigureAwait(false);
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
        if (string.IsNullOrEmpty(aliasTagId) || string.IsNullOrEmpty(idealTagId))
        {
            return FileOpResult.Fail("Pick two tags.");
        }

        var result = await _catalog.AddAliasAsync(aliasTagId, idealTagId).ConfigureAwait(false);
        if (!result.Ok)
        {
            return FileOpResult.Fail("That alias is not allowed.");
        }

        if (result.Migrated.Count == 0)
        {
            return new FileOpResult
            {
                Ok = true,
                Message = "Alias added.",
                Summary = "Alias added"
            };
        }

        var payload = CatalogService.SerializeAliasMigration(result.Migrated);
        var items = new List<FileOpItem>
        {
            new()
            {
                Step = FileOpStep.AddAlias,
                OldPath = result.AliasTagId,
                NewPath = result.IdealTagId,
                NewSourceId = payload
            }
        };
        var summary = $"Aliased tag ({AssetNouns.Count(result.Migrated.Count)} updated)";
        var batchId = await LogAsync(projectId, FileOpKind.TagAliasAdd, summary, items).ConfigureAwait(false);
        if (batchId is null)
        {
            try
            {
                await _catalog.UndoAliasAddAsync(result.AliasTagId, result.IdealTagId, result.Migrated)
                    .ConfigureAwait(false);
            }
            catch
            {
                return FileOpResult.Fail(JournalFailedPartialMessage);
            }

            return FileOpResult.Fail(JournalFailedMessage);
        }

        return new FileOpResult { Ok = true, Message = summary + ".", BatchId = batchId, Summary = summary };
    }

    private async Task<FileOpResult> DeleteTagCoreAsync(string tagId, string projectId)
    {
        var snapshot = await _catalog.SnapshotTagAsync(tagId).ConfigureAwait(false);
        if (snapshot is null)
        {
            return FileOpResult.Fail("That tag is gone.");
        }

        var name = snapshot.Tag.Name;
        await _catalog.DeleteTagAsync(tagId).ConfigureAwait(false);
        var items = new List<FileOpItem>
        {
            new()
            {
                Step = FileOpStep.DeleteTag,
                OldPath = tagId,
                NewPath = CatalogService.SerializeTagSnapshot(snapshot)
            }
        };
        var summary = $"Deleted {name}";
        var batchId = await LogAsync(projectId, FileOpKind.TagDelete, summary, items).ConfigureAwait(false);
        if (batchId is null)
        {
            try
            {
                await _catalog.RestoreTagSnapshotAsync(snapshot).ConfigureAwait(false);
            }
            catch
            {
                return FileOpResult.Fail(JournalFailedPartialMessage);
            }

            return FileOpResult.Fail(JournalFailedMessage);
        }

        var impact = AssetNouns.Count(snapshot.Assignments.Select(a => a.AssetId).Distinct().Count());
        return new FileOpResult
        {
            Ok = true,
            Message = $"Deleted “{name}” ({impact}).",
            BatchId = batchId,
            Summary = summary
        };
    }
}
