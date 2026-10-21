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
        Assert.Equal("CardHdrPeak", hdr.Value.CardAutomationId);

        var section = SettingsNav.FindHit("Appearance");
        Assert.NotNull(section);
        Assert.Equal(SettingsNav.Appearance, section.Value.SectionId);

        var shortcuts = SettingsNav.FindHit("keyboard");
        Assert.NotNull(shortcuts);
        Assert.Equal(SettingsNav.Input, shortcuts.Value.SectionId);
        Assert.Equal("CardShortcuts", shortcuts.Value.CardAutomationId);
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
}
