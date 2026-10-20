namespace Palace.Helpers;

/// <summary>
/// WinUI-free Gooey TagChip semantics: apply state, chrome sizes, tooltips, a11y copy.
/// The face never shows the word "On" — applied / partial are visual only.
/// </summary>
public enum TagChipApplyState
{
    Off = 0,
    On = 1,
    Partial = 2
}

public static class TagChipVisual
{
    public const int HeightDip = 32;

    public const int CompactHeightDip = 28;

    public const int CornerRadiusDip = 16;

    /// <summary>Reserved leading slot for Checkmark / Subtract (or empty when off).</summary>
    public const int LeadingGlyphSlotDip = 14;

    public const int ColorDotDip = 8;

    public const int StarButtonDip = 24;

    public const double PartialBorderThickness = 1.5;

    /// <summary>Star fill on Dark / Default chrome.</summary>
    public const string StarGoldDarkHex = "#F2B600";

    /// <summary>Star fill on Light chrome (deeper gold for contrast on pale panes).</summary>
    public const string StarGoldLightHex = "#B8860B";

    /// <summary>
    /// Light-theme Palace pink fill floor. Raw <c>#E05A8C</c> fails text-on-accent AA;
    /// darken toward this when retinting AccentFill for Light.
    /// </summary>
    public const string LightAccentFillFloorHex = "#C2185B";

    public const string MultiSelectLegend = "✓ all selected · – some";

    public const string FaceOnLabel = "";

    public static TagChipApplyState Resolve(bool isPartial, bool isOn) =>
        isPartial ? TagChipApplyState.Partial
        : isOn ? TagChipApplyState.On
        : TagChipApplyState.Off;

    /// <summary>Board / browse selection uses on/off only (no partial).</summary>
    public static TagChipApplyState FromSelected(bool selected) =>
        selected ? TagChipApplyState.On : TagChipApplyState.Off;

    /// <summary>Never put the word "On" on the chip face.</summary>
    public static bool ShowsFaceOnWord => false;

    public static string PartialBadge(int count, int total) =>
        total > 0 && count >= 0 ? $"{count}/{total}" : "";

    public static string PartialTooltip(int count, int total) =>
        total > 0
            ? $"On {count} of {total} selected. Click to add to all."
            : "";

    public static string AssignTooltip(TagChipApplyState state, string? name, int partialCount, int selectionTotal)
    {
        if (state == TagChipApplyState.Partial && selectionTotal > 0)
        {
            return PartialTooltip(partialCount, selectionTotal);
        }

        var label = string.IsNullOrWhiteSpace(name) ? "tag" : name.Trim();
        return state == TagChipApplyState.On ? $"Remove {label}" : $"Add {label}";
    }

    public static string StarTooltip(bool starred) =>
        starred ? "Unstar tag" : "Star tag";

    /// <summary>Next apply state after a click (partial → on all; on → off; off → on).</summary>
    public static TagChipApplyState NextAfterToggle(TagChipApplyState current) =>
        current switch
        {
            TagChipApplyState.Partial => TagChipApplyState.On,
            TagChipApplyState.On => TagChipApplyState.Off,
            _ => TagChipApplyState.On
        };

    /// <summary>True when the click should add the tag to every selected asset.</summary>
    public static bool ToggleAdds(TagChipApplyState current) =>
        current is TagChipApplyState.Off or TagChipApplyState.Partial;

    /// <summary>True when the click should remove the tag from every selected asset.</summary>
    public static bool ToggleRemoves(TagChipApplyState current) =>
        current == TagChipApplyState.On;

    public static string ToggleAutomationState(TagChipApplyState state) =>
        state switch
        {
            TagChipApplyState.On => "On",
            TagChipApplyState.Partial => "Mixed",
            _ => "Off"
        };

    public static string AutomationName(string? name, TagChipApplyState state, bool starred)
    {
        var label = string.IsNullOrWhiteSpace(name) ? "Tag" : name.Trim();
        var toggle = ToggleAutomationState(state);
        var star = starred ? ", Starred" : "";
        return $"{label}, {toggle}{star}";
    }

    public static string AnnounceAdded(string? name, int count) =>
        count <= 1
            ? $"Added {TrimName(name)}"
            : $"Added {TrimName(name)} to all {count} selected";

    public static string AnnounceRemoved(string? name, int count) =>
        count <= 1
            ? $"Removed {TrimName(name)}"
            : $"Removed {TrimName(name)} from {count} selected";

    public static bool IsLightChrome(bool isLightTheme) => isLightTheme;

    public static string StarGoldHex(bool isLightTheme) =>
        isLightTheme ? StarGoldLightHex : StarGoldDarkHex;

    private static string TrimName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "tag" : name.Trim();
}
