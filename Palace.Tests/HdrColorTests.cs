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

    private static void WriteU16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
    }
}
