using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class GalleryPeakTests
{
    public GalleryPeakTests() =>
        GalleryPeak.Apply(false, GalleryPresent.SdrReferenceNits);

    [Fact]
    public void ParseEnabled_ReadsBoolAndString()
    {
        Assert.False(GalleryPeak.ParseEnabled(null));
        Assert.True(GalleryPeak.ParseEnabled(true));
        Assert.False(GalleryPeak.ParseEnabled(false));
        Assert.True(GalleryPeak.ParseEnabled("true"));
        Assert.False(GalleryPeak.ParseEnabled("nope"));
    }

    [Fact]
    public void ParseNits_ClampsAndFallsBackToPaperWhite()
    {
        Assert.Equal(GalleryPresent.SdrReferenceNits, GalleryPeak.ParseNits(null), 2);
        Assert.Equal(400f, GalleryPeak.ParseNits(400.0), 2);
        Assert.Equal(400f, GalleryPeak.ParseNits("400"), 2);
        Assert.Equal(GalleryPresent.MinPeakNits, GalleryPeak.ParseNits(10), 2);
        Assert.Equal(GalleryPresent.MaxPeakNits, GalleryPeak.ParseNits(99999), 2);
    }

    [Fact]
    public void Apply_SetsAppWideOverrideForPresent()
    {
        var fired = 0;
        void OnChanged(object? _, EventArgs e) => fired++;
        GalleryPeak.Changed += OnChanged;
        try
        {
            GalleryPeak.Apply(true, 10);
            Assert.True(GalleryPeak.Enabled);
            Assert.Equal(GalleryPresent.MinPeakNits, GalleryPeak.Nits, 2);
            Assert.Equal(GalleryPresent.MinPeakNits, GalleryPeak.PresentOverrideNits);
            Assert.Equal(1, fired);

            GalleryPeak.Apply(true, GalleryPresent.MinPeakNits);
            Assert.Equal(1, fired);

            GalleryPeak.Apply(false, 400);
            Assert.False(GalleryPeak.Enabled);
            Assert.Equal(400f, GalleryPeak.Nits, 2);
            Assert.Null(GalleryPeak.PresentOverrideNits);
            Assert.Equal(2, fired);
        }
        finally
        {
            GalleryPeak.Changed -= OnChanged;
            GalleryPeak.Apply(false, GalleryPresent.SdrReferenceNits);
        }
    }
}
