using Palace.Models;

namespace Palace.Helpers;

/// <summary>Pure display rules for assigned-tag chip chrome (opacity + tooltip).</summary>
public static class AssignedTagChrome
{
    /// <summary>
    /// Implied-only chips use full opacity — transparency comes from the outline style,
    /// not a dimmed Opacity (T8 mockup). Partial Manual stays lightly muted.
    /// </summary>
    public static double Opacity(bool isPartial, TagSource source) =>
        source == TagSource.Implied ? 1.0 : isPartial ? 0.75 : 1.0;

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

    /// <summary>T8 chip tooltip: nearest imply / also-implied / aliases / partial / remove.</summary>
    public static string ChipTooltip(
        TagSource source,
        bool isAlsoImplied,
        string? nearestImpliedByName,
        IEnumerable<string>? aliasNames,
        bool isPartial,
        string? countLabel,
        string? tagName)
    {
        if (source == TagSource.Implied)
        {
            // No context menu yet — do not promise right-click edit.
            return ImpliedNearest.ImpliedTooltip(nearestImpliedByName);
        }

        var parts = new List<string>();
        if (isAlsoImplied)
        {
            parts.Add(ImpliedNearest.AlsoImpliedTooltip(nearestImpliedByName));
        }
        else if (isPartial && !string.IsNullOrWhiteSpace(countLabel))
        {
            // Keep T2 partial copy on the chip face tooltip.
            parts.Add(TagChipVisual.PartialTooltip(
                ParsePartialCount(countLabel),
                ParsePartialTotal(countLabel)));
        }
        else
        {
            var label = string.IsNullOrWhiteSpace(tagName) ? "tag" : tagName.Trim();
            parts.Add($"Remove {label}");
        }

        var also = ImpliedNearest.AliasAlsoLine(aliasNames);
        if (also.Length > 0)
        {
            parts.Add(also);
        }

        return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static int ParsePartialCount(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return 0;
        }

        var slash = label.IndexOf('/', StringComparison.Ordinal);
        return slash > 0 && int.TryParse(label.AsSpan(0, slash), out var n) ? n : 0;
    }

    private static int ParsePartialTotal(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return 0;
        }

        var slash = label.IndexOf('/', StringComparison.Ordinal);
        return slash >= 0
            && slash + 1 < label.Length
            && int.TryParse(label.AsSpan(slash + 1), out var n)
            ? n
            : 0;
    }
}
