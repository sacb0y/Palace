namespace Palace.Helpers;

/// <summary>
/// Middle-ellipsis for long file names and paths, keeping the extension visible.
/// Off WinUI for tests.
/// </summary>
public static class MiddleEllipsis
{
    public const int DefaultMaxChars = 42;

    /// <summary>
    /// Truncates <paramref name="text"/> to at most <paramref name="maxChars"/>,
    /// inserting "…" in the middle. When the text looks like a file name / path,
    /// the extension (last <c>.</c> segment of the leaf) stays at the end.
    /// </summary>
    public static string Truncate(string? text, int maxChars = DefaultMaxChars)
    {
        if (string.IsNullOrEmpty(text) || maxChars < 4)
        {
            return text ?? "";
        }

        var value = text.Trim();
        if (value.Length <= maxChars)
        {
            return value;
        }

        var leaf = value;
        var dirPrefix = "";
        var slash = Math.Max(value.LastIndexOf('\\'), value.LastIndexOf('/'));
        if (slash >= 0 && slash < value.Length - 1)
        {
            dirPrefix = value[..(slash + 1)];
            leaf = value[(slash + 1)..];
        }

        var dot = leaf.LastIndexOf('.');
        var stem = dot > 0 ? leaf[..dot] : leaf;
        var ext = dot > 0 ? leaf[dot..] : "";

        // Prefer keeping extension; budget the rest for stem (+ optional dir hint).
        var extBudget = Math.Min(ext.Length, maxChars - 3);
        if (extBudget < 0)
        {
            extBudget = 0;
        }

        var remaining = maxChars - 1 - extBudget; // 1 for ellipsis
        if (remaining < 2)
        {
            return "…" + (extBudget > 0 ? ext[^extBudget..] : "");
        }

        // Show a short head of the full string (dir or stem) + ellipsis + tail of stem + ext.
        var headLen = Math.Max(1, remaining / 2);
        var tailStem = Math.Max(0, remaining - headLen);
        var headSource = dirPrefix.Length > 0 ? value : stem;
        if (headSource.Length < headLen)
        {
            headLen = headSource.Length;
            tailStem = remaining - headLen;
        }

        var head = headSource[..headLen];
        var stemTail = tailStem > 0 && stem.Length > 0
            ? stem[^Math.Min(tailStem, stem.Length)..]
            : "";
        var keptExt = extBudget > 0 ? ext[^extBudget..] : "";
        return head + "…" + stemTail + keptExt;
    }
}
