namespace Palace.Helpers;

/// <summary>
/// Performance &amp; Storage prefs (LocalSettings). Off WinUI for Palace.Tests.
/// Thumbnail decode concurrency and preview decode ceiling — not a GPU driver knob.
/// </summary>
public static class PerformanceSettings
{
    public const string ThumbConcurrencyKey = "PerfThumbConcurrency";
    public const string PreviewQualityKey = "PerfPreviewQuality";

    public const string ConcurrencyAuto = "Auto";
    public const string ConcurrencyFewer = "Fewer";
    public const string ConcurrencyMore = "More";

    public const string QualityStandard = "Standard";
    public const string QualityHigh = "High";

    public const string DefaultConcurrency = ConcurrencyAuto;
    public const string DefaultPreviewQuality = QualityStandard;

    /// <summary>High preview long-edge ceiling (Standard keeps <see cref="ThumbScheduler.MaxDecodePixels"/>).</summary>
    public const int HighMaxDecodePixels = 3072;

    public static readonly string[] ConcurrencyOptions =
    [
        ConcurrencyAuto,
        ConcurrencyFewer,
        ConcurrencyMore
    ];

    public static readonly string[] PreviewQualityOptions =
    [
        QualityStandard,
        QualityHigh
    ];

    public static string ThumbConcurrency { get; private set; } = DefaultConcurrency;

    public static string PreviewQuality { get; private set; } = DefaultPreviewQuality;

    public static event EventHandler? Changed;

    public static void Apply(string? thumbConcurrency, string? previewQuality)
    {
        var concurrency = NormalizeConcurrency(thumbConcurrency);
        var quality = NormalizePreviewQuality(previewQuality);
        if (ThumbConcurrency == concurrency && PreviewQuality == quality)
        {
            return;
        }

        ThumbConcurrency = concurrency;
        PreviewQuality = quality;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static string NormalizeConcurrency(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return DefaultConcurrency;
        }

        var text = stored.Trim();
        return ConcurrencyOptions.Any(o => o.Equals(text, StringComparison.OrdinalIgnoreCase))
            ? ConcurrencyOptions.First(o => o.Equals(text, StringComparison.OrdinalIgnoreCase))
            : DefaultConcurrency;
    }

    public static string NormalizePreviewQuality(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return DefaultPreviewQuality;
        }

        var text = stored.Trim();
        return PreviewQualityOptions.Any(o => o.Equals(text, StringComparison.OrdinalIgnoreCase))
            ? PreviewQualityOptions.First(o => o.Equals(text, StringComparison.OrdinalIgnoreCase))
            : DefaultPreviewQuality;
    }

    public static string ParseConcurrency(object? stored) => stored switch
    {
        string text => NormalizeConcurrency(text),
        _ => DefaultConcurrency
    };

    public static string ParsePreviewQuality(object? stored) => stored switch
    {
        string text => NormalizePreviewQuality(text),
        _ => DefaultPreviewQuality
    };

    /// <summary>
    /// Resolved in-flight thumb decode cap. Auto follows cores; Fewer pins 2; More pins 4.
    /// </summary>
    public static int ResolveConcurrency(int processorCount, string? mode = null)
    {
        var resolved = NormalizeConcurrency(mode ?? ThumbConcurrency);
        return resolved switch
        {
            ConcurrencyFewer => ThumbScheduler.MinConcurrency,
            ConcurrencyMore => ThumbScheduler.MaxConcurrency,
            _ => ThumbScheduler.ConcurrencyCap(processorCount)
        };
    }

    public static int ResolveMaxDecodePixels(string? quality = null)
    {
        var resolved = NormalizePreviewQuality(quality ?? PreviewQuality);
        return resolved == QualityHigh ? HighMaxDecodePixels : ThumbScheduler.MaxDecodePixels;
    }

    public static string ThumbConcurrencyCaption { get; } =
        "How many mosaic thumbnails decode at once. Fewer softens load on weaker machines.";

    public static string PreviewQualityCaption { get; } =
        "Side-preview and mosaic decode ceiling. High allows larger stills before falling back to the disk thumb.";

    public static string UpdateChannelCaption { get; } =
        "Palace is in public preview. Store update channels land with the first Store release.";

    public static string UpdateChannelLabel { get; } = "Preview";
}
