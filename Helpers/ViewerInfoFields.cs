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
    /// Right side of the File meta row: <c>EXR · HDR 1/44</c> or just the
    /// format when there is no HDR / position tag.
    /// </summary>
    public static string FormatMetaRight(
        string? fileName,
        int? gifFrameCount,
        bool isHdr,
        string? positionLabel)
    {
        var format = FormatLabel(fileName, gifFrameCount);
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(format))
        {
            parts.Add(format.Contains('·', StringComparison.Ordinal)
                ? format.Split('·', 2)[0].Trim()
                : format);
        }

        if (isHdr)
        {
            parts.Add(string.IsNullOrWhiteSpace(positionLabel)
                ? "HDR"
                : $"HDR {positionLabel.Trim()}");
        }
        else if (!string.IsNullOrWhiteSpace(positionLabel)
            && string.IsNullOrEmpty(format))
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

    public static string KindLabel(AssetKind kind) => kind switch
    {
        AssetKind.Image => "Image",
        AssetKind.Gif => "GIF",
        AssetKind.Video => "Video",
        _ => ""
    };
}
