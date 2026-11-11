namespace Palace.Helpers;

/// <summary>
/// T2-04b Remove tags… anchored flyout: filter, select-all, most-used order, usage bars.
/// WinUI-free so Palace.Tests can cover the rules without WinRT.
/// </summary>
public static class TagRemoveFlyout
{
    public const string FilterPlaceholder = "Filter tags";

    public const string SelectAllLabel = "Select all";

    public const string SortMostUsedLabel = "Sort: most used";

    /// <summary>Usage bar track width in DIP (mockup t2-04b).</summary>
    public const double UsageBarWidthDip = 60;

    /// <summary>Most used first, then A–Z — default list order for the flyout.</summary>
    public static IReadOnlyList<T> OrderMostUsed<T>(
        IEnumerable<T>? rows,
        Func<T, int> appliedCount,
        Func<T, string?> name)
    {
        if (rows is null)
        {
            return [];
        }

        return rows
            .OrderByDescending(appliedCount)
            .ThenBy(r => name(r) ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Case-insensitive substring filter on the tag name; empty query keeps all.</summary>
    public static IReadOnlyList<T> FilterByName<T>(
        IEnumerable<T>? rows,
        string? query,
        Func<T, string?> name)
    {
        if (rows is null)
        {
            return [];
        }

        var q = (query ?? "").Trim();
        if (q.Length == 0)
        {
            return rows.ToList();
        }

        return rows
            .Where(r => (name(r) ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>true = all checked, false = none, null = mixed (Select all checkbox).</summary>
    public static bool? SelectAllState(IEnumerable<bool>? checkedFlags)
    {
        if (checkedFlags is null)
        {
            return false;
        }

        var any = false;
        var all = true;
        var saw = false;
        foreach (var isChecked in checkedFlags)
        {
            saw = true;
            if (isChecked)
            {
                any = true;
            }
            else
            {
                all = false;
            }
        }

        if (!saw)
        {
            return false;
        }

        if (all)
        {
            return true;
        }

        if (!any)
        {
            return false;
        }

        return null;
    }

    /// <summary>
    /// Mockup note listing implied names when present; otherwise the generic DialogImpliedNote.
    /// </summary>
    public static string ImpliedNote(IEnumerable<string?>? impliedNames)
    {
        var names = (impliedNames ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
        {
            return TagRemoveConfirm.DialogImpliedNote;
        }

        return $"Implied tags ({string.Join(", ", names)}) follow their rules, so they aren't listed here.";
    }

    public static double UsageBarFillWidth(double prevalence, double trackWidth = UsageBarWidthDip) =>
        Math.Clamp(prevalence, 0, 1) * Math.Max(0, trackWidth);

    public static int CheckedTagCount(IEnumerable<bool>? checkedFlags) =>
        checkedFlags?.Count(c => c) ?? 0;

    public static int CheckedAssignmentCount(
        IEnumerable<(bool IsChecked, int AppliedCount)>? rows)
    {
        if (rows is null)
        {
            return 0;
        }

        var sum = 0;
        foreach (var (isChecked, applied) in rows)
        {
            if (isChecked)
            {
                sum += Math.Max(0, applied);
            }
        }

        return sum;
    }
}
