using Palace.Helpers;
using Palace.Imaging;
using Xunit;

namespace Palace.Tests;

public class ImagingFullToneMapTests
{
    [Fact]
    public void TryMapToPresentScrgb_AbsoluteWhite_BelowUnity()
    {
        // Absolute scRGB 1.0 = 80 nits → Working ~0.394 → Option A paper darker
        // than relative unity (matches P3 absolute thumb ~168/255).
        float[] scrgb = [1f, 1f, 1f, 1f];
        var working = ImagingThumbToneMap.TryWorkingFromScrgb(scrgb, 1, 1);
        Assert.NotNull(working);
        var mapped = ImagingFullToneMap.TryMapToPresentScrgb(working!);
        Assert.NotNull(mapped);
        Assert.True(mapped![0] < 0.85f, $"absolute paper unexpectedly bright R={mapped[0]}");
        Assert.True(mapped[1] < 0.85f);
        Assert.True(mapped[2] < 0.85f);
        Assert.InRange(mapped[3], 0.99f, 1.01f);
    }

    [Fact]
    public void TryMapToPresentScrgb_RelativeWhite_NearUnity()
    {
        float[] relative = [1f, 1f, 1f, 1f];
        var working = ImagingThumbToneMap.TryWorkingFromBuffer(
            relative, 1, 1, absoluteScrgb: false);
        Assert.NotNull(working);
        var mapped = ImagingFullToneMap.TryMapToPresentScrgb(working!);
        Assert.NotNull(mapped);
        Assert.InRange(mapped![0], 0.95f, 1.05f);
        Assert.InRange(mapped[1], 0.95f, 1.05f);
        Assert.InRange(mapped[2], 0.95f, 1.05f);
    }

    [Fact]
    public void TryMapToPresentScrgb_MatchesFullBgraLinear_OnInGamut()
    {
        var frame = HotSpotFrame();
        var mapped = ImagingFullToneMap.TryMapToPresentScrgb(frame);
        var bgra = ImagingThumbToneMap.TryEncodeBgra(frame, ImagingPathKind.Full);
        Assert.NotNull(mapped);
        Assert.NotNull(bgra);
        // Mid-grey pixels (not the hot OOG red): JPEG clamps BT.709 >1 before
        // OETF; present float keeps OOG until the swapchain map clips.
        foreach (var i in new[] { 0, 2, 3 })
        {
            var o = i * 4;
            var linearR = Srgb8ToLinear(bgra![o + 2]);
            var linearG = Srgb8ToLinear(bgra[o + 1]);
            var linearB = Srgb8ToLinear(bgra[o]);
            Assert.InRange(mapped![o], linearR - 0.05f, linearR + 0.05f);
            Assert.InRange(mapped[o + 1], linearG - 0.05f, linearG + 0.05f);
            Assert.InRange(mapped[o + 2], linearB - 0.05f, linearB + 0.05f);
        }
    }

    [Fact]
    public void ResolveWorking_PrefersMatchingEager()
    {
        var eager = HotSpotFrame();
        float[] scrgb = [9f, 9f, 9f, 1f, 9f, 9f, 9f, 1f, 9f, 9f, 9f, 1f, 9f, 9f, 9f, 1f];
        var resolved = ImagingFullToneMap.ResolveWorking(eager, scrgb, 2, 2);
        Assert.Same(eager, resolved);
    }

    [Fact]
    public void ResolveWorking_LazyWhenSizeMismatch()
    {
        var eager = HotSpotFrame(); // 2×2
        float[] scrgb = [1f, 1f, 1f, 1f];
        var resolved = ImagingFullToneMap.ResolveWorking(eager, scrgb, 1, 1);
        Assert.NotNull(resolved);
        Assert.NotSame(eager, resolved);
        Assert.Equal(1, resolved!.Width);
        Assert.Equal(1, resolved.Height);
    }

    [Fact]
    public void TryMapFrameToPresentScrgb_FromScrgb_Succeeds()
    {
        float[] scrgb =
        [
            4f, 4f, 4f, 1f,
            0.2f, 0.2f, 0.2f, 1f,
            0.2f, 0.2f, 0.2f, 1f,
            0.2f, 0.2f, 0.2f, 1f,
        ];
        var mapped = ImagingFullToneMap.TryMapFrameToPresentScrgb(
            working: null, scrgb, 2, 2);
        Assert.NotNull(mapped);
        Assert.Equal(16, mapped!.Length);
    }

    [Fact]
    public void PresentMapImagingFull_Sdr_IdentityLift()
    {
        var map = GalleryPresent.PresentMapImagingFull(displayIsHdr: false);
        Assert.Equal(1f, map.Scale);
        Assert.Equal(0f, map.ReinhardWhite);
        Assert.Equal(1f, map.OutputScale);
        Assert.Equal(1f, map.ClipScrgb);
    }

    [Fact]
    public void PresentMapImagingFull_Hdr_LiftsBySdrWhiteOver80()
    {
        var map = GalleryPresent.PresentMapImagingFull(
            displayIsHdr: true, sdrWhiteLevelNits: 203f);
        Assert.Equal(0f, map.ReinhardWhite);
        Assert.Equal(203f / 80f, map.OutputScale, 3);
        Assert.Equal(203f / 80f, map.ClipScrgb, 3);
        Assert.Equal(
            GalleryPresent.ThumbMatchOutputScale(true, 203f),
            map.OutputScale);
    }

    [Fact]
    public void HotHighlight_IsToneMappedBelowSource()
    {
        float[] scrgb = [8f, 8f, 8f, 1f];
        var mapped = ImagingFullToneMap.TryMapFrameToPresentScrgb(
            working: null, scrgb, 1, 1);
        Assert.NotNull(mapped);
        Assert.True(mapped![0] < 2f, $"hot highlight not compressed R={mapped[0]}");
        Assert.True(mapped[0] > 0.1f);
    }

    private static float Srgb8ToLinear(byte v)
    {
        var s = v / 255f;
        return s <= 0.04045f
            ? s / 12.92f
            : MathF.Pow((s + 0.055f) / 1.055f, 2.4f);
    }

    private static NormalizedFrame HotSpotFrame()
    {
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
