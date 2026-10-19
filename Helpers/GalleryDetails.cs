using System.Globalization;

namespace Palace.Helpers;

/// <summary>Camera EXIF shown under the histogram. All fields optional.</summary>
public readonly record struct CameraExif(
    double? ExposureSeconds = null,
    double? FNumber = null,
    int? Iso = null,
    double? FocalLengthMm = null,
    string? Make = null,
    string? Model = null,
    string? Lens = null,
    DateTime? Taken = null)
{
    public static CameraExif None { get; } = new();

    /// <summary>True when the Lightroom exposure row has anything to show.</summary>
    public bool HasExposureRow =>
        ExposureSeconds is > 0 || FNumber is > 0 || Iso is > 0 || FocalLengthMm is > 0;
}

/// <summary>
/// The rows under the histogram (Lightroom layout): format · index in the
/// current view · pixel size (file size), then camera EXIF, or AI generation
/// settings when there is no camera EXIF.
/// </summary>
public sealed record GalleryDetailsRows(
    string Format,
    string Index,
    string Size,
    string? Settings,
    string? SettingsTooltip = null)
{
    public static GalleryDetailsRows Empty { get; } = new("", "", "", null);

    public bool HasAny =>
        !string.IsNullOrEmpty(Format) || !string.IsNullOrEmpty(Index) || !string.IsNullOrEmpty(Size);
}

public static class GalleryDetails
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Extension upper-case, plus PQ / HLG / HDR when the probe says so ("AVIF · PQ").</summary>
    public static string FormatLabel(string? path, HdrProbe probe)
    {
        var ext = string.IsNullOrEmpty(path) ? "" : Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        string? hdr = probe.IsPq ? "PQ" : probe.IsHlg ? "HLG" : probe.IsHdr ? "HDR" : null;
        if (string.IsNullOrEmpty(ext))
        {
            return hdr ?? "";
        }

        return hdr is null ? ext : $"{ext} · {hdr}";
    }

    /// <summary>"5/33" — 1-based position in the current view.</summary>
    public static string IndexLabel(int index0, int count) =>
        count <= 0 || index0 < 0 || index0 >= count ? "" : $"{index0 + 1}/{count}";

    /// <summary>"5472 × 3648 (21.8 MB)". File pixels, never the 512 thumb.</summary>
    public static string SizeLabel(int? width, int? height, long? bytes)
    {
        var dims = width is > 0 && height is > 0 ? $"{width} × {height}" : "";
        var size = bytes is >= 0 ? GalleryPresent.FormatSize(bytes) : "";
        if (dims.Length > 0 && size.Length > 0)
        {
            return $"{dims} ({size})";
        }

        return dims.Length > 0 ? dims : size;
    }

    /// <summary>"1/1000 s", "1/8 s", "0.6 s", "2 s", "30 s".</summary>
    public static string? ExposureLabel(double? seconds)
    {
        if (seconds is not > 0 || !double.IsFinite(seconds.Value))
        {
            return null;
        }

        var s = seconds.Value;
        if (s >= 1)
        {
            return s >= 10 || Math.Abs(s - Math.Round(s)) < 0.05
                ? $"{Math.Round(s).ToString("0", Inv)} s"
                : $"{s.ToString("0.#", Inv)} s";
        }

        var denom = 1 / s;
        if (denom >= 2 && Math.Abs(denom - Math.Round(denom)) < 0.1 * denom)
        {
            return $"1/{Math.Round(denom).ToString("0", Inv)} s";
        }

        return $"{s.ToString("0.0#", Inv)} s";
    }

    public static string? ApertureLabel(double? f) =>
        f is > 0 && double.IsFinite(f.Value) ? $"F{f.Value.ToString("0.#", Inv)}" : null;

    public static string? IsoLabel(int? iso) => iso is > 0 ? $"ISO {iso}" : null;

    public static string? FocalLabel(double? mm) =>
        mm is > 0 && double.IsFinite(mm.Value) ? $"{mm.Value.ToString("0.#", Inv)} mm" : null;

    /// <summary>"1/1000 s · F2.8 · ISO 100 · 100 mm" or null.</summary>
    public static string? CameraLine(CameraExif exif)
    {
        var parts = new[]
            {
                ExposureLabel(exif.ExposureSeconds),
                ApertureLabel(exif.FNumber),
                IsoLabel(exif.Iso),
                FocalLabel(exif.FocalLengthMm)
            }
            .Where(p => p is not null)
            .ToArray();
        return parts.Length == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>Camera / lens / date for the row tooltip.</summary>
    public static string? CameraTooltip(CameraExif exif)
    {
        var lines = new List<string>();
        var body = string.Join(' ', new[] { exif.Make?.Trim(), exif.Model?.Trim() }
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        if (exif.Make is { Length: > 0 } make && exif.Model is { Length: > 0 } model
            && model.Trim().StartsWith(make.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            body = model.Trim();
        }

        if (body.Length > 0)
        {
            lines.Add(body);
        }

        if (!string.IsNullOrWhiteSpace(exif.Lens))
        {
            lines.Add(exif.Lens.Trim());
        }

        if (exif.Taken is { } taken)
        {
            lines.Add(taken.ToString("yyyy-MM-dd HH:mm", Inv));
        }

        return lines.Count == 0 ? null : string.Join('\n', lines);
    }

    /// <summary>"Model · Sampler · 30 steps · Seed 123" or null.</summary>
    public static string? GenerationLine(string? model, string? sampler, string? steps, string? seed)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(model))
        {
            parts.Add(model.Trim());
        }

        if (!string.IsNullOrWhiteSpace(sampler))
        {
            parts.Add(sampler.Trim());
        }

        if (!string.IsNullOrWhiteSpace(steps))
        {
            var t = steps.Trim();
            parts.Add(int.TryParse(t, NumberStyles.Integer, Inv, out _) ? $"{t} steps" : t);
        }

        if (!string.IsNullOrWhiteSpace(seed))
        {
            parts.Add($"Seed {seed.Trim()}");
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    public static GalleryDetailsRows Build(
        string? path,
        HdrProbe probe,
        int index0,
        int count,
        int? width,
        int? height,
        long? bytes,
        CameraExif exif,
        string? model,
        string? sampler,
        string? steps,
        string? seed)
    {
        var camera = CameraLine(exif);
        string? settings;
        string? tooltip;
        if (camera is not null)
        {
            settings = camera;
            tooltip = CameraTooltip(exif);
        }
        else
        {
            settings = GenerationLine(model, sampler, steps, seed);
            tooltip = settings is null ? null : "AI generation settings";
        }

        return new GalleryDetailsRows(
            FormatLabel(path, probe),
            IndexLabel(index0, count),
            SizeLabel(width, height, bytes),
            settings,
            tooltip);
    }
}
