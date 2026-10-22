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
    /// Final toast after a batch Reject. Always surfaces already-rejected and
    /// failed counts — never silently drops them.
    /// </summary>
    public static string ToastMessage(int moved, int alreadyRejected, int failed)
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
            return moved == 1 ? "Rejected 1 file." : $"Rejected {moved} files.";
        }

        var parts = new List<string>
        {
            attempted == moved
                ? (moved == 1 ? "Rejected 1 file" : $"Rejected {moved} files")
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
}
