using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// Batch Reject (select-mode ContextFlyout) copy and confirm rules. Off WinUI
/// so Palace.Tests can cover multi-folder / partial-failure toasts without the VM.
/// </summary>
public static class BatchReject
{
    /// <summary>Default undo toast when Reject spans more than one destination folder.</summary>
    public const int MultiFolderUndoSeconds = 12;

    /// <summary>Short undo window for a single-folder Reject (matches Review).</summary>
    public const int SingleFolderUndoSeconds = 5;

    public static int UndoToastSeconds(int destinationFolderCount) =>
        destinationFolderCount > 1 ? MultiFolderUndoSeconds : SingleFolderUndoSeconds;

    /// <summary>
    /// Journal / status summary: <c>Reject N files across K folders</c> when
    /// more than one destination; otherwise <c>Reject N files</c>.
    /// </summary>
    public static string JournalSummary(int fileCount, int folderCount)
    {
        if (fileCount <= 0)
        {
            return "Reject 0 files";
        }

        var files = fileCount == 1 ? "1 file" : $"{fileCount} files";
        return folderCount > 1
            ? $"Reject {files} across {folderCount} folders"
            : $"Reject {files}";
    }

    /// <summary>
    /// Final toast after a batch Reject. Always surfaces already-rejected and
    /// failed counts — never silently drops them. Clean multi-folder success
    /// uses <see cref="JournalSummary"/> wording.
    /// </summary>
    public static string ToastMessage(
        int moved,
        int alreadyRejected,
        int failed,
        int folderCount = 1)
    {
        var attempted = moved + alreadyRejected + failed;
        if (moved == 0 && alreadyRejected == 0 && failed == 0)
        {
            return "Nothing rejected.";
        }

        if (moved == 0 && failed == 0 && alreadyRejected > 0)
        {
            return alreadyRejected == 1
                ? "Already in _rejected."
                : $"All {alreadyRejected} already in _rejected.";
        }

        if (failed == 0 && alreadyRejected == 0)
        {
            return JournalSummary(moved, folderCount).Replace("Reject ", "Rejected ", StringComparison.Ordinal) + ".";
        }

        var parts = new List<string>
        {
            attempted == moved
                ? JournalSummary(moved, folderCount).Replace("Reject ", "Rejected ", StringComparison.Ordinal)
                : $"Rejected {moved} of {attempted}"
        };
        if (alreadyRejected > 0)
        {
            parts.Add(alreadyRejected == 1
                ? "1 already in _rejected"
                : $"{alreadyRejected} already in _rejected");
        }

        if (failed > 0)
        {
            parts.Add(failed == 1 ? "1 failed" : $"{failed} failed");
        }

        return string.Join("; ", parts) + ".";
    }

    /// <summary>
    /// Confirm body when <see cref="SelectMode.NeedsBatchConfirm"/> is true.
    /// Cancel is the default button on the dialog.
    /// </summary>
    public static string ConfirmBody(int selectedCount, int collapsedSelectedCount)
    {
        var summary = SelectMode.Summary(true, selectedCount, collapsedSelectedCount);
        return collapsedSelectedCount > 0
            ? $"Reject {selectedCount} file(s) into _rejected?\n{summary}."
            : $"Reject {selectedCount} file(s) into _rejected?";
    }

    /// <summary>
    /// True when every undoable batch newer than the prior Move is only a
    /// <see cref="FileOpKind.CreateFolder"/> for <paramref name="destinationDir"/>.
    /// Otherwise the prior must stay on the stack (multi-ID undo).
    /// </summary>
    public static bool NewerBatchesAllowAbsorbPrior(
        IEnumerable<FileOpBatch> newerUndoableNewestFirst,
        string destinationDir)
    {
        if (string.IsNullOrWhiteSpace(destinationDir))
        {
            return false;
        }

        foreach (var batch in newerUndoableNewestFirst)
        {
            if (batch.Kind != FileOpKind.CreateFolder || batch.Items.Count == 0)
            {
                return false;
            }

            foreach (var item in batch.Items)
            {
                if (item.Step != FileOpStep.CreateDir
                    || !FolderNames.SamePath(item.NewPath, destinationDir))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Batches strictly newer than <paramref name="priorBatchId"/> from a
    /// newest-first undoable list. Empty when the prior is missing (unsafe).
    /// </summary>
    public static IReadOnlyList<FileOpBatch> NewerThanPrior(
        IReadOnlyList<FileOpBatch> undoableNewestFirst,
        string priorBatchId)
    {
        if (string.IsNullOrWhiteSpace(priorBatchId) || undoableNewestFirst.Count == 0)
        {
            return [];
        }

        var newer = new List<FileOpBatch>();
        var found = false;
        foreach (var batch in undoableNewestFirst)
        {
            if (string.Equals(batch.Id, priorBatchId, StringComparison.Ordinal))
            {
                found = true;
                break;
            }

            newer.Add(batch);
        }

        return found ? newer : [];
    }
}
