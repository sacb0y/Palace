using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// User-facing notice for what a scan left out: unsupported formats, files that could not be
/// decoded, files that failed to index, and folders that could not be opened. Null when clean.
/// </summary>
public static class ScanNotice
{
    private const int Examples = 3;

    /// <summary>OS / sync bookkeeping files that are never worth a "skipped" notice.</summary>
    public static bool IsIgnorable(string path, FileAttributes attributes)
    {
        if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
        {
            return true;
        }

        var name = Path.GetFileName(path);
        return name.StartsWith("._", StringComparison.Ordinal)
            || name.StartsWith("~$", StringComparison.Ordinal)
            || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
            || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".dropbox", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".dropbox.attr", StringComparison.OrdinalIgnoreCase);
    }

    public static string? Describe(ScanReport report)
    {
        var parts = new List<string>();
        if (report.Skipped > 0)
        {
            var exts = report.SkippedByExtension
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => (kv.Key.Length == 0 ? "no extension" : kv.Key) + (kv.Value > 1 ? $" \u00d7{kv.Value}" : ""))
                .ToList();
            parts.Add($"Skipped {Files(report.Skipped)} in formats Palace can't read yet ({List(exts)}).");
        }

        if (report.Undecodable > 0)
        {
            var names = report.UndecodablePaths.Select(Path.GetFileName).Select(n => n ?? "").ToList();
            parts.Add($"{Capital(Files(report.Undecodable))} couldn't be decoded and show a placeholder ({List(names, report.Undecodable)}).");
        }

        var fileErrors = report.Errors - report.InaccessibleFolders;
        if (fileErrors > 0)
        {
            var names = report.ErrorPaths
                .Select((p, i) => $"{Path.GetFileName(p)}: {Reason(report.ErrorReasons.ElementAtOrDefault(i))}")
                .ToList();
            parts.Add(names.Count > 0
                ? $"{Capital(Files(fileErrors))} couldn't be indexed ({List(names, fileErrors)})."
                : $"{Capital(Files(fileErrors))} couldn't be indexed.");
        }

        if (report.InaccessibleFolders > 0)
        {
            parts.Add(report.InaccessibleFolders == 1
                ? "1 folder couldn't be opened (no access or it went away)."
                : $"{report.InaccessibleFolders} folders couldn't be opened (no access or they went away).");
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static string Files(int n) => n == 1 ? "1 file" : $"{n} files";

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string Reason(string? message)
    {
        var text = (message ?? "").Trim();
        var newline = text.IndexOfAny(['\r', '\n']);
        if (newline >= 0)
        {
            text = text[..newline].TrimEnd();
        }

        text = text.TrimEnd('.');
        return text.Length > 80 ? text[..79] + "\u2026" : (text.Length == 0 ? "error" : text);
    }

    private static string List(IReadOnlyList<string> items, int? total = null)
    {
        var shown = items.Take(Examples).ToList();
        var more = (total ?? items.Count) - shown.Count;
        var text = string.Join(", ", shown);
        return more > 0 ? $"{text} and {more} more" : text;
    }
}
