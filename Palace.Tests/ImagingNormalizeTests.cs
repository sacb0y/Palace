using Palace.Imaging;
using Xunit;

namespace Palace.Tests;

public class ImagingNormalizeTests
{
    [Theory]
    [InlineData(TransferKind.Pq)]
    [InlineData(TransferKind.Scrgb)]
    [InlineData(TransferKind.LinearScene)]
    [InlineData(TransferKind.Sdr)]
    public void RoundTrip_WithinOnePercent(TransferKind transfer)
    {
        // SDR master clamps highlights >1 when encoding; skip the 4× pixel for SDR.
        var encoded = FixtureMaster.Encode(transfer);
        var frame = Normalize.Run(
            encoded,
            FixtureMaster.MasterWidth,
            FixtureMaster.MasterHeight,
            new NormalizeOptions(transfer));

        var master = FixtureMaster.WorkingMasterRgba;
        for (var i = 0; i < master.Length; i += 4)
        {
            if (transfer == TransferKind.Sdr && master[i] > 1f)
            {
                continue;
            }

            AssertNear(master[i], frame.Rgba[i], 0.01f);
            AssertNear(master[i + 1], frame.Rgba[i + 1], 0.01f);
            AssertNear(master[i + 2], frame.Rgba[i + 2], 0.01f);
        }
    }

    [Fact]
    public void Hlg_GreyRoundTrip_WithinOnePercent()
    {
        // Chromatic HLG inverse is approximate; greys must round-trip.
        foreach (var w in new[] { 0.18f, 1.0f, 2.0f })
        {
            FixtureMaster.EncodePixel(w, w, w, TransferKind.Hlg, 1f, out var er, out var eg, out var eb);
            Normalize.Pixel(er, eg, eb, new NormalizeOptions(TransferKind.Hlg), out var r, out var g, out var b);
            AssertNear(w, r, 0.01f);
            AssertNear(w, g, 0.01f);
            AssertNear(w, b, 0.01f);
        }
    }

    [Fact]
    public void StripsNanInf_AndClampsNegLuminance()
    {
        float[] buf =
        [
            float.NaN, float.PositiveInfinity, -0.5f, 1f,
        ];
        var frame = Normalize.Run(buf, 1, 1, new NormalizeOptions(TransferKind.Scrgb));
        Assert.True(float.IsFinite(frame.Rgba[0]));
        Assert.True(float.IsFinite(frame.Rgba[1]));
        Assert.True(ImagingConstants.Bt2020Y(frame.Rgba[0], frame.Rgba[1], frame.Rgba[2]) >= -1e-5f);
    }

    [Fact]
    public void SourceClipped_WhenEncodedMaxCluster()
    {
        // All pixels at PQ encoded max.
        var pq = ColorMath.NitsToPq(10_000f);
        var buf = new float[16];
        for (var i = 0; i < 4; i++)
        {
            buf[i * 4] = pq;
            buf[i * 4 + 1] = pq;
            buf[i * 4 + 2] = pq;
            buf[i * 4 + 3] = 1f;
        }

        var frame = Normalize.Run(buf, 2, 2, new NormalizeOptions(TransferKind.Pq));
        Assert.True(frame.SourceClipped);
        Assert.True(frame.SourceClipRatio > ImagingConstants.SourceClipFraction);
    }

    [Fact]
    public void Pq_PaperWhite_IsOne()
    {
        var pq203 = ColorMath.NitsToPq(203f);
        Normalize.Pixel(pq203, pq203, pq203, new NormalizeOptions(TransferKind.Pq), out var r, out var g, out var b);
        AssertNear(1f, r, 0.005f);
        AssertNear(1f, g, 0.005f);
        AssertNear(1f, b, 0.005f);
    }

    [Fact]
    public void Scrgb_CompositionWhite_MapsToWorking()
    {
        // scRGB 1.0 = 80 nits → 80/203 in working space.
        Normalize.Pixel(1f, 1f, 1f, new NormalizeOptions(TransferKind.Scrgb), out var r, out var g, out var b);
        var expected = ImagingConstants.ScrgbNits / ImagingConstants.RefWhiteNits;
        AssertNear(expected, r, 0.005f);
        AssertNear(expected, g, 0.005f);
        AssertNear(expected, b, 0.005f);
    }

    private static void AssertNear(float expected, float actual, float rel)
    {
        var tol = Math.Max(1e-4f, Math.Abs(expected) * rel);
        Assert.True(
            Math.Abs(actual - expected) <= tol,
            $"expected {expected}, got {actual}, tol {tol}");
    }
}
