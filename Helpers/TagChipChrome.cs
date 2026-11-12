namespace Palace.Helpers;

/// <summary>
/// Gooey TagChip layout: accent/transparent fill (never the tag color), leading
/// state glyph slot + optional color dot, wrap width from the name.
/// </summary>
public static class TagChipChrome
{
    /// <summary>Left + right board padding (<see cref="TagChipVisual.BoardPaddingXDip"/> × 2).</summary>
    public const int HorizontalPaddingDip = TagChipVisual.BoardPaddingXDip * 2;

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

    /// <summary>
    /// Relative wrap width from the name (plus visible chrome only). Collapsed
    /// leading glyphs must not reserve ColumnSpacing / slot width (Doktor #197).
    /// </summary>
    public static int WrapWidth(
        string? name,
        bool starred = false,
        bool filterSelected = false,
        bool hasColor = false,
        bool showStateGlyph = false)
    {
        var text = name ?? "";
        var width = HorizontalPaddingDip + (text.Length * CharacterDip);
        if (showStateGlyph || filterSelected)
        {
            width += LeadingStateSlotDip;
        }

        if (hasColor)
        {
            width += ColorDotSlotDip;
        }

        if (starred)
        {
            width += StarGlyphDip;
        }

        return width;
    }
}
