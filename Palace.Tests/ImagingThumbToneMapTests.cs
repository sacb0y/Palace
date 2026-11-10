using Palace.Helpers;
using Palace.Imaging;
using Xunit;

namespace Palace.Tests;

public class ImagingThumbToneMapTests
{
    [Fact]
    public void PathKind_ThumbAndFull_LutKeysDiffer()
    {
        var stats = new ImageStats(0.5f, 2f, 2f, 2f, 2f, 1f, null, false, 0);
        var thumb = LutCacheKey.Create("a", DisplayTarget.Sdr203, stats, ImagingPathKind.Thumb);
        var full = LutCacheKey.Create("a", DisplayTarget.Sdr203, stats, ImagingPathKind.Full);
        Assert.NotEqual(thumb.Token, full.Token);
        Assert.Contains("|p0", thumb.Token, StringComparison.Ordinal);
        Assert.Contains("|p1", full.Token, StringComparison.Ordinal);
    }

    [Fact]
    public void ParamsFor_UsesSdr203_ZeroEv()
    {
        var stats = new ImageStats(0.5f, 4f, 4f, 4f, 4f, 1f, null, false, 0);
        var p = ImagingThumbToneMap.ParamsFor(stats, ImagingPathKind.Thumb);
        Assert.False(p.UseHdrPath);
        Assert.Equal(0f, p.ExposureEv);
        Assert.Equal(DisplayTarget.Sdr203.PeakNits, p.Display.PeakNits);
    }

    [Fact]
    public void TryWorkingFromScrgb_ScrgbWhite_BecomesWorkingPaper()
    {
        // Absolute scRGB 1.0 = 80 nits → Working 80/203.
        float[] scrgb = [1f, 1f, 1f, 1f];
        var working = ImagingThumbToneMap.TryWorkingFromScrgb(scrgb, 1, 1);
        Assert.NotNull(working);
        var y = ImagingConstants.Bt2020Y(working!.Rgba[0], working.Rgba[1], working.Rgba[2]);
        Assert.InRange(y, 80f / 203f - 0.02f, 80f / 203f + 0.02f);
    }

    [Fact]
    public void TryWorkingFromRelative_White_IsWorkingUnity()
    {
        float[] relative = [1f, 1f, 1f, 1f];
        var working = ImagingThumbToneMap.TryWorkingFromBuffer(relative, 1, 1, absoluteScrgb: false);
        Assert.NotNull(working);
        var y = ImagingConstants.Bt2020Y(working!.Rgba[0], working.Rgba[1], working.Rgba[2]);
        Assert.InRange(y, 0.98f, 1.02f);
    }

    [Fact]
    public void RelativeWhite_MatchesLegacy255()
    {
        // Doktor B1: relative SDR/WideGamut white must not take absolute Scrgb
        // (that yielded ~168). Legacy Reinhard passthrough → 255.
        float[] relative = [1f, 1f, 1f, 1f];
        var legacy = ToneMap.ScrgbToBgra8(relative, 1, 1);
        var (imaging, _, _, used) = ImagingThumbToneMap.FromScrgb(
            relative, 1, 1, maxSide: 64, absoluteScrgb: false);
        Assert.True(used);
        Assert.Equal(255, legacy[2]); // R
        Assert.Equal(255, legacy[1]); // G
        Assert.Equal(255, legacy[0]); // B
        Assert.InRange(imaging[2], 250, 255);
        Assert.InRange(imaging[1], 250, 255);
        Assert.InRange(imaging[0], 250, 255);
        // Absolute Scrgb path intentionally darker for paper white (~168).
        var (absBgra, _, _, absUsed) = ImagingThumbToneMap.FromScrgb(
            relative, 1, 1, maxSide: 64, absoluteScrgb: true);
        Assert.True(absUsed);
        Assert.True(absBgra[2] < 200, $"absolute paper white unexpectedly bright {absBgra[2]}");
    }

    [Theory]
    [InlineData(HdrKind.HdrJxl, 1f, true)]
    [InlineData(HdrKind.HdrJxl, 0f, true)]
    [InlineData(HdrKind.WideGamutJxl, 1f, false)]
    [InlineData(HdrKind.None, 1f, false)]
    [InlineData(HdrKind.None, 3.1875f, true)]
    public void IsAbsoluteScrgbThumbBuffer_RoutesJxl(HdrKind kind, float scale, bool absolute)
    {
        Assert.Equal(absolute, ImagingThumbToneMap.IsAbsoluteScrgbThumbBuffer(kind, scale));
    }

    [Fact]
    public void TryEncodeBgra_Thumb_ProducesOpaqueSrgb()
    {
        var frame = HotSpotFrame();
        var bgra = ImagingThumbToneMap.TryEncodeBgra(frame, ImagingPathKind.Thumb);
        Assert.NotNull(bgra);
        Assert.Equal(frame.PixelCount * 4, bgra!.Length);
        // At least one non-black pixel and full alpha on first pixel.
        Assert.True(bgra[0] > 0 || bgra[1] > 0 || bgra[2] > 0);
        Assert.Equal(255, bgra[3]);
    }

