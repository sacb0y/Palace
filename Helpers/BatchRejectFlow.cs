using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// Select-mode batch Reject loop (multi-folder absorb / interrupt / cancel).
/// WinUI-free so Palace.Tests can drive the same path as
/// <c>LibraryViewModel.RejectSelectedAsync</c>.
/// </summary>
public static class BatchRejectFlow
{
    /// <summary>One destination folder's movers (sibling <c>_rejected</c>).</summary>
    public readonly record struct FolderGroup(
        string DestinationDir,
        string ParentDir,
        IReadOnlyList<Asset> Assets);

    /// <summary>Outcome of one folder's CreateFolder (when needed).</summary>
    public readonly record struct CreateFolderResult(bool Ok, string? Path, string Message);

    /// <summary>Outcome of one folder's Move (mirrors FileOpResult fields we need).</summary>
    public readonly record struct MoveResult(
        int MovedCount,
        string? BatchId,
        bool AbsorbedPriorBatch,
        string Message);

    /// <summary>Final status after the loop.</summary>
    public readonly record struct RunResult(
        int Moved,
        int Failed,
        IReadOnlyList<string> UndoBatchIds,
        IReadOnlyList<string> FailNotes,
        bool Cancelled,
        bool Interrupted,
        string? LastMessage);

    /// <summary>
    /// Reject each folder group into its <c>_rejected</c>. Absorbs prior when
    /// safe; on race (<c>chainBatchId</c> set but not absorbed) or unsafe tip,
    /// stops with interrupted. Cancel leaves completed folders undoable.
    /// </summary>
    public static async Task<RunResult> RunAsync(
        IReadOnlyList<FolderGroup> groups,
        Func<string, string, Task<CreateFolderResult>> createFolder,
        Func<IReadOnlyList<Asset>, string, string?, string?, CancellationToken, Task<MoveResult>> move,
        Func<Task<IReadOnlyList<FileOpBatch>>> getUndoable,
        CancellationToken token)
    {
        var moved = 0;
        var failed = 0;
        var failNotes = new List<string>();
        string? lastMessage = null;
        string? chainBatchId = null;
        var undoBatchIds = new List<string>();
        var cancelled = false;
        var interrupted = false;

        foreach (var group in groups)
        {
            if (token.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            if (string.IsNullOrEmpty(group.DestinationDir))
            {
                failed += group.Assets.Count;
                failNotes.Add("missing folder path");
                continue;
            }

            if (string.IsNullOrEmpty(group.ParentDir))
            {
                failed += group.Assets.Count;
                failNotes.Add("missing parent folder");
                continue;
            }

            var dest = group.DestinationDir;

            // Absorb must be safe before CreateFolder / Move.
            if (chainBatchId is not null)
            {
                var undoable = await getUndoable();
                if (!BatchReject.CanAbsorbPrior(undoable, chainBatchId, dest))
                {
                    interrupted = true;
                    lastMessage = BatchReject.InterruptedMessage(moved);
                    break;
                }
            }

            string? absorb = null;
            if (!Directory.Exists(dest))
            {
                var created = await createFolder(group.ParentDir, ReviewReject.FolderName);
                if (!created.Ok || string.IsNullOrWhiteSpace(created.Path))
                {
                    if (!Directory.Exists(dest))
                    {
                        failed += group.Assets.Count;
                        failNotes.Add(created.Message);
                        continue;
                    }
                }
                else
                {
                    absorb = created.Path;
                }
            }

            if (token.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            MoveResult result;
            try
            {
                result = await move(group.Assets, dest, absorb, chainBatchId, token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                break;
            }

            if (result.MovedCount > 0)
            {
                moved += result.MovedCount;
                lastMessage = result.Message;
                if (!string.IsNullOrWhiteSpace(result.BatchId))
                {
                    // Race: tip changed between CanAbsorbPrior and Move.
                    if (chainBatchId is not null && !result.AbsorbedPriorBatch)
                    {
                        BatchReject.TrackUndoBatchId(
                            undoBatchIds,
                            chainBatchId,
                            result.BatchId,
                            absorbedPriorBatch: false);
                        interrupted = true;
                        lastMessage = BatchReject.InterruptedMessage(moved);
                        break;
                    }

                    BatchReject.TrackUndoBatchId(
                        undoBatchIds,
                        chainBatchId,
                        result.BatchId,
                        result.AbsorbedPriorBatch);
                    chainBatchId = result.BatchId;
                }
            }

            var skippedHere = group.Assets.Count - result.MovedCount;
            if (skippedHere > 0)
            {
                failed += skippedHere;
                if (!string.IsNullOrWhiteSpace(result.Message))
                {
                    failNotes.Add(result.Message);
                }
            }
        }

        return new RunResult(
            moved,
            failed,
            undoBatchIds,
            failNotes,
            cancelled,
            interrupted,
            lastMessage);
    }
}
