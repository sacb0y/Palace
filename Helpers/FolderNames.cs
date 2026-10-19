namespace Palace.Helpers;

/// <summary>
/// Folder-name rules for New folder / Rename (Windows naming rules, checked on every OS so tests
/// match the app), plus unique "New folder (2)" style names. Pure: no disk access.
/// </summary>
public static class FolderNames
{
    public const string DefaultNewFolder = "New folder";
    public const int MaxLength = 200;

    private static readonly char[] Illegal = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public const string IllegalCharsMessage = "Folder names can't contain \\ / : * ? \" < > |";

    /// <summary>Trim and check a user-typed folder name. <paramref name="error"/> is a user-facing sentence.</summary>
    public static bool TryValidate(string? name, out string cleaned, out string error)
    {
        cleaned = (name ?? "").Trim();
        error = "";
        if (cleaned.Length == 0)
        {
            error = "Enter a folder name.";
            return false;
        }

        if (cleaned.IndexOfAny(Illegal) >= 0 || cleaned.Any(ch => ch < 32))
        {
            error = IllegalCharsMessage;
            return false;
        }

        if (cleaned is "." or "..")
        {
            error = "That name isn't allowed.";
            return false;
        }

        if (cleaned.EndsWith('.'))
        {
            error = "Folder names can't end with a period.";
            return false;
        }

        var stem = cleaned.Split('.')[0].TrimEnd();
        if (Reserved.Contains(stem))
        {
            error = $"\"{cleaned}\" is reserved by Windows.";
            return false;
        }

        if (cleaned.Length > MaxLength)
        {
            error = $"Folder names can be at most {MaxLength} characters.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// First free child name under <paramref name="parent"/>: <c>New folder</c>, <c>New folder (2)</c>, …
    /// <paramref name="exists"/> receives full paths.
    /// </summary>
    public static string UniqueChild(string parent, string baseName, Func<string, bool> exists)
    {
        var candidate = baseName;
        var n = 2;
        while (exists(Path.Combine(parent, candidate)))
        {
            candidate = $"{baseName} ({n})";
            n++;
        }

        return candidate;
    }

    /// <summary>
    /// Free file path in <paramref name="directory"/> for <paramref name="fileName"/>: the name itself,
    /// else <c>name (2).ext</c>, <c>name (3).ext</c>, … <paramref name="taken"/> receives full paths.
    /// </summary>
    public static string UniqueFilePath(string directory, string fileName, Func<string, bool> taken)
    {
        var desired = Path.Combine(directory, fileName);
        if (!taken(desired))
        {
            return desired;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({n}){ext}");
            if (!taken(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Same folder, case-insensitive (Windows), ignoring a trailing separator.</summary>
    public static bool SamePath(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return false;
        }

        return string.Equals(Trim(a), Trim(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><paramref name="path"/> is <paramref name="root"/> or strictly inside it.</summary>
    public static bool IsSameOrUnder(string path, string root)
    {
        var p = Trim(path);
        var r = Trim(root);
        return string.Equals(p, r, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Trim(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