    [Fact]
    public void TryEncodeBgra_ThumbLut_NearFullDirect()
    {
        var frame = HotSpotFrame();
        var thumb = ImagingThumbToneMap.TryEncodeBgra(frame, ImagingPathKind.Thumb);
        var full = ImagingThumbToneMap.TryEncodeBgra(frame, ImagingPathKind.Full);
        Assert.NotNull(thumb);
        Assert.NotNull(full);
        var maxDelta = 0;
        for (var i = 0; i < thumb!.Length; i++)
        {
            maxDelta = Math.Max(maxDelta, Math.Abs(thumb[i] - full![i]));
        }

        // Shared stats + Option A — LUT vs direct should stay close in 8-bit.
        Assert.True(maxDelta <= 12, $"thumb vs full max channel delta {maxDelta}");
    }

    [Fact]
    public void FromScrgb_UsesImaging_ForHdrLikeBuffer()
    {
        // Bright scRGB patch (HDR-ish) — Imaging path must win over legacy.
        float[] scrgb =
        [
            4f, 4f, 4f, 1f,
            0.2f, 0.2f, 0.2f, 1f,
            0.2f, 0.2f, 0.2f, 1f,
            0.2f, 0.2f, 0.2f, 1f,
        ];
        var (bgra, w, h, used) = ImagingThumbToneMap.FromScrgb(scrgb, 2, 2, maxSide: 64);
        Assert.True(used);
        Assert.Equal(2, w);
        Assert.Equal(2, h);
        Assert.Equal(16, bgra.Length);
    }

    [Fact]
    public void StillDecode_FromScrgb_ReturnsToneMappedBgra()
    {
        float[] scrgb = [2f, 1f, 0.5f, 1f];
        var result = StillDecode.FromScrgb(scrgb, 1, 1, maxSide: 64);
        Assert.True(result.ToneMapped);
        Assert.NotNull(result.Bgra);
        Assert.Equal(4, result.Bgra!.Length);
    }

    [Fact]
    public void ThumbCacheVersion_ImagingP3_IsGeneration8()
    {
        Assert.Equal(8, ThumbCacheVersion.Current);
        Assert.Equal(8, ThumbCacheVersion.ImagingP3ThumbGeneration);
        Assert.True(ThumbCacheVersion.ImagingP3ThumbChanged(@"D:\a.exr", isHdr: false));
        Assert.True(ThumbCacheVersion.ImagingP3ThumbChanged(@"D:\a.jxl", isHdr: false));
        Assert.True(ThumbCacheVersion.ImagingP3ThumbChanged(@"D:\a.hdr", isHdr: false));
        Assert.True(ThumbCacheVersion.ImagingP3ThumbChanged(@"D:\a.png", isHdr: true));
        Assert.False(ThumbCacheVersion.ImagingP3ThumbChanged(@"D:\a.jpg", isHdr: false));
        // SDR AVIF/HEIF/JXR stay shell/WIC — no wasteful gen-8 regen.
        Assert.False(ThumbCacheVersion.ImagingP3ThumbChanged(@"D:\a.avif", isHdr: false));
        Assert.False(ThumbCacheVersion.ImagingP3ThumbChanged(@"D:\a.heic", isHdr: false));
        Assert.False(ThumbCacheVersion.ImagingP3ThumbChanged(@"D:\a.jxr", isHdr: false));
        Assert.True(ThumbCacheVersion.ImagingP3ThumbChanged(@"D:\a.avif", isHdr: true));
    }

    [Fact]
    public void FromBt2020_RoundTripsBt709Primary()
    {
        Primaries.ToBt2020(1f, 0f, 0f, ColorPrimaries.Bt709, out var r2020, out var g2020, out var b2020);
        Primaries.FromBt2020(r2020, g2020, b2020, ColorPrimaries.Bt709, out var r, out var g, out var b);
        Assert.InRange(r, 0.99f, 1.01f);
        Assert.InRange(g, -0.01f, 0.01f);
        Assert.InRange(b, -0.01f, 0.01f);
    }

    private static NormalizedFrame HotSpotFrame()
    {
        // Mid grey + a hot red highlight in working space.
        float[] rgba =
        [
            0.5f, 0.5f, 0.5f, 1f,
            4f, 1f, 0.32f, 1f,
            0.5f, 0.5f, 0.5f, 1f,
            0.5f, 0.5f, 0.5f, 1f,
        ];
        return new NormalizedFrame
        {
            Rgba = rgba,
            Width = 2,
            Height = 2,
            SourceClipped = false,
            SourceClipRatio = 0f,
            MetadataPeakNits = null,
        };
    }
}
