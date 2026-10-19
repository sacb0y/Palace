using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class GalleryDetailsTests
{
    [Fact]
    public void Rows_MatchLightroomLayout()
    {
        var rows = GalleryDetails.Build(
            @"C:\shots\IMG_0005.CR2",
            HdrProbe.None,
            4,
            33,
            5472,
            3648,
            22_858_956,
            new CameraExif(0.001, 2.8, 100, 100),
            "sdxl", "euler", "30", "42");
        Assert.Equal("CR2", rows.Format);
        Assert.Equal("5/33", rows.Index);
        Assert.Equal("5472 × 3648 (21.8 MB)", rows.Size);
        Assert.Equal("1/1000 s · F2.8 · ISO 100 · 100 mm", rows.Settings);
    }

    [Fact]
    public void Rows_FallBackToGenerationSettingsWithoutCameraExif()
    {
        var rows = GalleryDetails.Build(
            "a.png", HdrProbe.None, 0, 1, 1024, 1024, 2048, CameraExif.None,
            "juggernautXL", "dpmpp_2m", "30", "1234");
        Assert.Equal("juggernautXL · dpmpp_2m · 30 steps · Seed 1234", rows.Settings);
        Assert.Equal("AI generation settings", rows.SettingsTooltip);

        var none = GalleryDetails.Build("a.png", HdrProbe.None, 0, 1, null, null, null, CameraExif.None, null, null, null, null);
        Assert.Null(none.Settings);
        Assert.Equal("", none.Size);
    }

    [Fact]
    public void FormatLabel_AddsTransfer()
    {
        Assert.Equal("AVIF · PQ", GalleryDetails.FormatLabel("x.avif", new HdrProbe(HdrKind.HdrAvif, 9, 16, null)));
        Assert.Equal("PNG", GalleryDetails.FormatLabel("x.png", HdrProbe.None));
        Assert.Equal("", GalleryDetails.FormatLabel(null, HdrProbe.None));
    }

    [Theory]
    [InlineData(0.001, "1/1000 s")]
    [InlineData(0.125, "1/8 s")]
    [InlineData(1.0 / 60, "1/60 s")]
    [InlineData(0.6, "0.6 s")]
    [InlineData(2.0, "2 s")]
    [InlineData(1.3, "1.3 s")]
    [InlineData(30.0, "30 s")]
    public void ExposureLabel_ReadsLikeACamera(double seconds, string expected) =>
        Assert.Equal(expected, GalleryDetails.ExposureLabel(seconds));

    [Fact]
    public void SmallLabels()
    {
        Assert.Null(GalleryDetails.ExposureLabel(0));
        Assert.Equal("F2.8", GalleryDetails.ApertureLabel(2.8));
        Assert.Equal("F11", GalleryDetails.ApertureLabel(11));
        Assert.Equal("ISO 100", GalleryDetails.IsoLabel(100));
        Assert.Equal("100 mm", GalleryDetails.FocalLabel(100));
        Assert.Equal("", GalleryDetails.IndexLabel(5, 3));
        Assert.Equal("1/1", GalleryDetails.IndexLabel(0, 1));
        Assert.Null(GalleryDetails.CameraLine(CameraExif.None));
        Assert.Equal("F4", GalleryDetails.CameraLine(new CameraExif(FNumber: 4)));
    }

    [Fact]
    public void CameraTooltip_DedupesMakeInModel()
    {
        var tip = GalleryDetails.CameraTooltip(new CameraExif(
            Make: "Canon", Model: "Canon EOS 5D Mark IV", Lens: "EF100mm f/2.8L", Taken: new DateTime(2026, 5, 1, 14, 3, 0)));
        Assert.Equal("Canon EOS 5D Mark IV\nEF100mm f/2.8L\n2026-05-01 14:03", tip);
        Assert.Equal("Sony ILCE-7M3", GalleryDetails.CameraTooltip(new CameraExif(Make: "Sony", Model: "ILCE-7M3")));
    }

    [Fact]
    public void ImageInfoText_AddsAnalysisFigures()
    {
        var text = GalleryPresent.ImageInfoText(new GalleryImageInfo(
            "a.avif", 100, 10, 10, new HdrProbe(HdrKind.HdrAvif, 9, 16, null), null,
            1000, 50, 0.1f, null, 12.5f, null,
            MaxCllChannel: 'B', P99Nits: 640, FallNits: 72.5f, AboveSdrWhitePercent: 4.4, Gamut: "Rec.709 90.0% · DCI-P3 10.0%"));
        Assert.Contains("MaxCLL (scRGB): 12.5 (B)", text);
        Assert.Contains("P99 luminance: 640 nits", text);
        Assert.Contains("FALL (Palace, avg of max RGB): 72.5 nits", text);
        Assert.Contains("Above SDR white: 4.4%", text);
        Assert.Contains("Gamut: Rec.709 90.0% · DCI-P3 10.0%", text);
    }
}
