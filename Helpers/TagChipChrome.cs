namespace Palace.Helpers;

/// <summary>
/// Gooey TagChip layout: accent/transparent fill (never the tag color), leading
/// state glyph slot + optional color dot, wrap width from the name.
/// </summary>
public static class TagChipChrome
{
    public const int HorizontalPaddingDip = 16;

    public const int CharacterDip = 7;

    public const int StarGlyphDip = TagChipVisual.StarButtonDip;

    /// <summary>Reserved for Checkmark / Subtract; replaces the old face "On" label width.</summary>
    public const int LeadingStateSlotDip = TagChipVisual.LeadingGlyphSlotDip;

    public const int ColorDotSlotDip = TagChipVisual.ColorDotDip + 6;

    /// <summary>Obsolete name kept for call sites that still measure filter chrome; equals the leading state slot.</summary>
    public const int FilterOnLabelDip = LeadingStateSlotDip;

    /// <summary>Tag color is a leading swatch only — never the chip fill.</summary>
    public static bool ShowsLeadingDot => true;

    public static bool UsesFillBackground => false;

    /// <summary>No DIP cap / ellipsis on the face name; wrap items size to content.</summary>
    public static bool CapsNameWidth => false;

    /// <summary>Relative wrap width from the name (plus chrome), not a shared tile slot.</summary>
    public static int WrapWidth(string? name, bool starred = false, bool filterSelected = false, bool hasColor = false)
    {
        var text = name ?? "";
        var width = HorizontalPaddingDip
            + LeadingStateSlotDip
            + (text.Length * CharacterDip);
        if (hasColor)
        {
            width += ColorDotSlotDip;
        }

        if (starred)
        {
            width += StarGlyphDip;
        }

        // filterSelected used to add the "On" word; state glyph already reserved above.
        _ = filterSelected;
        return width;
    }
}
