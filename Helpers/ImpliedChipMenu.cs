namespace Palace.Helpers;

/// <summary>
/// it-01 implied-chip context menu copy (header + Filter by…). WinUI-free for Palace.Tests.
/// Menu items: Implied by… · Edit implication rule… · Filter by {tag} · Add as manual tag · Go to tag.
/// No Remove.
/// </summary>
public static class ImpliedChipMenu
{
    public const string EditRuleLabel = "Edit implication rule…";

    public const string AddAsManualLabel = "Add as manual tag";

    public const string GoToTagLabel = "Go to tag";

    /// <summary>Disabled header: same nearest-source wording as the tooltip.</summary>
    public static string HeaderText(string? nearestSourceName) =>
        ImpliedNearest.ImpliedTooltip(nearestSourceName);

    public static string FilterByLabel(string? impliedTagName)
    {
        var name = string.IsNullOrWhiteSpace(impliedTagName) ? "tag" : impliedTagName.Trim();
        return $"Filter by {name}";
    }

    /// <summary>Edit needs a nearest source id; Filter / Add / Go to need the implied tag id.</summary>
    public static bool CanEditRule(string? nearestSourceId) =>
        !string.IsNullOrWhiteSpace(nearestSourceId);

    public static bool CanActOnImpliedTag(string? impliedTagId) =>
        !string.IsNullOrWhiteSpace(impliedTagId);
}
