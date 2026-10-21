namespace Palace.Helpers;

/// <summary>
/// Settings two-pane section catalog, last-open persist key, and search matching.
/// Off WinUI so Palace.Tests can cover it.
/// </summary>
public static class SettingsNav
{
    public const string LastSectionKey = "SettingsLastSection";

    public const string Appearance = "appearance";
    public const string Library = "library";
    public const string Viewer = "viewer";
    public const string Tags = "tags";
    public const string Input = "input";
    public const string Performance = "performance";
    public const string About = "about";

    public const string DefaultSectionId = Appearance;

    /// <summary>One left-nav row + searchable terms that jump into that section.</summary>
    public sealed record Section(
        string Id,
        string Title,
        string IconName,
        string AutomationId,
        IReadOnlyList<string> Keywords);

    /// <summary>Optional card target inside a section for search highlight.</summary>
    public sealed record SearchTarget(
        string SectionId,
        string CardAutomationId,
        IReadOnlyList<string> Terms);

    public static IReadOnlyList<Section> Sections { get; } =
    [
        new(
            Appearance,
            "Appearance",
            "PaintBrush",
            "BtnSettingsNav_Appearance",
            ["appearance", "theme", "accent", "background", "wallpaper", "darkness", "blur", "tint", "color", "mica", "pink", "density", "animations", "compact", "comfortable"]),
        new(
            Library,
            "Library & Folders",
            "Folder",
            "BtnSettingsNav_Library",
            ["library", "folders", "watched", "source", "organize", "destination", "cloud", "onedrive", "dropbox", "oauth", "scan", "formats", "thumbnail", "video", "images", "3d", "audio"]),
        new(
            Viewer,
            "Viewer",
            "Image",
            "BtnSettingsNav_Viewer",
            ["viewer", "hdr", "peak", "nits", "brightness", "zoom", "fit", "fill", "slideshow", "video", "tonemap"]),
        new(
            Tags,
            "Tags & Rooms",
            "Tag",
            "BtnSettingsNav_Tags",
            ["tags", "rooms", "export tags", "import tags"]),
        new(
            Input,
            "Input",
            "Keyboard",
            "BtnSettingsNav_Input",
            ["input", "shortcuts", "keyboard", "hotkey", "touch"]),
        new(
            Performance,
            "Performance",
            "Database",
            "BtnSettingsNav_Performance",
            ["performance", "storage", "cache", "media cache", "catalog", "gpu", "preview", "thumbs", "performance & storage"]),
        new(
            About,
            "About",
            "Info",
            "BtnSettingsNav_About",
            ["about", "version", "license", "licenses", "log", "logs", "export catalog", "import", "reset"])
    ];

    /// <summary>
    /// Card-level search hits (AutomationId of the SettingsCard). Kept in sync with
    /// Pages/SettingsPage.xaml card ids.
    /// </summary>
    public static IReadOnlyList<SearchTarget> Targets { get; } =
    [
        new(Appearance, "CardTheme", ["theme", "light", "dark", "system"]),
        new(Appearance, "CardAccent", ["accent", "pink", "brand", "color"]),
        new(Appearance, "CardDensity", ["density", "compact", "comfortable"]),
        new(Appearance, "CardAnimations", ["animations", "motion", "reduce motion"]),
        new(Appearance, "CardBackground", ["background", "wallpaper", "preset", "blur", "darkness", "tint", "mica", "custom", "recent"]),
        new(Library, "CardWatchedFolders", ["watched", "folder", "folders", "source", "add folder", "remove", "open folder"]),
        new(Library, "CardSelectedFolder", ["auto-organize", "organize", "template", "destination", "filename"]),
        new(Library, "CardScanning", ["scan", "scanning", "watch for changes", "watcher", "rescan"]),
        new(Library, "CardThumbnailCache", ["thumbnail", "thumbnails", "thumb cache", "build thumbnails"]),
        new(Library, "CardFormats", ["formats", "images", "video", "gif", "3d", "audio", "extensions"]),
        new(Library, "CardCloudAccounts", ["cloud", "onedrive", "dropbox", "oauth", "redirect", "connect"]),
        new(Viewer, "CardDefaultZoom", ["zoom", "fit", "fill", "1:1", "default zoom", "scale"]),
        new(Viewer, "CardHdr", ["hdr", "tonemap", "sdr tonemap"]),
        new(Viewer, "CardHdrPeak", ["hdr", "peak", "brightness", "override", "override hdr"]),
        new(Viewer, "CardHdrPeakNits", ["nits", "peak nits"]),
        new(Viewer, "CardHdrTonemap", ["tonemap", "sdr", "paper white"]),
        new(Viewer, "CardShowDetails", ["details", "info", "show details"]),
        new(Viewer, "CardSlideshow", ["slideshow", "timed", "advance"]),
        new(Viewer, "CardLoopVideo", ["loop", "loop video", "video loop", "replay"]),
        new(Tags, "CardTagPortable", ["export tags", "import tags", "tags json"]),
        new(Input, "CardShortcuts", ["shortcut", "shortcuts", "keyboard", "hotkey", "ctrl"]),
        new(Input, "CardTouchGestures", ["touch", "gestures", "pinch", "pan"]),
        new(Performance, "CardMediaCache", ["cache", "media cache", "max mb", "clear cache", "usage", "cache size"]),
        new(Performance, "CardCatalogPath", ["catalog", "palace.db", "durable", "catalog folder"]),
        new(Performance, "CardThumbConcurrency", ["thumbnail decode", "fewer", "more", "auto decode"]),
        new(Performance, "CardPreviewQuality", ["preview quality", "high preview", "standard preview"]),
        new(Performance, "CardGpuPreview", ["gpu", "gpu preview", "decode load"]),
        new(About, "CardAppVersion", ["version", "about", "palace", "infancy", "copy version"]),
        new(About, "CardUpdateChannel", ["update", "channel", "preview channel"]),
        new(About, "CardLicenses", ["license", "licenses", "attribution", "notices"]),
        new(About, "CardLogs", ["log", "logs", "error-log", "diagnostics"]),
        new(About, "CardExportImportSettings", ["export settings", "import settings", "portable settings"]),
        new(About, "CardExportCatalog", ["export catalog", "backup catalog"]),
        new(About, "CardGlobalReset", ["reset", "reset all", "global reset", "defaults"])
    ];

