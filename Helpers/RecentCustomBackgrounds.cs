using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Palace.Helpers;

/// <summary>
/// Custom-group MRU wallpapers (newest first, capped). Each entry keeps a
/// LocalFolder cache copy, optional source path, content hash, last-used, and
/// per-image darkness/blur. Off WinUI for Palace.Tests.
/// Removing from Recent never deletes the cached file (Undo InfoBar only).
/// </summary>
public static class RecentCustomBackgrounds
{
    /// <summary>Gooey: five recents; adding a 6th drops the oldest non-active.</summary>
    public const int Max = 5;

    public const string SettingsKey = "ShellRecentCustoms";
    public const string ActiveIdKey = "ShellRecentCustomId";
    public const string FilePrefix = "shell-recent-";
    public const int ThumbDecodeWidth = 224;
    public const int ThumbDecodeHeight = 126;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static IReadOnlyList<RecentCustomBackgroundEntry> Parse(object? stored)
    {
        if (stored is not string text || string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            var rows = JsonSerializer.Deserialize<List<RecentCustomDto>>(text, JsonOptions);
            if (rows is null || rows.Count == 0)
            {
                return [];
            }

            return Normalize(rows.Select(FromDto));
        }
        catch
        {
            return [];
        }
    }

    public static string Serialize(IEnumerable<RecentCustomBackgroundEntry> entries) =>
        JsonSerializer.Serialize(Normalize(entries).Select(ToDto).ToList(), JsonOptions);

