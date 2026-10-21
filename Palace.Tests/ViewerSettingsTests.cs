using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

[Collection("ViewerSettingsStatic")]
public sealed class ViewerSettingsTests
{
    public ViewerSettingsTests()
    {
        ViewerSettings.Apply(
            ViewerSettings.DefaultZoom,
            ViewerSettings.DefaultTouchGestures);
    }

    [Fact]
    public void ParseZoom_AcceptsLabelsAndEnums()
    {
        Assert.Equal(ImageScaling.Fit, ViewerSettings.ParseZoom(null));
        Assert.Equal(ImageScaling.Fit, ViewerSettings.ParseZoom("nope"));
        Assert.Equal(ImageScaling.Fit, ViewerSettings.ParseZoom("Fit"));
        Assert.Equal(ImageScaling.Fill, ViewerSettings.ParseZoom("Fill"));
        Assert.Equal(ImageScaling.Actual, ViewerSettings.ParseZoom("1:1"));
        Assert.Equal(ImageScaling.Actual, ViewerSettings.ParseZoom("Actual"));
        Assert.Equal(ImageScaling.Fill, ViewerSettings.ParseZoom(ImageScaling.Fill));
        Assert.Equal(ImageScaling.Actual, ViewerSettings.ParseZoom((int)ImageScaling.Actual));
    }

    [Fact]
    public void PersistZoom_UsesFriendlyLabels()
    {
        Assert.Equal("Fit", ViewerSettings.PersistZoom(ImageScaling.Fit));
        Assert.Equal("Fill", ViewerSettings.PersistZoom(ImageScaling.Fill));
        Assert.Equal("1:1", ViewerSettings.PersistZoom(ImageScaling.Actual));
        Assert.Equal(
            ["Fit", "Fill", "1:1"],
            ViewerSettings.ZoomOptions);
    }

    [Fact]
    public void Apply_UpdatesStaticGates()
    {
        ViewerSettings.Apply(ImageScaling.Fill, touchGestures: false);
        Assert.Equal(ImageScaling.Fill, ViewerSettings.DefaultZoomMode);
        Assert.False(ViewerSettings.TouchGesturesEnabled);
        Assert.False(ViewerSettings.AllowsTouchGestures(false));
        Assert.True(ViewerSettings.AllowsTouchGestures(true));
    }

    [Fact]
    public void Captions_MentionTonemapAndSlideshow()
    {
        Assert.Contains("80 nits", ViewerSettings.SdrTonemapCaption, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("slideshow", ViewerSettings.SlideshowPlaceholderCaption, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseBool_DefaultsAndStrings()
    {
        Assert.True(ViewerSettings.ParseBool(null, true));
        Assert.False(ViewerSettings.ParseBool(null, false));
        Assert.False(ViewerSettings.ParseBool("false", true));
        Assert.True(ViewerSettings.ParseBool("True", false));
    }
}
