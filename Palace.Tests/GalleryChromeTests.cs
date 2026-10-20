using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class GalleryChromeTests
{
    [Fact]
    public void Keys_AreStable()
    {
        Assert.Equal("GalleryShowDetails", GalleryChrome.ShowDetailsKey);
        Assert.Equal("GalleryShowImageInfo", GalleryChrome.ShowImageInfoKey);
        Assert.Equal("GalleryLoopVideo", GalleryChrome.LoopVideoKey);
        Assert.Equal("GalleryDetailsSection_", GalleryChrome.DetailsSectionKeyPrefix);
        Assert.Equal("GalleryDetailsSection_file", GalleryChrome.DetailsSectionKey("file"));
    }

    [Fact]
    public void ParseShowDetails_DefaultsOn()
    {
        Assert.True(GalleryChrome.DefaultShowDetails);
        Assert.True(GalleryChrome.ParseShowDetails(null));
        Assert.True(GalleryChrome.ParseShowDetails("nope"));
        Assert.True(GalleryChrome.ParseShowDetails(true));
        Assert.False(GalleryChrome.ParseShowDetails(false));
        Assert.True(GalleryChrome.ParseShowDetails("true"));
        Assert.False(GalleryChrome.ParseShowDetails("false"));
    }

    [Fact]
    public void ParseShowImageInfo_DefaultsOn()
    {
        Assert.True(GalleryChrome.DefaultShowImageInfo);
        Assert.True(GalleryChrome.ParseShowImageInfo(null));
        Assert.True(GalleryChrome.ParseShowImageInfo("nope"));
        Assert.True(GalleryChrome.ParseShowImageInfo(true));
        Assert.False(GalleryChrome.ParseShowImageInfo(false));
        Assert.True(GalleryChrome.ParseShowImageInfo("true"));
        Assert.False(GalleryChrome.ParseShowImageInfo("false"));
    }

    [Fact]
    public void ParseLoopVideo_DefaultsOn()
    {
        Assert.True(GalleryChrome.DefaultLoopVideo);
        Assert.True(GalleryChrome.ParseLoopVideo(null));
        Assert.True(GalleryChrome.ParseLoopVideo("nope"));
        Assert.True(GalleryChrome.ParseLoopVideo(true));
        Assert.False(GalleryChrome.ParseLoopVideo(false));
        Assert.True(GalleryChrome.ParseLoopVideo("true"));
        Assert.False(GalleryChrome.ParseLoopVideo("false"));
    }

    [Fact]
    public void ParseBool_UsesCallerDefault()
    {
        Assert.False(GalleryChrome.ParseBool(null, defaultValue: false));
        Assert.True(GalleryChrome.ParseBool(null, defaultValue: true));
        Assert.False(GalleryChrome.ParseBool("garbage", defaultValue: false));
    }

    [Fact]
    public void ParseSectionExpanded_UsesModuleDefault()
    {
        Assert.True(GalleryChrome.ParseSectionExpanded(null, defaultExpanded: true));
        Assert.False(GalleryChrome.ParseSectionExpanded(null, defaultExpanded: false));
        Assert.False(GalleryChrome.ParseSectionExpanded(false, defaultExpanded: true));
        Assert.True(GalleryChrome.ParseSectionExpanded("true", defaultExpanded: false));
    }

    [Fact]
    public void CoerceExclusivePanels_DetailsWinsWhenBothOn()
    {
        Assert.Equal((true, false), GalleryChrome.CoerceExclusivePanels(true, true));
        Assert.Equal((true, false), GalleryChrome.CoerceExclusivePanels(true, false));
        Assert.Equal((false, true), GalleryChrome.CoerceExclusivePanels(false, true));
        Assert.Equal((false, false), GalleryChrome.CoerceExclusivePanels(false, false));
    }

    [Fact]
    public void CoerceExclusivePanels_FirstRunDefaultsPreferDetails()
    {
        var details = GalleryChrome.ParseShowDetails(null);
        var info = GalleryChrome.ParseShowImageInfo(null);
        Assert.True(details);
        Assert.True(info);
        Assert.Equal((true, false), GalleryChrome.CoerceExclusivePanels(details, info));
    }
}

