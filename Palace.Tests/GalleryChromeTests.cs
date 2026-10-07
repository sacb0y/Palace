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
    public void ParseBool_UsesCallerDefault()
    {
        Assert.False(GalleryChrome.ParseBool(null, defaultValue: false));
        Assert.True(GalleryChrome.ParseBool(null, defaultValue: true));
        Assert.False(GalleryChrome.ParseBool("garbage", defaultValue: false));
    }
}
