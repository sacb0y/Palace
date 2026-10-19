using System.Text.RegularExpressions;
using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// WinUI-free organize templates. Empty tokens fall back to the original file stem,
/// and separator-only pieces are dropped so a name is never just <c>.ext</c>.
/// </summary>
public static class OrganizeNames
{
    private static readonly Regex Token = new(@"\{([^}]+)\}|%([^%]+)%", RegexOptions.Compiled);

    /// <summary>
    /// "Stay in the current source folders" uses the watched source root.
    /// "Move to a destination folder" uses the picked destination.
    /// The template folder is under that root, not under the file's current subdirectory.
    /// </summary>
    public static string? Root(DestinationPolicy policy, string? destinationRoot, string? sourceFolderPath) =>
        policy == DestinationPolicy.Destination ? destinationRoot : sourceFolderPath;

    public static string RenderFolder(string template, Asset asset, IReadOnlyList<AssignedTag> tags)
    {
        var rendered = ReplaceTokens(template, asset, tags, forFolder: true);
        var parts = rendered.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Select(PathSafe.StripIllegal)
            .Select(PathSafe.TidyNameSeparators)
            .Where(p => p.Length > 0);
        return Path.Combine(parts.ToArray());
    }

    public static string RenderFile(string template, Asset asset, IReadOnlyList<AssignedTag> tags)
    {
        var ext = FileExtension(asset);
        var rendered = PathSafe.StripIllegal(ReplaceTokens(template, asset, tags, forFolder: false));
        var stem = WithoutExtension(rendered, ext);
        stem = PathSafe.TidyNameSeparators(stem);
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = PathSafe.TidyNameSeparators(Path.GetFileNameWithoutExtension(asset.FileName));
        }

        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = PathSafe.TidyNameSeparators(Path.GetFileNameWithoutExtension(asset.Path));
        }

        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "untitled";
        }

        return string.IsNullOrEmpty(ext) ? stem : stem + ext;
    }

    private static string FileExtension(Asset asset)
    {
        var ext = Path.GetExtension(asset.FileName);
        return string.IsNullOrEmpty(ext) ? Path.GetExtension(asset.Path) : ext;
    }

    private static string WithoutExtension(string rendered, string ext)
    {
        if (!string.IsNullOrEmpty(ext) && rendered.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        {
            return rendered[..^ext.Length];
        }

        return rendered;
    }

    private static string ReplaceTokens(string template, Asset asset, IReadOnlyList<AssignedTag> tags, bool forFolder)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return Token.Replace(template, match =>
        {
            var raw = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            var value = ResolveToken(raw, asset, tags, used);
            if (forFolder && string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            return value ?? "";
        });
    }

    private static string? ResolveToken(string raw, Asset asset, IReadOnlyList<AssignedTag> tags, HashSet<string> used)
    {
        var key = raw.Trim();
        if (key.Equals("original", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(asset.FileName);
        }

        if (key.Equals("ext", StringComparison.OrdinalIgnoreCase))
        {
            var ext = Path.GetExtension(asset.FileName);
            return ext.StartsWith('.') ? ext[1..] : ext;
        }

        if (key.Equals("model", StringComparison.OrdinalIgnoreCase))
        {
            return PathSafe.StripIllegal(asset.Model ?? "");
        }

        if (key.Equals("seed", StringComparison.OrdinalIgnoreCase))
        {
            return asset.Seed ?? "";
        }

        if (key.StartsWith("tags", StringComparison.OrdinalIgnoreCase))
        {
            var take = int.MaxValue;
            var colon = key.IndexOf(':');
            if (colon >= 0 && int.TryParse(key[(colon + 1)..], out var n))
            {
                take = n;
            }

            var remaining = tags.Where(t => !used.Contains(t.TagId)).Take(take).ToList();
            foreach (var t in remaining)
            {
                used.Add(t.TagId);
            }

            return string.Join(", ", remaining.Select(t => t.TagName));
        }

        if (key.StartsWith("top:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(key[4..], out var topN))
        {
            var top = tags.Take(topN).ToList();
            foreach (var t in top)
            {
                used.Add(t.TagId);
            }

            return string.Join("/", top.Select(t => t.Slug));
        }

        if (key.StartsWith("facet:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
        {
            var groupName = key[(key.IndexOf(':') + 1)..];
            var hit = tags.FirstOrDefault(t =>
                t.ParentNames.Any(p => p.Equals(groupName, StringComparison.OrdinalIgnoreCase)));
            return hit?.TagName ?? "";
        }

        var byGroup = tags.FirstOrDefault(t =>
            t.ParentNames.Any(p => p.Equals(key, StringComparison.OrdinalIgnoreCase)));
        if (byGroup is not null)
        {
            used.Add(byGroup.TagId);
            return byGroup.TagName;
        }

        var byTag = tags.FirstOrDefault(t => t.TagName.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (byTag is not null)
        {
            used.Add(byTag.TagId);
            return byTag.TagName;
        }

        return "";
    }
}
