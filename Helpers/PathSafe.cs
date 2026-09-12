using System.Text;
using System.Text.RegularExpressions;

namespace Palace.Helpers;

public static class PathSafe
{
    private static readonly HashSet<char> Illegal =
        ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public static string Slug(string name)
    {
        var cleaned = StripIllegal(name).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "untitled" : cleaned;
    }

    public static string StripIllegal(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var buffer = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch < 32 || Illegal.Contains(ch))
            {
                continue;
            }

            buffer.Append(ch);
        }

        return buffer.ToString().Trim();
    }

    public static string UniquePath(string desiredPath)
    {
        if (!CloudFile.Exists(desiredPath) && !Directory.Exists(desiredPath))
        {
            return desiredPath;
        }

        var dir = System.IO.Path.GetDirectoryName(desiredPath) ?? "";
        var name = System.IO.Path.GetFileNameWithoutExtension(desiredPath);
        var ext = System.IO.Path.GetExtension(desiredPath);
        var n = 2;
        while (true)
        {
            var candidate = System.IO.Path.Combine(dir, $"{name} ({n}){ext}");
            if (!CloudFile.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }

            n++;
        }
    }

    public static bool IsUnderRoot(string path, string root)
    {
        var full = System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var fullRoot = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        return full.StartsWith(fullRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(full, fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static readonly HashSet<string> ImageExt =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff" };

    public static readonly HashSet<string> GifExt =
        new(StringComparer.OrdinalIgnoreCase) { ".gif" };

    public static readonly HashSet<string> VideoExt =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".mkv", ".webm", ".avi" };

    public static bool IsCatalogExt(string ext) =>
        ImageExt.Contains(ext) || GifExt.Contains(ext) || VideoExt.Contains(ext);

    public static Models.AssetKind KindFromExt(string ext)
    {
        if (GifExt.Contains(ext))
        {
            return Models.AssetKind.Gif;
        }

        if (VideoExt.Contains(ext))
        {
            return Models.AssetKind.Video;
        }

        if (ImageExt.Contains(ext))
        {
            return Models.AssetKind.Image;
        }

        return Models.AssetKind.Other;
    }

    public static string CollapseDashes(string name)
    {
        var collapsed = Regex.Replace(name, "-{2,}", "-");
        return collapsed.Trim('-', ' ', ',');
    }
}
