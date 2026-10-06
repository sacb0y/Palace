using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class HdrColorTests
{
    [Fact]
    public void YuvBt2020_NeutralChromaIsWhite()
    {
        HdrColor.YuvToRgb(1f, 0.5f, 0.5f, 9, true, out var r, out var g, out var b);
        Assert.InRange(r, 0.999f, 1.001f);
        Assert.InRange(g, 0.999f, 1.001f);
        Assert.InRange(b, 0.999f, 1.001f);

        var probe = new HdrProbe(HdrKind.HdrAvif, 9, 16, null, 9, true, 10);
        HdrColor.YuvEncodedToScrgb(1f, 0.5f, 0.5f, probe, out var sr, out var sg, out var sb);
        Assert.InRange(sr, 124.5f, 125.5f);
        Assert.InRange(sg, 124.5f, 125.5f);
        Assert.InRange(sb, 124.5f, 125.5f);
    }

    [Fact]
    public void YAsRed_Pq2020_IsIsiacs207Peak()
    {
        HdrColor.EncodedRgbToScrgb(1f, 0f, 0f, HdrTransfer.Pq, 9, out var sr, out var sg, out var sb);
        Assert.Equal(HdrColor.YuvAsRedScrgbPeak, sr, 3);
        Assert.True(sr > 200);
        Assert.True(Math.Abs(sg) < 20);
        Assert.True(Math.Abs(sb) < 5);
        Assert.True(HdrColor.IsLumaInRedOnly([1f], [0f], [0f]));
        Assert.False(HdrColor.IsLumaInRedOnly([1f], [0.5f], [0.2f]));
    }

    [Fact]
    public void SkivPrimaries_PqWhiteScales()
    {
        HdrColor.EncodedRgbToScrgb(1f, 1f, 1f, HdrTransfer.Pq, 1, out var r709, out var g709, out var b709);
        Assert.Equal(HdrColor.PqToScrgb, r709, 2);
        Assert.Equal(HdrColor.PqToScrgb, g709, 2);
        Assert.Equal(HdrColor.PqToScrgb, b709, 2);

        HdrColor.EncodedRgbToScrgb(1f, 1f, 1f, HdrTransfer.Pq, 9, out var r2020, out var g2020, out var b2020);
        Assert.InRange(r2020, 124.5f, 125.5f);
        Assert.InRange(g2020, 124.5f, 125.5f);
        Assert.InRange(b2020, 124.5f, 125.5f);

        HdrColor.EncodedRgbToScrgb(1f, 1f, 1f, HdrTransfer.Pq, 11, out var rP3, out var gP3, out var bP3);
        Assert.True(rP3 > 80);
        Assert.True(gP3 > 80);
        Assert.True(bP3 > 80);

        HdrColor.EncodedRgbToScrgb(1f, 1f, 1f, HdrTransfer.Pq, 12, out var rDisp, out _, out _);
        Assert.InRange(rDisp, 124.5f, 125.5f);
    }

    [Fact]
    public void Hlg_Uses12_5NotPq125()
    {
        HdrColor.Linear01ToScrgb(1f, 1f, 1f, HdrTransfer.Hlg, 9, out var sr, out var sg, out var sb);
        Assert.Equal(HdrColor.HlgToScrgb, sr, 2);
        Assert.Equal(HdrColor.HlgToScrgb, sg, 2);
        Assert.Equal(HdrColor.HlgToScrgb, sb, 2);
        Assert.True(sr < 20);
    }

    [Fact]
    public void SourcePrimaryY_IsBefore2020To709()
    {
        var sourceY = HdrColor.SourcePrimaryNitsY(1f, 0f, 0f, HdrTransfer.Pq, 9);
        Assert.InRange(sourceY, 2625f, 2630f);

        HdrColor.EncodedRgbToScrgb(1f, 0f, 0f, HdrTransfer.Pq, 9, out var sr, out var sg, out var sb);
        var afterPrimaries = GalleryPresent.LuminanceY(
            sr * GalleryPresent.ScrgbNits,
            sg * GalleryPresent.ScrgbNits,
            sb * GalleryPresent.ScrgbNits,
            true);
        Assert.True(afterPrimaries > sourceY + 400f, $"post-matrix {afterPrimaries} vs source {sourceY}");

        var probe = new HdrProbe(HdrKind.HdrAvif, 9, 16, null, 9, true, 10);
        HdrColor.YuvToRgb(1f, 0.5f, 0.5f, 9, true, out var r, out var g, out var b);
        Assert.InRange(HdrColor.YuvSourcePrimaryNitsY(1f, 0.5f, 0.5f, probe), 9990f, 10010f);
        Assert.Equal(HdrColor.SourcePrimaryNitsY(r, g, b, HdrTransfer.Pq, 9), HdrColor.YuvSourcePrimaryNitsY(1f, 0.5f, 0.5f, probe), 2);
    }

    [Fact]
    public void IdentityMatrix_IsGbrWithoutChromaOffset()
    {
        HdrColor.YuvToRgb(0.2f, 0.3f, 0.9f, 0, true, out var r, out var g, out var b);
        Assert.InRange(r, 0.899f, 0.901f);
        Assert.InRange(g, 0.199f, 0.201f);
        Assert.InRange(b, 0.299f, 0.301f);

        HdrColor.YuvToRgb(1f, 1f, 1f, 0, true, out var wr, out var wg, out var wb);
        Assert.InRange(wr, 0.999f, 1.001f);
        Assert.InRange(wg, 0.999f, 1.001f);
        Assert.InRange(wb, 0.999f, 1.001f);

        var probe = new HdrProbe(HdrKind.HdrAvif, 9, 16, 1499, 0, true, 10);
        HdrColor.YuvEncodedToScrgb(1f, 0f, 0f, probe, out var sr, out var sg, out var sb);
        Assert.True(sr < 50, $"identity G peak must not be Y-as-R ({sr})");
        Assert.True(sg > 80);
        Assert.True(Math.Abs(sr - HdrColor.YuvAsRedScrgbPeak) > 50);
        Assert.True(HdrColor.IsIdentityMatrix(0));
        Assert.False(HdrColor.IsIdentityMatrix(9));
    }

    [Fact]
    public void IdentityLimitedRange_UsesLumaExpandOnAllPlanes()
    {
        var black = 16f / 255f;
        HdrColor.YuvToRgb(black, black, black, 0, false, out var r, out var g, out var b);
        Assert.InRange(r, -0.001f, 0.001f);
        Assert.InRange(g, -0.001f, 0.001f);
        Assert.InRange(b, -0.001f, 0.001f);

        var mid = 128f / 255f;
        HdrColor.YuvToRgb(mid, mid, mid, 0, false, out var mr, out var mg, out var mb);
        Assert.InRange(mr, 0.50f, 0.52f);
        Assert.InRange(mg, 0.50f, 0.52f);
        Assert.InRange(mb, 0.50f, 0.52f);
        Assert.InRange(HdrColor.LumaRange(black, false), -0.001f, 0.001f);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(9)]
    public void YcbcrMatrices_NeutralChromaIsWhite(int matrix)
    {
        HdrColor.YuvToRgb(1f, 0.5f, 0.5f, matrix, true, out var r, out var g, out var b);
        Assert.InRange(r, 0.999f, 1.001f);
        Assert.InRange(g, 0.999f, 1.001f);
        Assert.InRange(b, 0.999f, 1.001f);
    }

    [Fact]
    public void LimitedRangeYuv_ExpandsStudioBlack()
    {
        var y = 16f / 255f;
        var c = 128f / 255f;
        HdrColor.YuvToRgb(y, c, c, 9, true, out var fr, out _, out _);
        HdrColor.YuvToRgb(y, c, c, 9, false, out var lr, out var lg, out var lb);
        Assert.True(fr > 0.05f);
        Assert.InRange(lr, -0.001f, 0.001f);
        Assert.InRange(lg, -0.001f, 0.001f);
        Assert.InRange(lb, -0.001f, 0.001f);
    }

    [Fact]
    public void P010_MsbAlignedPeakIsOne()
    {
        var y = (ushort)(1023 << 6);
        var uv = (ushort)(512 << 6);
        var data = new byte[2 * 2 * 3];
        WriteU16(data, 0, y);
        WriteU16(data, 2, y);
        WriteU16(data, 4, y);
        WriteU16(data, 6, y);
        WriteU16(data, 8, uv);
        WriteU16(data, 10, uv);
        HdrPixels.ReadYuv(data, 2, 2, 0, 0, HdrPackedFormat.P010, out var luma, out var u, out var v);
        Assert.InRange(luma, 0.999f, 1.001f);
        Assert.InRange(u, 0.499f, 0.502f);
        Assert.InRange(v, 0.499f, 0.502f);
        Assert.True(HdrPixels.IsYuv(HdrPackedFormat.P010));
        Assert.True(HdrPixels.HasPackedData(data, HdrPackedFormat.P010, 2, 2));
    }

    [Fact]
    public void Yuy2_ReadsPairedChroma()
    {
        var data = new byte[] { 255, 128, 255, 128, 0, 16, 0, 240 };
        HdrPixels.ReadYuv(data, 2, 2, 0, 0, HdrPackedFormat.Yuy2, out var y0, out var u0, out var v0);
        Assert.InRange(y0, 0.999f, 1.001f);
        Assert.InRange(u0, 0.499f, 0.503f);
        Assert.InRange(v0, 0.499f, 0.503f);
        HdrPixels.ReadYuv(data, 2, 2, 1, 1, HdrPackedFormat.Yuy2, out var y1, out var u1, out var v1);
        Assert.InRange(y1, -0.001f, 0.001f);
        Assert.InRange(u1, 15f / 255f, 17f / 255f);
        Assert.InRange(v1, 239f / 255f, 241f / 255f);
        Assert.True(HdrPixels.IsYuv(HdrPackedFormat.Yuy2));
        Assert.True(HdrPixels.TreatAsYuv(HdrPackedFormat.Rgba16, true, identityMatrix: true));
        Assert.False(HdrPixels.TreatAsYuv(HdrPackedFormat.Rgba16, true, identityMatrix: false));
        Assert.False(HdrPixels.TreatAsYuv(HdrPackedFormat.Rgba16, false, identityMatrix: true));
        Assert.True(HdrPixels.TreatAsYuv(HdrPackedFormat.Yuy2, true, identityMatrix: false));
    }

    [Fact]
    public void Rgba16_444_IdentityReadsGbr()
    {
        var data = new byte[8];
        WriteU16(data, 0, 0x3333);
        WriteU16(data, 2, 0x6666);
        WriteU16(data, 4, 0xCCCC);
        WriteU16(data, 6, 0xFFFF);
        HdrPixels.ReadYuvPackedRgb(data, 1, 1, 0, 0, HdrPackedFormat.Rgba16, out var y, out var u, out var v);
        HdrColor.YuvToRgb(y, u, v, 0, true, out var r, out var g, out var b);
        Assert.InRange(g, 0.19f, 0.21f);
        Assert.InRange(b, 0.39f, 0.41f);
        Assert.InRange(r, 0.79f, 0.81f);
    }

    private static void WriteU16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
    }
}
