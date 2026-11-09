using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// Which tag rows an assign or remove actually changed. Undo journals only those
/// rows, so a partial fill does not strip a tag the asset already had.
/// Source-only upgrades (e.g. Prompt→Manual) journal too so Undo can restore.
/// </summary>
public static class TagAssignUndo
{
    /// <summary>Leading journal token: the named tag was already in the desired state.</summary>
    public const string KeepAssigned = "*";

    /// <param name="PriorAssignedSource">
    /// Applied (non-<see cref="TagSource.Implied"/>) source on the assigned tag
    /// before this mutation when <see cref="TouchAssignedTag"/> is false — Undo
    /// restores it. Null when the assigned tag was newly written (or upgraded from
    /// stored Implied, which Undo deletes and never rematerializes).
    /// </param>
    public readonly record struct Change(
        bool Journal,
        bool TouchAssignedTag,
        IReadOnlyList<string> ImpliedIds,
        TagSource? PriorAssignedSource = null);

    public static IReadOnlyList<string> ImpliedAdded(
        IEnumerable<string> presentBefore,
        IEnumerable<string> presentAfter,
        string assignedTagId)
    {
        var before = presentBefore as IReadOnlySet<string>
            ?? presentBefore.ToHashSet(StringComparer.Ordinal);
        var added = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in presentAfter)
        {
            if (string.IsNullOrEmpty(id)
                || string.Equals(id, assignedTagId, StringComparison.Ordinal)
                || before.Contains(id)
                || !seen.Add(id))
            {
                continue;
            }

            added.Add(id);
        }

        return added;
    }

    /// <summary>
    /// ID-only overload for tests / callers without sources. Treats every present
    /// id as <see cref="TagSource.Manual"/> (no source-only detection).
    /// </summary>
    public static Change PlanAssign(
        IEnumerable<string> presentBefore,
        IEnumerable<string> presentAfter,
        string assignedTagId) =>
        PlanAssign(
            presentBefore.Select(id => (id, TagSource.Manual)),
            presentAfter.Select(id => (id, TagSource.Manual)),
            assignedTagId);

    /// <summary>
    /// Journal this asset when the assigned tag was newly written, its applied
    /// <see cref="TagSource"/> changed, or an implied tag was newly written.
    /// <see cref="Change.TouchAssignedTag"/> is true when Undo should delete the
    /// assigned tag (new apply, or stored Implied → applied). False (KeepAssigned)
    /// when the asset already had an applied row — Undo restores
    /// <see cref="Change.PriorAssignedSource"/> and only strips new implied ids.
    /// </summary>
    public static Change PlanAssign(
        IEnumerable<(string Id, TagSource Source)> presentBefore,
        IEnumerable<(string Id, TagSource Source)> presentAfter,
        string assignedTagId)
    {
        var before = new Dictionary<string, TagSource>(StringComparer.Ordinal);
        foreach (var row in presentBefore)
        {
            if (string.IsNullOrEmpty(row.Id) || before.ContainsKey(row.Id))
            {
                continue;
            }

            before[row.Id] = row.Source;
        }

        TagSource? afterAssigned = null;
        var afterIds = new List<string>();
        var seenAfter = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in presentAfter)
        {
            if (string.IsNullOrEmpty(row.Id) || !seenAfter.Add(row.Id))
            {
                continue;
            }

            afterIds.Add(row.Id);
            if (string.Equals(row.Id, assignedTagId, StringComparison.Ordinal))
            {
                afterAssigned = row.Source;
            }
        }

        before.TryGetValue(assignedTagId, out var priorRaw);
        TagSource? prior = before.ContainsKey(assignedTagId) ? priorRaw : null;

        // New applied row, or upgrading a leftover stored Implied → applied.
        // Undo deletes the assigned tag and never rematerializes Implied.
        var touch = !string.IsNullOrEmpty(assignedTagId)
            && afterAssigned is not null
            && afterAssigned != TagSource.Implied
            && (prior is null || prior == TagSource.Implied);

        // Applied → applied source change (Prompt→Manual, etc.).
        var sourceChanged = prior is not null
            && prior != TagSource.Implied
            && afterAssigned is not null
            && afterAssigned != TagSource.Implied
            && prior != afterAssigned;

        var implied = ImpliedAdded(before.Keys, afterIds, assignedTagId);
        var journal = touch || sourceChanged || implied.Count > 0;

        // KeepAssigned path needs the prior applied source so Undo can restore it
        // (no-op when source was unchanged and only implied ids were added).
        TagSource? priorForJournal = null;
        if (!touch && prior is not null && prior != TagSource.Implied)
        {
            priorForJournal = prior;
        }

        return new Change(journal, touch, implied, priorForJournal);
    }

    /// <summary>
    /// Journal a remove only when the tag was present or an implied tag was swept off.
    /// Implied ids are the ones that disappeared and were <see cref="TagSource.Implied"/>.
    /// </summary>
    public static Change PlanRemove(
        IEnumerable<(string Id, TagSource Source)> presentBefore,
        IEnumerable<(string Id, TagSource Source)> presentAfter,
        string removedTagId)
    {
        var beforeIds = new HashSet<string>(StringComparer.Ordinal);
        var beforeImplied = new List<string>();
        foreach (var row in presentBefore)
        {
            if (string.IsNullOrEmpty(row.Id) || !beforeIds.Add(row.Id))
            {
                continue;
            }

            if (row.Source == TagSource.Implied)
            {
                beforeImplied.Add(row.Id);
            }
        }

        var afterIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in presentAfter)
        {
            if (!string.IsNullOrEmpty(row.Id))
            {
                afterIds.Add(row.Id);
            }
        }

        var touch = !string.IsNullOrEmpty(removedTagId) && beforeIds.Contains(removedTagId);
        var implied = new List<string>();
        foreach (var id in beforeImplied)
        {
            if (string.Equals(id, removedTagId, StringComparison.Ordinal) || afterIds.Contains(id))
            {
                continue;
            }

            implied.Add(id);
        }

        return new Change(touch || implied.Count > 0, touch, implied);
    }

    /// <summary>
    /// Source on this asset's assignment row. A merged chip can show one source
    /// for a mixed selection; Undo must write each asset's own source back.
    /// <c>AssetTag</c> stores Source only (no implied-by link).
    /// </summary>
    public static TagSource RecordedSource(
        IEnumerable<(string Id, TagSource Source)> presentBefore,
        string tagId,
        TagSource fallback)
    {
        foreach (var row in presentBefore)
        {
            if (string.Equals(row.Id, tagId, StringComparison.Ordinal))
            {
                return row.Source;
            }
        }

        return fallback;
    }

    public static string Encode(IReadOnlyList<string> impliedIds, bool touchAssignedTag = true)
    {
        var body = string.Join('\n', impliedIds);
        if (touchAssignedTag)
        {
            return body;
        }

        return impliedIds.Count == 0 ? KeepAssigned : KeepAssigned + "\n" + body;
    }

    public static bool TouchesAssignedTag(string? encoded) =>
        string.IsNullOrEmpty(encoded)
        || !string.Equals(FirstToken(encoded), KeepAssigned, StringComparison.Ordinal);

    public static IReadOnlyList<string> Decode(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded))
        {
            return [];
        }

        var parts = encoded.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 0 && string.Equals(parts[0], KeepAssigned, StringComparison.Ordinal))
        {
            return parts.Length == 1 ? [] : parts[1..];
        }

        return parts;
    }

    private static string FirstToken(string encoded)
    {
        var cut = encoded.IndexOf('\n');
        return cut < 0 ? encoded : encoded[..cut];
    }
}
