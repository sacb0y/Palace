using System.Text.Json;
using System.Text.Json.Serialization;

namespace Palace.Helpers;

/// <summary>
/// Portable LocalSettings export/import (JSON). Prefs only — never catalog paths,
/// FutureAccessList tokens, OAuth client secrets, or project ids. Off WinUI.
/// </summary>
public static class SettingsPortable
{
    public const string Schema = "palace-settings/1";
    public const string FileExtension = ".palace-settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Allowlisted LocalSettings keys that travel between machines.</summary>
    public static readonly HashSet<string> ExportKeys = new(StringComparer.Ordinal)
    {
        "Theme",
        BrandAccent.SettingsKey,
        AppearanceSettings.DensityKey,
        AppearanceSettings.AnimationsKey,
        LibrarySettings.WatchForChangesKey,
        LibrarySettings.IndexImagesKey,
        LibrarySettings.IndexVideoKey,
        LibrarySettings.GenerateThumbsOnScanKey,
        ViewerSettings.DefaultZoomKey,
        ViewerSettings.TouchGesturesKey,
        ViewerSettings.KeepZoomBetweenImagesKey,
        GalleryChrome.ShowDetailsKey,
        GalleryChrome.LoopVideoKey,
        GalleryPeak.EnabledKey,
        GalleryPeak.NitsKey,
        MediaCache.EnabledKey,
        MediaCache.MaxMbKey,
        PerformanceSettings.ThumbConcurrencyKey,
        PerformanceSettings.PreviewQualityKey,
        SettingsNav.LastSectionKey,
        ShellBackground.PresetKey,
        ShellBackground.WallpaperPathKey,
        ShellBackground.WallpaperNameKey,
        ShellBackground.DarknessKey,
        ShellBackground.BlurKey,
        ShellBackground.TintEnabledKey,
        ShellBackground.TintKey,
        "ShellRecentCustoms",
        "ShellRecentCustomId",
        GalleryVolume.VolumeKey,
        GalleryVolume.MuteKey
    };

    public static string Serialize(SettingsPortableDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Schema = Schema;
        document.ExportedAt ??= DateTimeOffset.UtcNow.ToString("o");
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public static SettingsPortableDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Settings export JSON is empty.", nameof(json));
        }

        var doc = JsonSerializer.Deserialize<SettingsPortableDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("Settings export JSON deserialized to null.");
        if (!string.IsNullOrEmpty(doc.Schema)
            && !doc.Schema.StartsWith("palace-settings/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported settings export schema '{doc.Schema}'.");
        }

        doc.Values ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        return doc;
    }

    public static SettingsPortableDocument FromValues(IEnumerable<KeyValuePair<string, object>> values)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (!ExportKeys.Contains(key) || value is null)
            {
                continue;
            }

            map[key] = NormalizeStored(value);
        }

        return new SettingsPortableDocument
        {
            Schema = Schema,
            ExportedAt = DateTimeOffset.UtcNow.ToString("o"),
            Values = ToJsonMap(map)
        };
    }

    /// <summary>
    /// Apply imported values onto a mutable bag. Only allowlisted keys; unknown keys skipped.
    /// Returns how many keys were written.
    /// </summary>
    public static int ApplyTo(
        SettingsPortableDocument document,
        Action<string, object> setValue,
        Action<string>? removeValue = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(setValue);
        var count = 0;
        foreach (var (key, element) in document.Values ?? [])
        {
            if (!ExportKeys.Contains(key))
            {
                continue;
            }

            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                removeValue?.Invoke(key);
                continue;
            }

            var boxed = FromJson(element);
            if (boxed is null)
            {
                continue;
            }

            setValue(key, boxed);
            count++;
        }

        return count;
    }

    /// <summary>Keys cleared by global reset (same allowlist as export).</summary>
    public static IReadOnlyCollection<string> ResetKeys => ExportKeys;

    private static object? NormalizeStored(object value) => value switch
    {
        bool or int or long or double or float or string => value,
        _ => value.ToString()
    };

    private static Dictionary<string, JsonElement> ToJsonMap(Dictionary<string, object?> map)
    {
        var json = JsonSerializer.Serialize(map, JsonOptions);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions)
            ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    }

    private static object? FromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt32(out var i) => i,
        JsonValueKind.Number when element.TryGetInt64(out var l) => l,
        JsonValueKind.Number when element.TryGetDouble(out var d) => d,
        _ => element.GetRawText()
    };
}

public sealed class SettingsPortableDocument
{
    public string Schema { get; set; } = SettingsPortable.Schema;

    public string? ExportedAt { get; set; }

    public Dictionary<string, JsonElement>? Values { get; set; }
}
