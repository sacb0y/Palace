using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SettingsNavTests
{
    [Fact]
    public void Sections_AreInSpecOrder()
    {
        Assert.Equal(
            [
                SettingsNav.Appearance,
                SettingsNav.Library,
                SettingsNav.Viewer,
                SettingsNav.Tags,
                SettingsNav.Input,
                SettingsNav.Performance,
                SettingsNav.About
            ],
            SettingsNav.Sections.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void Normalize_FallsBackToAppearance()
    {
        Assert.Equal(SettingsNav.Appearance, SettingsNav.Normalize(null));
        Assert.Equal(SettingsNav.Appearance, SettingsNav.Normalize(""));
        Assert.Equal(SettingsNav.Appearance, SettingsNav.Normalize("nope"));
        Assert.Equal(SettingsNav.Viewer, SettingsNav.Normalize("Viewer"));
    }

    [Fact]
    public void ParseStored_ReadsStringOrDefault()
    {
        Assert.Equal(SettingsNav.About, SettingsNav.ParseStored("about"));
        Assert.Equal(SettingsNav.Appearance, SettingsNav.ParseStored(42));
        Assert.Equal(SettingsNav.Appearance, SettingsNav.ParseStored(null));
    }

    [Fact]
    public void FindHit_PrefersCardThenSection()
    {
        Assert.Null(SettingsNav.FindHit(""));
        Assert.Null(SettingsNav.FindHit("   "));

        var hdr = SettingsNav.FindHit("peak nits");
        Assert.NotNull(hdr);
        Assert.Equal(SettingsNav.Viewer, hdr.Value.SectionId);
        Assert.Equal("CardHdrPeakNits", hdr.Value.CardAutomationId);

        var section = SettingsNav.FindHit("appearance");
        Assert.NotNull(section);
        Assert.Equal(SettingsNav.Appearance, section.Value.SectionId);

        var shortcuts = SettingsNav.FindHit("keyboard");
        Assert.NotNull(shortcuts);
        Assert.Equal(SettingsNav.Input, shortcuts.Value.SectionId);
        Assert.Equal("CardShortcuts", shortcuts.Value.CardAutomationId);
    }

    [Fact]
    public void FindHits_CacheFiltersMatchingCards()
    {
        Assert.Empty(SettingsNav.FindHits(""));
        Assert.Empty(SettingsNav.FindHits("   "));

        var hits = SettingsNav.FindHits("cache");
        var cards = hits.Select(h => h.CardAutomationId).Where(id => id is not null).ToHashSet();
        Assert.Contains("CardMediaCache", cards);
        Assert.Contains("CardThumbnailCache", cards);

        var primary = SettingsNav.FindHit("cache");
        Assert.NotNull(primary);
        Assert.Equal("CardMediaCache", primary.Value.CardAutomationId);
        Assert.Equal(SettingsNav.Performance, primary.Value.SectionId);

        var visible = SettingsNav.VisibleCardIds(hits);
        Assert.Contains("CardMediaCache", visible);
        Assert.Contains("CardThumbnailCache", visible);
        Assert.DoesNotContain("CardTheme", visible);

        var sections = SettingsNav.VisibleSectionIds(hits);
        Assert.Contains(SettingsNav.Performance, sections);
        Assert.Contains(SettingsNav.Library, sections);
    }

    [Fact]
    public void Targets_CoverExistingSettings()
    {
        // Every mapped control family from the pre-restructure page should hit a card.
        string[] mustHit =
        [
            "theme", "accent", "density", "animations", "background", "blur",
            "watched", "organize", "onedrive", "dropbox",
            "scan", "thumbnail", "formats",
            "hdr", "export tags", "shortcuts",
            "media cache", "catalog", "version", "export catalog"
        ];
        foreach (var query in mustHit)
        {
            Assert.True(SettingsNav.FindHit(query) is not null, $"No hit for '{query}'");
        }
    }

    [Fact]
    public void FindHit_LibraryScanningAndFormats()
    {
        var scan = SettingsNav.FindHit("watch for changes");
        Assert.NotNull(scan);
        Assert.Equal(SettingsNav.Library, scan.Value.SectionId);
        Assert.Equal("CardScanning", scan.Value.CardAutomationId);

        var formats = SettingsNav.FindHit("formats");
        Assert.NotNull(formats);
        Assert.Equal(SettingsNav.Library, formats.Value.SectionId);
        Assert.Equal("CardFormats", formats.Value.CardAutomationId);

        var thumbs = SettingsNav.FindHit("build thumbnails");
        Assert.NotNull(thumbs);
        Assert.Equal("CardThumbnailCache", thumbs.Value.CardAutomationId);
    }

    [Fact]
    public void FindHit_ViewerAndInputCards()
    {
        var zoom = SettingsNav.FindHit("default zoom");
        Assert.NotNull(zoom);
        Assert.Equal(SettingsNav.Viewer, zoom.Value.SectionId);
        Assert.Equal("CardDefaultZoom", zoom.Value.CardAutomationId);

        var keepZoom = SettingsNav.FindHit("keep zoom");
        Assert.NotNull(keepZoom);
        Assert.Equal(SettingsNav.Viewer, keepZoom.Value.SectionId);
        Assert.Equal("CardKeepZoomBetweenImages", keepZoom.Value.CardAutomationId);

        var loop = SettingsNav.FindHit("replay");
        Assert.NotNull(loop);
        Assert.Equal(SettingsNav.Viewer, loop.Value.SectionId);
        Assert.Equal("CardLoopVideo", loop.Value.CardAutomationId);

        var details = SettingsNav.FindHit("show details");
        Assert.NotNull(details);
        Assert.Equal("CardShowDetails", details.Value.CardAutomationId);

        var touch = SettingsNav.FindHit("touch");
        Assert.NotNull(touch);
        Assert.Equal(SettingsNav.Input, touch.Value.SectionId);
        Assert.Equal("CardTouchGestures", touch.Value.CardAutomationId);
    }

    [Fact]
    public void FindHit_PerformanceAndAboutCards()
    {
        var gpu = SettingsNav.FindHit("preview quality");
        Assert.NotNull(gpu);
        Assert.Equal(SettingsNav.Performance, gpu.Value.SectionId);
        Assert.Equal("CardPreviewQuality", gpu.Value.CardAutomationId);

        var reset = SettingsNav.FindHit("global reset");
        Assert.NotNull(reset);
        Assert.Equal(SettingsNav.About, reset.Value.SectionId);
        Assert.Equal("CardGlobalReset", reset.Value.CardAutomationId);

        var licenses = SettingsNav.FindHit("licenses");
        Assert.NotNull(licenses);
        Assert.Equal("CardLicenses", licenses.Value.CardAutomationId);
    }
}
