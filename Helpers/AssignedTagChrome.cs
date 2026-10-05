using Palace.Models;

namespace Palace.Helpers;

/// <summary>Pure display rules for assigned-tag chip chrome (opacity + tooltip).</summary>
public static class AssignedTagChrome
{
    /// <summary>Implied tags are slightly transparent; Manual stays full; partial Manual is lightly muted.</summary>
    public static double Opacity(bool isPartial, TagSource source) =>
        source == TagSource.Implied ? 0.62 : isPartial ? 0.75 : 1.0;

    /// <summary>Manual / Implied / Mixed (+ optional k/n) for ToolTipService — not chip face text.</summary>
    public static string Tooltip(string? sourceLabel, string? countLabel)
    {
        var source = sourceLabel?.Trim() ?? "";
        var count = countLabel?.Trim() ?? "";
        if (source.Length == 0)
        {
            return count;
        }

        return count.Length == 0 ? source : $"{source} ({count})";
    }
}
