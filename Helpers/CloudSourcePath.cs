using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// Virtual <see cref="SourceFolder.Path"/> values for API cloud sources.
/// Path stays UNIQUE across projects — the same cloud folder cannot be added twice.
/// </summary>
public static class CloudSourcePath
{
    public static string Build(CloudProvider provider, string accountLabel, string? folderDisplay)
    {
        var safeAccount = new string((accountLabel ?? "").Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray());
        if (string.IsNullOrWhiteSpace(safeAccount))
        {
            safeAccount = "account";
        }

        var folder = FolderDisplay(folderDisplay)
            .Replace('/', Path.DirectorySeparatorChar)
            .Trim(Path.DirectorySeparatorChar);
        return Path.Combine("cloud", provider.ToString().ToLowerInvariant(), safeAccount, folder);
    }

    public static string FolderDisplay(string? displayPath) =>
        string.IsNullOrWhiteSpace(displayPath) ? "Root" : displayPath.Trim();

    public static string? NormalizeRootItemId(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : id.Trim();

    public static bool PathTaken(string candidatePath, IEnumerable<string> existingPaths) =>
        existingPaths.Any(path => string.Equals(path, candidatePath, StringComparison.OrdinalIgnoreCase));

    public static bool SameCloudFolder(
        string accountId,
        string? rootItemId,
        string otherAccountId,
        string? otherRootItemId) =>
        string.Equals(accountId, otherAccountId, StringComparison.Ordinal)
        && string.Equals(NormalizeRootItemId(rootItemId), NormalizeRootItemId(otherRootItemId), StringComparison.Ordinal);
}
