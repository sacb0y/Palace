namespace Palace.Helpers;

/// <summary>
/// Review Undo orchestration: peek session history, undo Reject by batch id
/// (via injected disk undo), commit session rollback only after disk succeeds.
/// WinUI-free for Palace.Tests with a fake disk undo.
/// </summary>
public static class ReviewUndoFlow
{
    public readonly record struct DiskUndoResult(bool Ok, string Message);

    /// <summary>
    /// Undo the last Review decision. <paramref name="undoBatch"/> receives the
    /// Reject move batch id and must refuse when that batch is not newest.
    /// Session rolls back only when disk undo succeeds (Keep/Skip always succeed).
    /// </summary>
    public static async Task<(bool RolledBack, ReviewHistoryEntry? Entry, string? Message)> TryUndoAsync(
        ReviewSession session,
        Func<string, Task<DiskUndoResult>> undoBatch)
    {
        if (!session.TryPeekUndo(out var entry))
        {
            return (false, null, null);
        }

        if (entry.Decision == ReviewDecision.Reject)
        {
            if (string.IsNullOrWhiteSpace(entry.MoveBatchId))
            {
                return (false, entry, "Can't undo this reject — missing move journal.");
            }

            var disk = await undoBatch(entry.MoveBatchId).ConfigureAwait(false);
            if (!disk.Ok)
            {
                // Session stays on the post-reject position.
                return (false, entry, disk.Message);
            }
        }

        if (!session.TryCommitUndo())
        {
            return (false, entry, "Couldn't roll back the review step.");
        }

        return (true, entry, null);
    }
}
