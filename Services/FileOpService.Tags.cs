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
        GatedAsync(projectId, () => AssignTagCoreAsync(assetIds, tagId, tagName, source, projectId));

    /// <summary>Remove <paramref name="tagId"/> from each asset; journals <see cref="FileOpKind.TagRemove"/>.</summary>
    public Task<FileOpResult> RemoveTagAsync(
        IReadOnlyList<(string AssetId, TagSource Source)> rows,
        string tagId,
        string tagName,
        string projectId) =>
        GatedAsync(projectId, () => RemoveTagCoreAsync(rows, tagId, tagName, projectId));

    /// <summary>Delete a tag after snapshotting; journals <see cref="FileOpKind.TagDelete"/>.</summary>
    public Task<FileOpResult> DeleteTagAsync(string tagId, string projectId) =>
        GatedAsync(projectId, () => DeleteTagCoreAsync(tagId, projectId));

    private async Task<FileOpResult> AssignTagCoreAsync(
        IReadOnlyList<string> assetIds,
        string tagId,
        string tagName,
        TagSource source,
        string projectId)
    {
        var ids = assetIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0 || string.IsNullOrEmpty(tagId))
        {
            return FileOpResult.Fail("Select items first.");
        }

        var items = new List<FileOpItem>();
        foreach (var assetId in ids)
        {
            await _catalog.AssignTagAsync(assetId, tagId, source).ConfigureAwait(false);
            items.Add(new FileOpItem
            {
                Step = FileOpStep.AssignTag,
                AssetId = assetId,
                OldPath = tagId,
                NewPath = tagName,
                OldSourceId = source.ToString()
            });
        }

        var summary = $"Tagged {AssetNouns.Count(ids.Count)} with {tagName}";
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

        var items = new List<FileOpItem>();
        foreach (var (assetId, source) in list)
        {
            await _catalog.RemoveTagAsync(assetId, tagId).ConfigureAwait(false);
            items.Add(new FileOpItem
            {
                Step = FileOpStep.RemoveTag,
                AssetId = assetId,
                OldPath = tagId,
                NewPath = tagName,
                OldSourceId = source.ToString()
            });
        }

        var summary = $"Removed {tagName} from {AssetNouns.Count(list.Count)}";
        var batchId = await LogAsync(projectId, FileOpKind.TagRemove, summary, items).ConfigureAwait(false);
        if (batchId is null)
        {
            return FileOpResult.Fail(LastJournalPartial ? JournalFailedPartialMessage : JournalFailedMessage);
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