    /// <summary>
    /// Insert <paramref name="entry"/> first. Same content hash → move existing to
    /// front (no duplicate). Cap at <see cref="Max"/> by dropping the oldest
    /// entry that is not <paramref name="activeId"/>.
    /// </summary>
    public static (IReadOnlyList<RecentCustomBackgroundEntry> List, IReadOnlyList<RecentCustomBackgroundEntry> Evicted)
        Push(
            IEnumerable<RecentCustomBackgroundEntry> existing,
            RecentCustomBackgroundEntry entry,
            string? activeId = null)
    {
        if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.FileName))
        {
            return (Normalize(existing), []);
        }

        var incoming = NormalizeEntry(entry);
        var list = new List<RecentCustomBackgroundEntry>();
        RecentCustomBackgroundEntry? matched = null;
        foreach (var row in Normalize(existing))
        {
            if (!string.IsNullOrWhiteSpace(incoming.ContentHash)
                && string.Equals(row.ContentHash, incoming.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                matched = row;
                continue;
            }

            if (string.Equals(row.Id, incoming.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.FileName, incoming.FileName, StringComparison.OrdinalIgnoreCase))
            {
                matched = row;
                continue;
            }

            list.Add(row);
        }

        // Same hash: keep the existing id/file so we do not orphan the prior cache;
        // refresh display name / source / amounts and move to front.
        var head = matched is null
            ? incoming
            : matched with
            {
                DisplayName = incoming.DisplayName,
                SourcePath = incoming.SourcePath ?? matched.SourcePath,
                Darkness = incoming.Darkness,
                Blur = incoming.Blur,
                ContentHash = string.IsNullOrWhiteSpace(incoming.ContentHash)
                    ? matched.ContentHash
                    : incoming.ContentHash,
                DateAddedUtc = incoming.DateAddedUtc,
                LastUsedUtc = incoming.LastUsedUtc
            };

        list.Insert(0, head);
        var evicted = new List<RecentCustomBackgroundEntry>();
        while (list.Count > Max)
        {
            var dropIndex = FindOldestNonActiveIndex(list, activeId);
            if (dropIndex < 0)
            {
                // Every slot is the active image — still enforce Max by dropping last.
                dropIndex = list.Count - 1;
            }

            evicted.Add(list[dropIndex]);
            list.RemoveAt(dropIndex);
        }

        return (list, evicted);
    }

    public static IReadOnlyList<RecentCustomBackgroundEntry> Remove(
        IEnumerable<RecentCustomBackgroundEntry> existing,
        string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Normalize(existing);
        }

        return Normalize(existing.Where(e => !string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)));
    }

    public static IReadOnlyList<RecentCustomBackgroundEntry> UpdateAmounts(
        IEnumerable<RecentCustomBackgroundEntry> existing,
        string? id,
        double darkness,
        double blur)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Normalize(existing);
        }

        var d = ShellBackground.ClampDarkness(darkness);
        var b = ShellBackground.NormalizeBlur(blur);
        return Normalize(existing.Select(e =>
            string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)
                ? e with { Darkness = d, Blur = b, LastUsedUtc = DateTimeOffset.UtcNow }
                : e));
    }

    public static IReadOnlyList<RecentCustomBackgroundEntry> TouchLastUsed(
        IEnumerable<RecentCustomBackgroundEntry> existing,
        string? id,
        DateTimeOffset? whenUtc = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Normalize(existing);
        }

        var stamp = whenUtc ?? DateTimeOffset.UtcNow;
        return Normalize(existing.Select(e =>
            string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)
                ? e with { LastUsedUtc = stamp }
                : e));
    }

    public static RecentCustomBackgroundEntry? FindById(
        IEnumerable<RecentCustomBackgroundEntry> existing,
        string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return existing.FirstOrDefault(e =>
            string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public static RecentCustomBackgroundEntry? FindByHash(
        IEnumerable<RecentCustomBackgroundEntry> existing,
        string? contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
        {
            return null;
        }

        return existing.FirstOrDefault(e =>
            string.Equals(e.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase));
    }

    public static RecentCustomBackgroundEntry? FindByCachedPath(
        IEnumerable<RecentCustomBackgroundEntry> existing,
        string? localFolder,
        string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return null;
        }

        var name = FileNameOnly(absolutePath);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var byName = existing.FirstOrDefault(e =>
            string.Equals(e.FileName, name, StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            return byName;
        }

        if (string.IsNullOrWhiteSpace(localFolder))
        {
            return null;
        }

        foreach (var entry in existing)
        {
            var cached = CachePath(localFolder, entry.FileName);
            if (string.Equals(cached, absolutePath, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Prefer LocalFolder cache; else original source. Null when both are gone
    /// (broken tile — show placeholder, not selectable).
    /// </summary>
    public static string? ResolveImagePath(
        RecentCustomBackgroundEntry entry,
        string localFolder,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        if (!string.IsNullOrWhiteSpace(entry.FileName) && !string.IsNullOrWhiteSpace(localFolder))
        {
            var cached = CachePath(localFolder, entry.FileName);
            if (fileExists(cached))
            {
                return cached;
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.SourcePath) && fileExists(entry.SourcePath))
        {
            return entry.SourcePath;
        }

        return null;
    }

    public static bool IsBroken(
        RecentCustomBackgroundEntry entry,
        string localFolder,
        Func<string, bool> fileExists) =>
        ResolveImagePath(entry, localFolder, fileExists) is null;

    public static string CachePath(string localFolder, string fileName) =>
        Path.Combine(localFolder, fileName);

    public static string NewFileName(string? extension)
    {
        var ext = extension ?? "";
        if (ext.Length > 0 && ext[0] != '.')
        {
            ext = "." + ext;
        }

        return FilePrefix + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ext;
    }

    public static bool IsStoredRecentName(string? fileName) =>
        FileNameOnly(fileName).StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase);

    public static bool ShouldKeepStoredFile(
        string? absolutePath,
        IEnumerable<RecentCustomBackgroundEntry> existing)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return false;
        }

        var name = FileNameOnly(absolutePath);
        if (name.Length == 0)
        {
            return false;
        }

        if (IsStoredRecentName(name) || ShellBackground.IsStoredWallpaperName(name))
        {
            return existing.Any(e =>
                string.Equals(e.FileName, name, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    public static string FileNameOnly(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        var name = path.Trim();
        var slash = name.LastIndexOfAny(['/', '\\']);
        return slash >= 0 ? name[(slash + 1)..] : name;
    }

    public static string DisplayLabel(string? displayNameOrPath)
    {
        var leaf = FileNameOnly(displayNameOrPath);
        if (leaf.Length == 0)
        {
            return "Custom";
        }

        return Path.GetFileNameWithoutExtension(leaf);
    }

    public static string Tooltip(string? displayName, DateTimeOffset addedUtc)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? "Custom" : displayName.Trim();
        var local = addedUtc.ToLocalTime();
        return $"{name}\nAdded {local:g}";
    }

    public static string HashFile(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static RecentCustomBackgroundEntry Create(
        string fileName,
        string displayName,
        string? sourcePath,
        double darkness,
        double blur,
        string? contentHash = null,
        DateTimeOffset? dateAddedUtc = null,
        string? id = null,
        DateTimeOffset? lastUsedUtc = null)
    {
        var added = dateAddedUtc ?? DateTimeOffset.UtcNow;
        return NormalizeEntry(new RecentCustomBackgroundEntry(
            string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) : id.Trim(),
            fileName,
            string.IsNullOrWhiteSpace(displayName) ? fileName : displayName.Trim(),
            string.IsNullOrWhiteSpace(sourcePath) ? null : sourcePath.Trim(),
            ShellBackground.ClampDarkness(darkness),
            ShellBackground.NormalizeBlur(blur),
            string.IsNullOrWhiteSpace(contentHash) ? "" : contentHash.Trim().ToLowerInvariant(),
            added,
            lastUsedUtc ?? added));
    }

    private static int FindOldestNonActiveIndex(
        IReadOnlyList<RecentCustomBackgroundEntry> list,
        string? activeId)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (string.IsNullOrWhiteSpace(activeId)
                || !string.Equals(list[i].Id, activeId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static IReadOnlyList<RecentCustomBackgroundEntry> Normalize(
        IEnumerable<RecentCustomBackgroundEntry?> entries)
    {
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<RecentCustomBackgroundEntry>();
        foreach (var raw in entries)
        {
            if (raw is null)
            {
                continue;
            }

            var entry = NormalizeEntry(raw);
            if (entry.Id.Length == 0 || entry.FileName.Length == 0)
            {
                continue;
            }

            if (!seenIds.Add(entry.Id) || !seenFiles.Add(entry.FileName))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(entry.ContentHash) && !seenHashes.Add(entry.ContentHash))
            {
                continue;
            }

            list.Add(entry);
            if (list.Count == Max)
            {
                break;
            }
        }

        return list;
    }

    private static RecentCustomBackgroundEntry NormalizeEntry(RecentCustomBackgroundEntry entry)
    {
        var added = entry.DateAddedUtc == default ? DateTimeOffset.UtcNow : entry.DateAddedUtc;
        var used = entry.LastUsedUtc == default ? added : entry.LastUsedUtc;
        return new(
            (entry.Id ?? "").Trim(),
            (entry.FileName ?? "").Trim(),
            string.IsNullOrWhiteSpace(entry.DisplayName) ? (entry.FileName ?? "").Trim() : entry.DisplayName.Trim(),
            string.IsNullOrWhiteSpace(entry.SourcePath) ? null : entry.SourcePath.Trim(),
            ShellBackground.ClampDarkness(entry.Darkness),
            ShellBackground.NormalizeBlur(entry.Blur),
            string.IsNullOrWhiteSpace(entry.ContentHash) ? "" : entry.ContentHash.Trim().ToLowerInvariant(),
            added,
            used);
    }

    private static RecentCustomDto ToDto(RecentCustomBackgroundEntry entry) =>
        new()
        {
            Id = entry.Id,
            File = entry.FileName,
            Name = entry.DisplayName,
            Source = entry.SourcePath,
            Darkness = entry.Darkness,
            Blur = entry.Blur,
            Hash = string.IsNullOrWhiteSpace(entry.ContentHash) ? null : entry.ContentHash,
            Added = entry.DateAddedUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            LastUsed = entry.LastUsedUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture)
        };

    private static RecentCustomBackgroundEntry FromDto(RecentCustomDto? dto)
    {
        if (dto is null)
        {
            return Create("", "", null, ShellBackground.DefaultDarkness, ShellBackground.DefaultBlur);
        }

        DateTimeOffset added = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(dto.Added)
            && DateTimeOffset.TryParse(dto.Added, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            added = parsed.ToUniversalTime();
        }

        DateTimeOffset lastUsed = added;
        if (!string.IsNullOrWhiteSpace(dto.LastUsed)
            && DateTimeOffset.TryParse(dto.LastUsed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedUsed))
        {
            lastUsed = parsedUsed.ToUniversalTime();
        }

        return Create(
            dto.File ?? "",
            dto.Name ?? dto.File ?? "",
            dto.Source,
            dto.Darkness ?? ShellBackground.DefaultDarkness,
            dto.Blur ?? ShellBackground.DefaultBlur,
            dto.Hash,
            added,
            dto.Id,
            lastUsed);
    }

    private sealed class RecentCustomDto
    {
        public string? Id { get; set; }
        public string? File { get; set; }
        public string? Name { get; set; }
        public string? Source { get; set; }
        public double? Darkness { get; set; }
        public double? Blur { get; set; }
        public string? Hash { get; set; }
        public string? Added { get; set; }
        public string? LastUsed { get; set; }
    }
}

/// <summary>One Recent custom row (Palace cache copy + amounts + lastUsed).</summary>
public sealed record RecentCustomBackgroundEntry(
    string Id,
    string FileName,
    string DisplayName,
    string? SourcePath,
    double Darkness,
    double Blur,
    string ContentHash,
    DateTimeOffset DateAddedUtc,
    DateTimeOffset LastUsedUtc);
