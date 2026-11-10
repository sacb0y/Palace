using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>Outcome of Delete rejected (Recycle or permanent).</summary>
public sealed class DeleteRejectedResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public string? BatchId { get; init; }
    public string? Summary { get; init; }
    public int Succeeded { get; init; }
    public int NetworkSkipped { get; init; }
    public int Failed { get; init; }
    public long Bytes { get; init; }
    public DeleteRejectedDestination Destination { get; init; }
    public IReadOnlyList<string> RemovedAssetIds { get; init; } = [];
    public IReadOnlyList<DeleteRejectedFailure> Failures { get; init; } = [];
    public IReadOnlyList<string> RemovedFolders { get; init; } = [];
    public bool CanUndo => Destination == DeleteRejectedDestination.RecycleBin && BatchId is not null;

    public static DeleteRejectedResult Fail(string message) => new() { Ok = false, Message = message };
}

public sealed partial class FileOpService
{
    /// <summary>
    /// Delete rejected assets (must live in <c>_rejected</c>). Recycle journals an undoable
    /// batch; permanent does not. Network-share files are skipped on Recycle and left in place.
    /// Emptied <c>_rejected</c> folders are removed.
    /// </summary>
    public Task<DeleteRejectedResult> DeleteRejectedAsync(
        IReadOnlyList<Asset> candidates,
        string projectId,
        DeleteRejectedDestination destination,
        Func<string, bool>? isNetwork = null,
        CancellationToken cancellationToken = default) =>
        GatedAsync(projectId, () => DeleteRejectedCoreAsync(
            candidates, projectId, destination, isNetwork, cancellationToken), cancellationToken);

