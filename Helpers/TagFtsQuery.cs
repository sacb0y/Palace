namespace Palace.Helpers;

/// <summary>
/// FTS5 MATCH helpers: quote/escape terms so <c>-</c> <c>:</c> <c>+</c> <c>(</c> and
/// keywords like <c>NOT</c> stay literal, and expand multi-word alias names.
/// </summary>
public static class TagFtsQuery
{
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
    /// Build an AND of prefix terms. When a contiguous token span matches an alias
    /// tag name (via <paramref name="idByName"/>), that span becomes one clause that
    /// ORs the ideal name so rewrite-on-assign rows still hit.
    /// </summary>
    public static string Build(
        string query,
        IReadOnlyDictionary<string, string> aliasToIdeal,
        IReadOnlyDictionary<string, string> idByName,
        IReadOnlyDictionary<string, string> nameById)
    {
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
            var term = PrefixTerm(spanText);
            if (aliasToIdeal.Count == 0)
            {
                clauses.Add(term);
                i += length;
                continue;
            }

            var idealId = TagAliasResolve.Ideal(tagId, aliasToIdeal);
            if (string.Equals(idealId, tagId, StringComparison.Ordinal)
                || !nameById.TryGetValue(idealId, out var idealName)
                || string.Equals(idealName, spanText, StringComparison.OrdinalIgnoreCase))
            {
                clauses.Add(term);
            }
            else
            {
                clauses.Add($"({term} OR {PrefixTerm(idealName)})");
            }

            i += length;
        }

        return string.Join(" AND ", clauses);
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
