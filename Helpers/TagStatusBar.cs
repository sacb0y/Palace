namespace Palace.Helpers;

/// <summary>
/// T2 confirmations live in the status bar only (✓ + message + Undo / Ctrl+Z). No toast.
/// Wording uses "files" (not "images" / "items") per Gooey + Isiac.
/// </summary>
public static class TagStatusBar
{
    public const string CheckMark = "✓";

    public static string WithCheck(string message)
    {
        var text = (message ?? "").Trim();
        if (text.Length == 0)
        {
            return CheckMark;
        }

        if (text.StartsWith(CheckMark, StringComparison.Ordinal))
        {
            return text;
        }

        return CheckMark + " " + text;
    }

    public static string Tagged(
        int fileCount,
        IReadOnlyList<string> tagNames,
        int createdCount = 0)
    {
        var names = FormatNames(tagNames);
        var core = fileCount <= 0
            ? $"Tagged with {names}"
            : $"Tagged {fileCount} file{(fileCount == 1 ? "" : "s")} with {names}";
        if (createdCount > 0)
        {
            core += $" · created {createdCount} tag{(createdCount == 1 ? "" : "s")}";
        }

        return WithCheck(core);
    }

    public static string Removed(IReadOnlyList<string> tagNames, int fileCount)
    {
        var names = FormatNames(tagNames);
        var core = fileCount <= 0
            ? $"Removed {names}"
            : $"Removed {names} from {fileCount} file{(fileCount == 1 ? "" : "s")}";
        return WithCheck(core);
    }

    public static string CreatedInGroup(int createdCount, string? groupName, IReadOnlyList<string> tagNames)
    {
        var names = FormatNames(tagNames);
        var group = string.IsNullOrWhiteSpace(groupName) ? "Ungrouped" : groupName.Trim();
        return WithCheck(
            $"Created {createdCount} tag{(createdCount == 1 ? "" : "s")} in {group}: {names}");
    }

    /// <summary>pt-01b: Accept all known prompt chips on one file.</summary>
    public static string AddedFromPrompt(int tagCount) =>
        WithCheck(
            tagCount == 1
                ? "Added 1 tag from prompt"
                : $"Added {tagCount} tags from prompt");

    /// <summary>pt-02: one chip (or Accept all) applied only to matching files.</summary>
    public static string AddedFromPrompts(string? tagName, int fileCount, int tagCount = 1)
    {
        if (tagCount > 1)
        {
            return WithCheck(
                $"Added {tagCount} tags from prompts");
        }

        var name = string.IsNullOrWhiteSpace(tagName) ? "tag" : tagName.Trim();
        if (fileCount <= 1)
        {
            return WithCheck($"Added {name} from prompt");
        }

        return WithCheck($"Added {name} to {fileCount} files from prompts");
    }

    private static string FormatNames(IReadOnlyList<string> tagNames)
    {
        if (tagNames is null || tagNames.Count == 0)
        {
            return "tags";
        }

        return string.Join(", ", tagNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()));
    }
}
