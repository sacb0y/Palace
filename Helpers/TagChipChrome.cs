namespace Palace.Helpers;

/// <summary>
/// Tags-board child chips (<c>RepTagChildren</c>): fill is the tag color, width follows the name.
/// Library assign/filter chips and the Tags asset mosaic stay on their own surfaces.
/// </summary>
public static class TagChipChrome
{
    public const int HorizontalPaddingDip = 20;

    public const int CharacterDip = 7;

    public const int StarGlyphDip = 18;

    public const int FilterOnLabelDip = 24;

    /// <summary>Board chips paint <c>EffectiveColor</c> as the chip fill, not a leading swatch.</summary>
    public static bool ShowsLeadingDot => false;

    public static bool UsesFillBackground => true;

    /// <summary>No DIP cap / ellipsis on the face name; wrap items size to content.</summary>
    public static bool CapsNameWidth => false;

    /// <summary>Relative wrap width from the name (plus chrome), not a shared tile slot.</summary>
    public static int WrapWidth(string? name, bool starred = false, bool filterSelected = false)
    {
        var text = name ?? "";
        var width = HorizontalPaddingDip + (text.Length * CharacterDip);
        if (starred)
        {
            width += StarGlyphDip;
        }

        if (filterSelected)
        {
            width += FilterOnLabelDip;
        }

        return width;
    }
}