    private async Task<DeleteRejectedResult> DeleteRejectedCoreAsync(
        IReadOnlyList<Asset> candidates,
        string projectId,
        DeleteRejectedDestination destination,
        Func<string, bool>? isNetwork,
        CancellationToken cancellationToken)
    {
        var rejected = DeleteRejected.OnlyRejected(candidates);
        if (rejected.Count == 0)
        {
            return DeleteRejectedResult.Fail("No rejected files to delete.");
        }

        // Refuse anything that somehow slipped past OnlyRejected (defense in depth).
        if (rejected.Any(a => !DeleteRejected.IsRejectedPath(a.Path)))
        {
            return DeleteRejectedResult.Fail("Refusing to delete files outside _rejected.");
        }

        var actionable = DeleteRejected.Actionable(rejected, destination, out var networkSkipped, isNetwork);
        if (actionable.Count == 0 && networkSkipped.Count > 0)
        {
            return new DeleteRejectedResult
            {
                Ok = true,
                Message = DeleteRejected.NetworkSkipNote(networkSkipped.Count),
                NetworkSkipped = networkSkipped.Count,
                Destination = destination
            };
        }

        if (actionable.Count == 0)
        {
            return DeleteRejectedResult.Fail("No rejected files to delete.");
        }

        await _catalog.BackupBeforeDeleteRejectedAsync(actionable.Count).ConfigureAwait(false);

        var allSources = await _catalog.GetSourceFoldersAsync().ConfigureAwait(false);
        var journal = new List<FileOpItem>();
        var removedIds = new List<string>();
        var failures = new List<DeleteRejectedFailure>();
        var removedFolders = new List<string>();
        long bytes = 0;
        var seq = 0;

        using (_suppressWatcher?.Invoke())
        {
            foreach (var asset in actionable)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ResolveWritableRoot(asset.Path, allSources, projectId, out var rootError) is null)
                {
                    failures.Add(new DeleteRejectedFailure(
                        asset.Id, asset.Path, asset.FileName, rootError));
                    continue;
                }

                if (!DeleteRejected.IsRejectedPath(asset.Path))
                {
                    failures.Add(new DeleteRejectedFailure(
                        asset.Id, asset.Path, asset.FileName, "not in _rejected"));
                    continue;
                }

                AssetDeleteSnapshot? snapshot = null;
                if (destination == DeleteRejectedDestination.RecycleBin)
                {
                    snapshot = await _catalog.SnapshotAssetAsync(asset.Id).ConfigureAwait(false);
                    if (snapshot is null)
                    {
                        failures.Add(new DeleteRejectedFailure(
                            asset.Id, asset.Path, asset.FileName, "catalog row missing"));
                        continue;
                    }
                }

                try
                {
                    string? token = null;
                    if (destination == DeleteRejectedDestination.RecycleBin)
                    {
                        token = _trash.Recycle(asset.Path);
                        // Never journal OldPath as a fake token — restore needs a real trash key.
                        if (string.IsNullOrWhiteSpace(token))
                        {
                            throw new IOException("Recycle Bin returned no restore token.");
                        }
                    }
                    else
                    {
                        _trash.PermanentDelete(asset.Path);
                    }

                    bytes += asset.FileSize ?? 0;
                    removedIds.Add(asset.Id);

                    if (destination == DeleteRejectedDestination.RecycleBin && snapshot is not null)
                    {
                        journal.Add(new FileOpItem
                        {
                            Seq = seq++,
                            Step = FileOpStep.RecycleFile,
                            AssetId = asset.Id,
                            OldPath = asset.Path,
                            NewPath = CatalogService.SerializeAssetSnapshot(snapshot),
                            OldSourceId = asset.SourceFolderId,
                            NewSourceId = token
                        });
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
                {
                    failures.Add(new DeleteRejectedFailure(
                        asset.Id,
                        asset.Path,
                        asset.FileName,
                        DeleteRejected.FailureReason(ex)));
                }
            }

            if (removedIds.Count > 0)
            {
                await _catalog.DeleteAssetsAsync(removedIds).ConfigureAwait(false);
            }

            // Remove emptied _rejected folders (only those folders — never parents).
            var folderCandidates = removedIds
                .Select(id => actionable.FirstOrDefault(a => a.Id == id))
                .Where(a => a is not null)
                .Select(a => DeleteRejected.RejectedFolderOf(a!.Path))
                .Where(p => p is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var folder in folderCandidates)
            {
                if (folder is null || !DeleteRejected.IsEmptyRejectedFolder(folder))
                {
                    continue;
                }

                if (ResolveWritableRoot(folder, allSources, projectId, out _) is null)
                {
                    continue;
                }

                try
                {
                    Directory.Delete(folder, recursive: false);
                    removedFolders.Add(folder);
                    if (destination == DeleteRejectedDestination.RecycleBin)
                    {
                        journal.Add(new FileOpItem
                        {
                            Seq = seq++,
                            Step = FileOpStep.DeleteDir,
                            OldPath = folder,
                            NewPath = ""
                        });
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Leave the folder; files that remain (failures) keep it non-empty usually.
                    _ = ex;
                }
            }
        }

        string? batchId = null;
        string? summary = null;
        if (destination == DeleteRejectedDestination.RecycleBin && journal.Count > 0)
        {
            summary = $"Delete rejected ({removedIds.Count}) to Recycle Bin";
            batchId = await LogAsync(projectId, FileOpKind.DeleteRejected, summary, journal)
                .ConfigureAwait(false);
            if (batchId is null)
            {
                // Journal failed — Undo steps already attempted restore.
                return new DeleteRejectedResult
                {
                    Ok = false,
                    Message = LastJournalPartial ? JournalFailedPartialMessage : JournalFailedMessage,
                    Succeeded = 0,
                    NetworkSkipped = networkSkipped.Count,
                    Failed = failures.Count + removedIds.Count,
                    Destination = destination,
                    Failures = failures
                };
            }
        }

        var message = BuildStatusMessage(destination, removedIds.Count, actionable.Count, bytes, networkSkipped.Count, failures.Count);
        return new DeleteRejectedResult
        {
            Ok = removedIds.Count > 0 || (networkSkipped.Count > 0 && failures.Count == 0),
            Message = message,
            BatchId = batchId,
            Summary = summary,
            Succeeded = removedIds.Count,
            NetworkSkipped = networkSkipped.Count,
            Failed = failures.Count,
            Bytes = bytes,
            Destination = destination,
            RemovedAssetIds = removedIds,
            Failures = failures,
            RemovedFolders = removedFolders
        };
    }

    private static string BuildStatusMessage(
        DeleteRejectedDestination destination,
        int succeeded,
        int attempted,
        long bytes,
        int networkSkipped,
        int failed)
    {
        if (destination == DeleteRejectedDestination.Permanent)
        {
            if (failed > 0)
            {
                return DeleteRejected.StatusPermanentPartial(succeeded, attempted, failed);
            }

            return DeleteRejected.StatusPermanentSuccess(succeeded, bytes);
        }

        if (failed > 0)
        {
            var partial = DeleteRejected.StatusRecyclePartial(succeeded, attempted, failed);
            return networkSkipped > 0
                ? $"{partial} · {networkSkipped} skipped (network)"
                : partial;
        }

        return DeleteRejected.StatusRecycleSuccess(succeeded, bytes, networkSkipped);
    }

    private async Task<UndoNote?> UndoRecycleFileStepAsync(
        FileOpItem item,
        IReadOnlyList<SourceFolder> allSources,
        string projectId,
        List<FileOpMove> restored)
    {
        if (string.IsNullOrWhiteSpace(item.OldPath) || string.IsNullOrWhiteSpace(item.NewPath))
        {
            return UndoNote.Skip("recycle: incomplete journal row");
        }

        var snapshot = CatalogService.DeserializeAssetSnapshot(item.NewPath);
        if (snapshot?.Asset is null)
        {
            return UndoNote.Skip($"{Path.GetFileName(item.OldPath)}: bad catalog snapshot");
        }

        if (ResolveWritableRoot(item.OldPath, allSources, projectId, out var rootError) is null)
        {
            return UndoNote.Skip($"{Path.GetFileName(item.OldPath)}: {rootError}");
        }

        // Ensure _rejected parent exists (DeleteDir undo may already have recreated it).
        var parent = Path.GetDirectoryName(item.OldPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            try
            {
                Directory.CreateDirectory(parent);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return UndoNote.Retry($"{Path.GetFileName(item.OldPath)}: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(item.NewSourceId))
        {
            return UndoNote.Retry($"{Path.GetFileName(item.OldPath)}: missing trash token");
        }

        var token = item.NewSourceId;
        try
        {
            if (!_trash.TryRestore(token, item.OldPath))
            {
                // File already at destination counts as success inside TryRestore; missing trash = fail.
                if (!CloudFile.Exists(item.OldPath))
                {
                    return UndoNote.Retry($"{Path.GetFileName(item.OldPath)}: not in Recycle Bin");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return UndoNote.Retry($"{Path.GetFileName(item.OldPath)}: {ex.Message}");
        }

        try
        {
            // Keep snapshot path authoritative.
            snapshot.Asset.Path = item.OldPath;
            snapshot.Asset.FileName = Path.GetFileName(item.OldPath);
            if (!string.IsNullOrWhiteSpace(item.OldSourceId))
            {
                snapshot.Asset.SourceFolderId = item.OldSourceId;
            }

            await _catalog.RestoreAssetSnapshotAsync(snapshot).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(item.AssetId))
            {
                restored.Add(new FileOpMove(
                    item.AssetId,
                    item.OldPath,
                    item.OldPath,
                    item.OldSourceId ?? snapshot.Asset.SourceFolderId));
            }

            return null;
        }
        catch (Exception ex)
        {
            return UndoNote.Retry($"{Path.GetFileName(item.OldPath)}: catalog restore failed ({ex.Message})");
        }
    }
}
