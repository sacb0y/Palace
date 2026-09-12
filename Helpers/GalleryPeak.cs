using System.Globalization;

namespace Palace.Helpers;

/// <summary>
/// App-wide HDR peak override. Persist via LocalSettings; present reads
/// <see cref="PresentOverrideNits"/>. Off WinUI.
/// </summary>
public static class GalleryPeak
{
    public const string EnabledKey = "HdrPeakOverrideEnabled";
    public const string NitsKey = "HdrPeakOverrideNits";

    public static bool Enabled { get; private set; }

    public static float Nits { get; private set; } = GalleryPresent.SdrReferenceNits;

    public static float? PresentOverrideNits => Enabled ? Nits : null;

    public static event EventHandler? Changed;

    public static void Apply(bool enabled, float nits)
    {
        nits = GalleryPresent.ClampPeakNits(nits);
        if (Enabled == enabled && Math.Abs(Nits - nits) < 0.01f)
        {
            return;
        }

        Enabled = enabled;
        Nits = nits;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static bool ParseEnabled(object? stored) => stored switch
    {
        bool value => value,
        string text when bool.TryParse(text, out var parsed) => parsed,
        _ => false
    };

    public static float ParseNits(object? stored)
    {
        var nits = stored switch
        {
            double value => (float)value,
            float value => value,
            int value => value,
            long value => value,
            string text when float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => GalleryPresent.SdrReferenceNits
        };
        return GalleryPresent.ClampPeakNits(nits);
    }
}