    public static string Normalize(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return DefaultSectionId;
        }

        var text = id.Trim();
        return Sections.Any(s => s.Id.Equals(text, StringComparison.OrdinalIgnoreCase))
            ? Sections.First(s => s.Id.Equals(text, StringComparison.OrdinalIgnoreCase)).Id
            : DefaultSectionId;
    }

    public static string ParseStored(object? stored) => stored switch
    {
        string text => Normalize(text),
        _ => DefaultSectionId
    };

    public static Section? FindSection(string? id)
    {
        var normalized = Normalize(id);
        return Sections.FirstOrDefault(s => s.Id == normalized);
    }

    /// <summary>
    /// First search hit for a query: prefers an exact card term, then any card
    /// match, then section keywords. Empty / whitespace query returns null.
    /// </summary>
    public static SearchHit? FindHit(string? query)
    {
        var hits = FindHits(query);
        if (hits.Count == 0)
        {
            return null;
        }

        var needle = query!.Trim();
        foreach (var hit in hits)
        {
            if (hit.CardAutomationId is not { } cardId)
            {
                continue;
            }

            var target = Targets.FirstOrDefault(t =>
                string.Equals(t.CardAutomationId, cardId, StringComparison.Ordinal));
            if (target is not null
                && target.Terms.Any(term =>
                    term.Equals(needle, StringComparison.OrdinalIgnoreCase)))
            {
                return hit;
            }
        }

        return hits[0];
    }

    /// <summary>
    /// All card (and, if none, section) hits for filtering Settings content.
    /// Empty / whitespace query returns an empty list.
    /// </summary>
    public static IReadOnlyList<SearchHit> FindHits(string? query)
    {
        var needle = query?.Trim() ?? "";
        if (needle.Length == 0)
        {
            return [];
        }

        var hits = new List<SearchHit>();
        foreach (var target in Targets)
        {
            if (Matches(needle, target.Terms) || Contains(needle, target.CardAutomationId))
            {
                hits.Add(new SearchHit(target.SectionId, target.CardAutomationId));
            }
        }

        if (hits.Count > 0)
        {
            return hits;
        }

        foreach (var section in Sections)
        {
            if (Contains(needle, section.Title) || Matches(needle, section.Keywords))
            {
                hits.Add(new SearchHit(section.Id, CardAutomationId: null));
            }
        }

        return hits;
    }

    /// <summary>
    /// Card AutomationIds that should stay visible for a hit set. Section-only
    /// hits (no card id) mean "show every card in that section."
    /// </summary>
    public static IReadOnlySet<string> VisibleCardIds(IReadOnlyList<SearchHit> hits)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (hits.Count == 0)
        {
            return ids;
        }

        var sectionOnly = new HashSet<string>(
            hits.Where(h => h.CardAutomationId is null).Select(h => h.SectionId),
            StringComparer.OrdinalIgnoreCase);

        foreach (var target in Targets)
        {
            if (sectionOnly.Contains(target.SectionId))
            {
                ids.Add(target.CardAutomationId);
                continue;
            }

            if (hits.Any(h =>
                    string.Equals(h.CardAutomationId, target.CardAutomationId, StringComparison.Ordinal)))
            {
                ids.Add(target.CardAutomationId);
            }
        }

        return ids;
    }

    public static IReadOnlySet<string> VisibleSectionIds(IReadOnlyList<SearchHit> hits) =>
        new HashSet<string>(
            hits.Select(h => h.SectionId),
            StringComparer.OrdinalIgnoreCase);

    public static bool Matches(string needle, IReadOnlyList<string> terms) =>
        terms.Any(term => Contains(needle, term) || Contains(term, needle));

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    public readonly record struct SearchHit(string SectionId, string? CardAutomationId);
}
