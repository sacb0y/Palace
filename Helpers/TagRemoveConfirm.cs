namespace Palace.Helpers;

/// <summary>
/// T2 bulk remove: × / Remove from all act at once when ≤10 files are affected;
/// above that, ask first (chip flyout or dialog). Primary is always Remove; Cancel is default.
/// </summary>
public static class TagRemoveConfirm
{
    public const int Threshold = 10;

    public const string Primary = "Remove";

    public const string Cancel = "Cancel";

    public const string RemoveTagsLink = "Remove tags…";

    public static bool NeedsConfirm(int affectedFileCount) =>
        affectedFileCount > Threshold;

    public static string ChipFlyoutTitle(string tagName, int fileCount) =>
        $"Remove {Trim(tagName)} from {fileCount} files?";

    public static string ChipFlyoutBody { get; } =
        "More than 10 files, so Palace asks first. You can still undo it afterwards.";

    public static string DialogTitle(int fileCount) =>
        $"Remove tags from {fileCount} selected";

    public static string DialogHint { get; } =
        "Tick the tags to take off every selected file.";

    public static string DialogImpliedNote { get; } =
        "Implied tags follow their rules, so they aren't listed here.";

    public static string AssignmentSummary(int assignmentCount) =>
        assignmentCount == 1
            ? "1 assignment · one Undo"
            : $"{assignmentCount} assignments · one Undo";

    public static string RemoveButtonLabel(int tagCount) =>
        tagCount <= 1 ? "Remove 1 tag" : $"Remove {tagCount} tags";

    public static string SelectedBarLabel(int tagCount, int fileCount) =>
        $"{tagCount} tag{(tagCount == 1 ? "" : "s")} · {fileCount} file{(fileCount == 1 ? "" : "s")}";

    public static string RemoveFromAllLabel { get; } = "Remove from all";

    /// <summary>
    /// Affected file count for a chip ×: partial uses the k in k/n; otherwise the selection size.
    /// </summary>
    public static int AffectedFileCount(bool isPartial, int partialCount, int selectionTotal)
    {
        if (isPartial && partialCount > 0)
        {
            return partialCount;
        }

        return Math.Max(0, selectionTotal);
    }

    private static string Trim(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "tag" : name.Trim();
}
