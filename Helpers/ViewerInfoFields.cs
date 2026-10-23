using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// Structured viewer Info / Details field labels. Empty strings mean hide the row.
/// Off WinUI for tests.
/// </summary>
public static class ViewerInfoFields
{
    public static string TypeBadge(string? fileName)
    {
        var ext = PathSafe.Extension(fileName);
        if (string.IsNullOrEmpty(ext) || ext.Length < 2)
        {
            return "";
        }

        return ext.TrimStart('.').ToUpperInvariant();
    }

    public static string FormatLabel(string? fileName, int? gifFrameCount)
    {
        var badge = TypeBadge(fileName);
        if (string.IsNullOrEmpty(badge))
        {
            return "";
        }

        if (gifFrameCount is > 1)
        {
            return $"{badge} · {gifFrameCount} frames";
        }

        return badge;
    }

    /// <summary>
    /// Right side of the File meta row: <c>EXR · 1/44</c> / <c>GIF · 44 frames</c>
    /// / format only. Position label is the frame index in the gallery.
    /// </summary>
    public static string FormatMetaRight(
        string? fileName,
        int? gifFrameCount,
        bool isHdr,
        string? positionLabel)
    {
        _ = isHdr;
        var parts = new List<string>();
        var badge = TypeBadge(fileName);
        if (!string.IsNullOrEmpty(badge))
        {
            parts.Add(badge);
        }

        if (gifFrameCount is > 1)
        {
            parts.Add($"{gifFrameCount} frames");
        }
        else if (!string.IsNullOrWhiteSpace(positionLabel))
        {
            parts.Add(positionLabel.Trim());
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Left side of the File meta row: <c>23 MB · 3840×2160</c>.</summary>
    public static string FileMetaLeft(long? bytes, int? width, int? height)
    {
        var parts = new List<string>();
        var size = SizeLabel(bytes);
        if (!string.IsNullOrEmpty(size))
        {
            parts.Add(size);
        }

        var res = ResolutionLabel(width, height);
        if (!string.IsNullOrEmpty(res))
        {
            parts.Add(res);
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Single File meta line: <c>size · resolution | format · frame</c>.
    /// Empty when both sides are empty.
    /// </summary>
    public static string FileMetaLine(
        long? bytes,
        int? width,
        int? height,
        string? fileName,
        int? gifFrameCount,
        bool isHdr,
        string? positionLabel)
    {
        var left = FileMetaLeft(bytes, width, height);
        var right = FormatMetaRight(fileName, gifFrameCount, isHdr, positionLabel);
        if (string.IsNullOrEmpty(left))
        {
            return right;
        }

        if (string.IsNullOrEmpty(right))
        {
            return left;
        }

        return $"{left} | {right}";
    }

    public static string ResolutionLabel(int? width, int? height)
    {
        if (width is > 0 && height is > 0)
        {
            return $"{width}×{height}";
        }

        return "";
    }

    public static string SizeLabel(long? bytes)
    {
        if (bytes is null or < 0)
        {
            return "";
        }

        return GalleryPresent.FormatSize(bytes);
    }

    public static string DurationLabel(int? durationMs)
    {
        if (durationMs is null or < 0)
        {
            return "";
        }

        var total = TimeSpan.FromMilliseconds(durationMs.Value);
        if (total.TotalHours >= 1)
        {
            return total.ToString(@"h\:mm\:ss");
        }

        return total.ToString(@"m\:ss");
    }

    public static string DateLabel(string? isoOrLocal)
    {
        if (string.IsNullOrWhiteSpace(isoOrLocal))
        {
            return "";
        }

        if (DateTime.TryParse(isoOrLocal, out var dt))
        {
            return dt.ToString("g");
        }

        return isoOrLocal.Trim();
    }

    public static string NitsLabel(float? nits)
    {
        if (nits is null or < 0)
        {
            return "";
        }

        return GalleryPresent.FormatNits(nits.Value);
    }

    /// <summary>
    /// MaxCLL value for the Details row labelled "MaxCLL (scRGB)".
    /// Units are scRGB channel peak (1.0 = 80 nits), not nits — e.g.
    /// <c>109.772 (R)</c> ≈ 8782 nits on the R channel.
    /// </summary>
    public static string ScrgbLabel(float? maxScrgb, char? channel)
    {
        if (maxScrgb is null or <= 0)
        {
            return "";
        }

        return channel is 'R' or 'G' or 'B'
            ? $"{maxScrgb.Value:0.###} ({channel})"
            : $"{maxScrgb.Value:0.###}";
    }

    public static string PercentLabel(double? percent)
    {
        if (percent is null or <= 0)
        {
            return "";
        }

        return $"{percent.Value:0.#}%";
    }

    public static bool HasHdrStatRows(
        bool hdrPresented,
        bool analysisIsHdr,
        float? maxScrgb,
        float? maxLum,
        float? p99,
        float? fall,
        double? aboveSdr)
    {
        if (!hdrPresented && !analysisIsHdr)
        {
            return false;
        }

        return maxScrgb is > 0
            || maxLum is > 0
            || p99 is > 0
            || fall is > 0
            || aboveSdr is > 0;
    }

    /// <summary>
    /// Color &amp; HDR expander has at least one row. Gamut summary belongs in
    /// the Gamut section — do not count it here (avoids an empty Color &amp; HDR box).
    /// </summary>
    public static bool HasColorHdrRows(
        string? color,
        string? hdrStatus,
        bool showHdrStats,
        string? displayLum,
        string? displayProbe) =>
        !string.IsNullOrWhiteSpace(color)
        || !string.IsNullOrWhiteSpace(hdrStatus)
        || showHdrStats
        || !string.IsNullOrWhiteSpace(displayLum)
        || !string.IsNullOrWhiteSpace(displayProbe);

    /// <summary>
    /// One-line HDR value for the dense Color &amp; HDR grid (no wrap).
    /// e.g. <c>OpenEXR · SDR 80 nits</c>.
    /// </summary>
    public static string ShortHdrLine(string? hdrStatus, int maxChars = 28)
    {
        if (string.IsNullOrWhiteSpace(hdrStatus))
        {
            return "";
        }

        var text = hdrStatus.Trim().Replace('\n', ' ').Replace('\r', ' ');
        while (text.Contains("  ", StringComparison.Ordinal))
        {
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (text.Length <= maxChars)
        {
            return text;
        }

        return text[..(maxChars - 1)] + "…";
    }

    public static string KindLabel(AssetKind kind) => kind switch
    {
        AssetKind.Image => "Image",
        AssetKind.Gif => "GIF",
        AssetKind.Video => "Video",
        _ => ""
    };
}
