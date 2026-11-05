using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>
/// Forward tag assign / remove / delete through the shared undo journal.
/// App-only partial (Palace.Tests links <see cref="FileOpService"/> without this file).
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

        var items = new List<FileOpItem>();
        var appliedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tagId, tagName, source) in unique.Values)
        {
            foreach (var assetId in ids)
            {
                var before = await _catalog.GetStoredAssignedTagsAsync(assetId).ConfigureAwait(false);
                await _catalog.AssignTagAsync(assetId, tagId, source).ConfigureAwait(false);
                var after = await _catalog.GetStoredAssignedTagsAsync(assetId).ConfigureAwait(false);
                var change = TagAssignUndo.PlanAssign(
                    before.Select(t => t.TagId),
                    after.Select(t => t.TagId),
                    tagId);
                if (!change.Journal)
                {
                    continue;
                }

                appliedNames.Add(tagName);
                items.Add(new FileOpItem
                {
                    Step = FileOpStep.AssignTag,
                    AssetId = assetId,
                    OldPath = tagId,
                    NewPath = tagName,
                    OldSourceId = source.ToString(),
                    NewSourceId = TagAssignUndo.Encode(change.ImpliedIds, change.TouchAssignedTag)
                });
            }
        }

        if (items.Count == 0)
        {
            var already = unique.Count == 1
                ? $"{unique.Values.First().TagName} was already on the selection."
                : "Those tags were already on the selection.";
            return new FileOpResult { Ok = true, Message = already, Summary = already };
        }

        var assetCount = items.Select(i => i.AssetId).Distinct(StringComparer.Ordinal).Count();
        var summary = appliedNames.Count == 1
            ? $"Tagged {AssetNouns.Count(assetCount)} with {appliedNames.First()}"
            : $"Tagged {AssetNouns.Count(assetCount)} with {appliedNames.Count} tags";
        var batchId = await LogAsync(projectId, FileOpKind.TagAssign, summary, items).ConfigureAwait(false);
        if (batchId is null)
        {
            return FileOpResult.Fail(LastJournalPartial ? JournalFailedPartialMessage : JournalFailedMessage);
        }

        return new FileOpResult { Ok = true, Message = summary + ".", BatchId = batchId, Summary = summary };
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

        tagId = ideal.Id;
        tagName = ideal.Name;

        var items = new List<FileOpItem>();
        foreach (var (assetId, source) in list)
        {
            var before = await _catalog.GetStoredAssignedTagsAsync(assetId).ConfigureAwait(false);
            var recorded = TagAssignUndo.RecordedSource(
                before.Select(t => (t.TagId, t.Source)),
                tagId,
                source);
            if (recorded == TagSource.Implied)
            {
                await _catalog.RemoveTagAsync(assetId, tagId).ConfigureAwait(false);
                continue;
            }

            await _catalog.RemoveTagAsync(assetId, tagId).ConfigureAwait(false);
            var after = await _catalog.GetStoredAssignedTagsAsync(assetId).ConfigureAwait(false);
            var change = TagAssignUndo.PlanRemove(
                before.Select(t => (t.TagId, t.Source)),
                after.Select(t => (t.TagId, t.Source)),
                tagId);
            if (!change.Journal)
            {
                continue;
            }

            items.Add(new FileOpItem
            {
                Step = FileOpStep.RemoveTag,
                AssetId = assetId,
                OldPath = tagId,
                NewPath = tagName,
                OldSourceId = recorded.ToString(),
                NewSourceId = TagAssignUndo.Encode(change.ImpliedIds, change.TouchAssignedTag)
            });
        }

        if (items.Count == 0)
        {
            return new FileOpResult
            {
                Ok = true,
                Message = $"{tagName} was not on the selection.",
                Summary = tagName
            };
        }

        var summary = $"Removed {tagName} from {AssetNouns.Count(items.Count)}";
        var batchId = await LogAsync(projectId, FileOpKind.TagRemove, summary, items).ConfigureAwait(false);
        if (batchId is null)
        {
            return FileOpResult.Fail(LastJournalPartial ? JournalFailedPartialMessage : JournalFailedMessage);
        }

        return new FileOpResult { Ok = true, Message = summary + ".", BatchId = batchId, Summary = summary };
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

        var items = new List<FileOpItem>();
        var removedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tagId, tagName) in unique.Values)
        {
            foreach (var assetId in ids)
            {
                var before = await _catalog.GetStoredAssignedTagsAsync(assetId).ConfigureAwait(false);
                var recorded = TagAssignUndo.RecordedSource(
                    before.Select(t => (t.TagId, t.Source)),
                    tagId,
                    TagSource.Manual);
                // T7: never journal Source=Implied (undo would rematerialize / no-op).
                if (recorded == TagSource.Implied)
                {
                    await _catalog.RemoveTagAsync(assetId, tagId).ConfigureAwait(false);
                    continue;
                }

                await _catalog.RemoveTagAsync(assetId, tagId).ConfigureAwait(false);
                var after = await _catalog.GetStoredAssignedTagsAsync(assetId).ConfigureAwait(false);
                var change = TagAssignUndo.PlanRemove(
                    before.Select(t => (t.TagId, t.Source)),
                    after.Select(t => (t.TagId, t.Source)),
                    tagId);
                if (!change.Journal)
                {
                    continue;
                }

                removedNames.Add(tagName);
                items.Add(new FileOpItem
                {
                    Step = FileOpStep.RemoveTag,
                    AssetId = assetId,
                    OldPath = tagId,
                    NewPath = tagName,
                    OldSourceId = recorded.ToString(),
                    NewSourceId = TagAssignUndo.Encode(change.ImpliedIds, change.TouchAssignedTag)
                });
            }
        }

        if (items.Count == 0)
        {
            var missing = unique.Count == 1
                ? $"{unique.Values.First().TagName} was not on the selection."
                : "Those tags were not on the selection.";
            return new FileOpResult { Ok = true, Message = missing, Summary = missing };
        }

        var assetCount = items.Select(i => i.AssetId).Distinct(StringComparer.Ordinal).Count();
        var summary = removedNames.Count == 1
            ? $"Removed {removedNames.First()} from {AssetNouns.Count(assetCount)}"
            : $"Removed {removedNames.Count} tags from {AssetNouns.Count(assetCount)}";
        var batchId = await LogAsync(projectId, FileOpKind.TagRemove, summary, items).ConfigureAwait(false);
        if (batchId is null)
        {
            return FileOpResult.Fail(LastJournalPartial ? JournalFailedPartialMessage : JournalFailedMessage);
        }

        return new FileOpResult { Ok = true, Message = summary + ".", BatchId = batchId, Summary = summary };
    }

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
