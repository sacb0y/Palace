namespace Palace.Helpers;

/// <summary>
/// T2-03b Tags board shell: nav keys, create-target label, recently-created outline fade.
/// WinUI-free — tested from Palace.Tests.
/// </summary>
public static class TagBoardShell
{
    public const string AllKey = "all";
    public const string UngroupedKey = "ungrouped";
    public const string StarredKey = "starred";
    public const string RecentKey = "recent";
    public const string AliasesKey = "aliases";
    public const string GroupsHeaderKey = "groups-header";

    public const string NewTagsPlaceholder =
        "Type or paste names — comma, semicolon or one per line";

    public const string NewTagsHint =
        "Enter creates all · names that already exist are added to the group instead";

    public const string RecentlyCreatedCaption =
        "Green outline fades after the next refresh.";

    /// <summary>Mockup green outline (#6CCB5F @ ~55% border / 12% fill).</summary>
    public const string RecentlyCreatedBorderHex = "#8C6CCB5F";

    public const string RecentlyCreatedFillHex = "#1F6CCB5F";

    public static string CreateTargetLabel(string? groupName, bool ungrouped) =>
        ungrouped || string.IsNullOrWhiteSpace(groupName) ? "Ungrouped" : groupName.Trim();

    public static string NewTagsHeader(string? groupName, bool ungrouped) =>
        $"New tags in {CreateTargetLabel(groupName, ungrouped)}";

    /// <summary>
    /// After a create refresh stamps <paramref name="createdIds"/>, the next rebuild
    /// clears them (mockup: outline fades after the next refresh).
    /// </summary>
    public static HashSet<string> NextRecentlyCreated(
        IReadOnlyCollection<string> pendingClear,
        IReadOnlyCollection<string>? justCreated)
    {
        var next = new HashSet<string>(StringComparer.Ordinal);
        if (justCreated is { Count: > 0 })
        {
            foreach (var id in justCreated)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    next.Add(id);
                }
            }

            return next;
        }

        // Consume prior set → empty (fade).
        _ = pendingClear;
        return next;
    }

    public static bool IsNavSection(string? key) =>
        key is AllKey or UngroupedKey or StarredKey or RecentKey or AliasesKey or GroupsHeaderKey;

    public static bool IsGroupsHeader(string? key) =>
        string.Equals(key, GroupsHeaderKey, StringComparison.Ordinal);

    public static bool IsUserGroupKey(string? key) =>
        !string.IsNullOrEmpty(key) && !IsNavSection(key);
}
