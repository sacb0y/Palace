namespace Palace.Helpers;

/// <summary>
/// FTS5 MATCH helpers: quote/escape terms so <c>-</c> <c>:</c> <c>+</c> <c>(</c> and
/// keywords like <c>NOT</c> stay literal, and expand multi-word alias / implication names.
/// </summary>
public static class TagFtsQuery
{
    /// <summary>
    /// Cap on OR terms per matched parent (matched name + ideal + implying children).
    /// Keeps MATCH strings bounded when a parent has a huge implication fan-in.
    /// </summary>
    public const int MaxParentExpandOrTerms = 24;

    /// <summary>Alphanumeric runs only (same tokenization as catalog search).</summary>
    public static List<string> Tokenize(string query)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var ch in query)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(ch);
            }
            else if (current.Length > 0)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    /// <summary>
    /// Prefix term safe for FTS5 MATCH. Always double-quotes; doubles internal quotes.
    /// </summary>
    public static string PrefixTerm(string token)
    {
        var clean = (token ?? "").Replace("\"", "\"\"", StringComparison.Ordinal).Trim();
        return string.IsNullOrEmpty(clean) ? "\"\"" : $"\"{clean}\"*";
    }

    /// <summary>
    /// Build an AND of prefix terms. When a contiguous token span matches a tag name,
    /// the clause ORs the ideal (alias) and every tag that implies the matched tag
    /// (transitive), so applied-only FTS still finds wolf-only assets for <c>canine</c>.
    /// </summary>
    public static string Build(
        string query,
        IReadOnlyDictionary<string, string> aliasToIdeal,
        IReadOnlyDictionary<string, string> idByName,
        IReadOnlyDictionary<string, string> nameById,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? implyingIds = null) =>
        Build(query, aliasToIdeal, idByName, nameById, implyingIds, out _);

    /// <inheritdoc cref="Build(string,IReadOnlyDictionary{string,string},IReadOnlyDictionary{string,string},IReadOnlyDictionary{string,string},IReadOnlyDictionary{string,IReadOnlyList{string}}?)"/>
    public static string Build(
        string query,
        IReadOnlyDictionary<string, string> aliasToIdeal,
        IReadOnlyDictionary<string, string> idByName,
        IReadOnlyDictionary<string, string> nameById,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? implyingIds,
        out bool parentExpandTruncated)
    {
        parentExpandTruncated = false;
        var parts = Tokenize(query);
        if (parts.Count == 0)
        {
            return "\"\"";
        }

        // Pre-tokenize alias / ideal names for multi-word span matching.
        var nameTokensById = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (id, name) in nameById)
        {
            nameTokensById[id] = Tokenize(name);
        }

        var clauses = new List<string>();
        var i = 0;
        while (i < parts.Count)
        {
            var match = LongestNameMatch(parts, i, idByName, nameTokensById);
            if (match is null)
            {
                clauses.Add(PrefixTerm(parts[i]));
                i++;
                continue;
            }

            var (tagId, length, spanText) = match.Value;
            clauses.Add(BuildExpandedClause(
                tagId, spanText, aliasToIdeal, nameById, implyingIds, ref parentExpandTruncated));
            i += length;
        }

        return string.Join(" AND ", clauses);
    }

    private static string BuildExpandedClause(
        string tagId,
        string spanText,
        IReadOnlyDictionary<string, string> aliasToIdeal,
        IReadOnlyDictionary<string, string> nameById,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? implyingIds,
        ref bool truncated)
    {
        var orTerms = new List<string> { PrefixTerm(spanText) };
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { spanText };

        void AddName(string? name)
        {
            if (string.IsNullOrEmpty(name) || !seenNames.Add(name))
            {
                return;
            }

            orTerms.Add(PrefixTerm(name));
        }

        var idealId = aliasToIdeal.Count == 0
            ? tagId
            : TagAliasResolve.Ideal(tagId, aliasToIdeal);
        if (!string.Equals(idealId, tagId, StringComparison.Ordinal)
            && nameById.TryGetValue(idealId, out var idealName))
        {
            AddName(idealName);
        }

        if (implyingIds is not null && implyingIds.Count > 0)
        {
            var childNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            void CollectChildren(string rootId)
            {
                foreach (var childId in ImplyingClosure(rootId, implyingIds))
                {
                    if (nameById.TryGetValue(childId, out var childName))
                    {
                        childNames.Add(childName);
                    }
                }
            }

            CollectChildren(tagId);
            if (!string.Equals(idealId, tagId, StringComparison.Ordinal))
            {
                CollectChildren(idealId);
            }

            foreach (var childName in childNames)
            {
                if (orTerms.Count >= MaxParentExpandOrTerms)
                {
                    truncated = true;
                    break;
                }

                AddName(childName);
            }
        }

        if (orTerms.Count > MaxParentExpandOrTerms)
        {
            truncated = true;
            orTerms.RemoveRange(MaxParentExpandOrTerms, orTerms.Count - MaxParentExpandOrTerms);
        }

        return orTerms.Count == 1 ? orTerms[0] : $"({string.Join(" OR ", orTerms)})";
    }

    /// <summary>
    /// Tags that imply <paramref name="tagId"/> directly or transitively
    /// (<c>implyingIds</c> maps ImpliedTagId → TagIds that imply it).
    /// </summary>
    public static IEnumerable<string> ImplyingClosure(
        string tagId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> implyingIds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { tagId };
        var queue = new Queue<string>();
        queue.Enqueue(tagId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!implyingIds.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                if (!seen.Add(child))
                {
                    continue;
                }

                yield return child;
                queue.Enqueue(child);
            }
        }
    }

    /// <summary>
    /// Longest tag-name match starting at <paramref name="start"/>: prefers exact
    /// space-joined lookup, then equal token sequences against known tag names.
    /// </summary>
    public static (string TagId, int Length, string SpanText)? LongestNameMatch(
        IReadOnlyList<string> parts,
        int start,
        IReadOnlyDictionary<string, string> idByName,
        IReadOnlyDictionary<string, List<string>> nameTokensById)
    {
        (string TagId, int Length, string SpanText)? best = null;
        for (var length = parts.Count - start; length >= 1; length--)
        {
            var span = string.Join(' ', parts.Skip(start).Take(length));
            if (idByName.TryGetValue(span, out var byJoined))
            {
                return (byJoined, length, span);
            }

            foreach (var (id, tokens) in nameTokensById)
            {
                if (tokens.Count != length)
                {
                    continue;
                }

                var equal = true;
                for (var t = 0; t < length; t++)
                {
                    if (!string.Equals(parts[start + t], tokens[t], StringComparison.OrdinalIgnoreCase))
                    {
                        equal = false;
                        break;
                    }
                }

                if (!equal)
                {
                    continue;
                }

                // Prefer longer matches; first hit at this length wins.
                best = (id, length, span);
                break;
            }

            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }
}
