using System.Globalization;

namespace Palace.Helpers;

/// <summary>
/// WinUI-free tag chip ink / luminance math (WCAG contrast). <see cref="TagColor"/> wraps this for brushes.
/// </summary>
public static class TagInk
{
    /// <summary>
    /// Curated palette (Tailwind-500-ish). Every swatch reaches ≥ 4.5:1 with
    /// <see cref="Contrasting"/>.
    /// </summary>
    public static IReadOnlyList<string> Presets { get; } =
    [
        "#EF4444",
        "#F97316",
        "#EAB308",
        "#22C55E",
        "#14B8A6",
        "#3B82F6",
        "#6366F1",
        "#A855F7",
        "#EC4899",
        "#64748B"
    ];

    /// <summary>
    /// Aliases mockup (al-01) group-face order: orange, teal, green, indigo,
    /// purple, yellow, blue, pink. Used when a group has no colour or the stored
    /// colour is too dark to read on the dark Tags pane.
    /// </summary>
    public static IReadOnlyList<string> GroupFacePalette { get; } =
    [
        "#F97316",
        "#14B8A6",
        "#22C55E",
        "#6366F1",
        "#A855F7",
        "#EAB308",
        "#3B82F6",
        "#EC4899"
    ];

    /// <summary>Dark Tags pane reference (~ControlFill / card on dark).</summary>
    public static Rgba DarkPaneFace { get; } = new(255, 0x1C, 0x1C, 0x1C);

    /// <summary>Minimum contrast vs <see cref="DarkPaneFace"/> for an 8 DIP group dot.</summary>
    public const double MinFaceContrast = 2.2;

    /// <summary>Fluent-ramp black ink (#E6000000) for light fills.</summary>
    public static Rgba Black { get; } = new(0xE6, 0, 0, 0);

    public static Rgba White { get; } = new(255, 255, 255, 255);

    public readonly record struct Rgba(byte A, byte R, byte G, byte B);

    public static bool TryParse(string? hex, out Rgba color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var text = hex.Trim();
        if (text.StartsWith('#') || text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[0] == '#' ? text[1..] : text[2..];
        }

        if (text.Length == 6)
        {
            text = "FF" + text;
        }

        if (text.Length != 8 ||
            !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        color = new Rgba(
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value);
        return true;
    }

    public static string ToHex(Rgba color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Canonical <c>#AARRGGBB</c>, or null when the string is not a color.</summary>
    public static string? Normalize(string? hex) =>
        TryParse(hex, out var color) ? ToHex(color) : null;

    /// <summary>
    /// True when <paramref name="hex"/> is missing/invalid or too dark against the dark Tags pane
    /// (near-black group dots vanish — Wimbly / al-01).
    /// </summary>
    public static bool IsTooDarkForFace(string? hex)
    {
        if (!TryParse(hex, out var fill))
        {
            return true;
        }

        return ContrastRatio(fill, DarkPaneFace) < MinFaceContrast;
    }

    /// <summary>
    /// Visible face colour for a group or inherited chip. Keeps a readable stored colour;
    /// otherwise picks a deterministic <see cref="GroupFacePalette"/> entry from
    /// <paramref name="stableKey"/> (group id preferred, else name).
    /// </summary>
    public static string FaceColor(string? stored, string? stableKey)
    {
        if (Normalize(stored) is { } own && !IsTooDarkForFace(own))
        {
            return own;
        }

        var key = stableKey ?? string.Empty;
        var index = StableIndex(key, GroupFacePalette.Count);
        return Normalize(GroupFacePalette[index])!;
    }

    public static double RelativeLuminance(Rgba fill) =>
        0.2126 * Linear(fill.R) + 0.7152 * Linear(fill.G) + 0.0722 * Linear(fill.B);

    /// <summary>
    /// Black or white ink by maximum WCAG contrast (equal at L ≈ 0.179). Soft black matches the Fluent text ramp.
    /// </summary>
    public static Rgba Contrasting(Rgba fill)
    {
        var l = RelativeLuminance(fill);
        var onWhite = 1.05 / (l + 0.05);
        var onBlack = (l + 0.05) / 0.05;
        return onBlack >= onWhite ? Black : White;
    }

    public static double ContrastRatio(Rgba a, Rgba b)
    {
        var l1 = RelativeLuminance(a);
        var l2 = RelativeLuminance(b);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Linear(byte channel)
    {
        var srgb = channel / 255.0;
        return srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }

    /// <summary>FNV-1a 32-bit → stable palette slot (same key ⇒ same colour).</summary>
    private static int StableIndex(string key, int modulo)
    {
        if (modulo <= 0)
        {
            return 0;
        }

        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in key)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return (int)(hash % (uint)modulo);
        }
    }
}
