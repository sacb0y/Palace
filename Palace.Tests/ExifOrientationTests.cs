using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Doktor #212: Smooth strip keeps storage tiles + EXIF tag for view compose.
/// Synth JPEG orientation 6 / 8 / 1 — strip display dims ≡ normal upright dims.
/// </summary>
public class ExifOrientationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(8)]
    public void TryRead_SynthJpeg_ReturnsTag(int orientation)
    {
        var jpeg = SynthJpegWithOrientation(orientation, storedW: 100, storedH: 200);
        Assert.Equal(orientation, ExifOrientation.TryRead(jpeg));
    }

    [Theory]
    [InlineData(1, 100, 200, 100, 200)]
    [InlineData(6, 100, 200, 200, 100)]
    [InlineData(8, 100, 200, 200, 100)]
    public void StripVsNormal_DisplayedOrientationAndDims(
        int exif,
        int storedW,
        int storedH,
        int uprightW,
        int uprightH)
    {
        // Strip path: storage tiles + EXIF → ExifOrient oriented size.
        var stripOrient = SmoothViewport.ExifOrient.FromExif(exif, storedW, storedH);
        Assert.Equal(uprightW, stripOrient.OrientedWidth);
        Assert.Equal(uprightH, stripOrient.OrientedHeight);

        // Normal DecodePath: WIC RespectExif → upright buffer, Orientation=1.
        var normalOrient = SmoothViewport.ExifOrient.FromExif(1, uprightW, uprightH);
        Assert.Equal(stripOrient.OrientedWidth, normalOrient.OrientedWidth);
        Assert.Equal(stripOrient.OrientedHeight, normalOrient.OrientedHeight);
        Assert.Equal(
            HdrPixels.OrientedSize(storedW, storedH, (uint)exif),
            (normalOrient.OrientedWidth, normalOrient.OrientedHeight));
    }

    [Fact]
    public void TryRead_MissingOrEmpty_DefaultsTo1()
    {
        Assert.Equal(1, ExifOrientation.TryRead(""));
        Assert.Equal(1, ExifOrientation.TryRead(ReadOnlySpan<byte>.Empty));
        Assert.Equal(1, ExifOrientation.TryRead(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }));
    }

    /// <summary>
    /// Minimal JPEG: SOI + APP1 Exif IFD0 Orientation SHORT + SOF0 dims + EOI.
    /// Not a full decodeable image — enough for <see cref="ExifOrientation.TryRead"/>.
    /// </summary>
    internal static byte[] SynthJpegWithOrientation(int orientation, int storedW, int storedH)
    {
        orientation = Math.Clamp(orientation, 1, 8);
        // TIFF LE: II*\0 + IFD0 offset 8 + 1 entry (Orientation) + next IFD 0.
        var tiff = new byte[8 + 2 + 12 + 4];
        tiff[0] = (byte)'I';
        tiff[1] = (byte)'I';
        tiff[2] = 0x2A;
        tiff[3] = 0x00;
        WriteU32Le(tiff, 4, 8); // IFD0 at 8
        WriteU16Le(tiff, 8, 1); // 1 entry
        WriteU16Le(tiff, 10, ExifOrientation.TagId);
        WriteU16Le(tiff, 12, 3); // SHORT
        WriteU32Le(tiff, 14, 1); // count
        WriteU16Le(tiff, 18, (ushort)orientation);
        WriteU16Le(tiff, 20, 0); // pad
        WriteU32Le(tiff, 22, 0); // next IFD

        var exifPayload = new byte[6 + tiff.Length];
        exifPayload[0] = (byte)'E';
        exifPayload[1] = (byte)'x';
        exifPayload[2] = (byte)'i';
        exifPayload[3] = (byte)'f';
        exifPayload[4] = 0;
        exifPayload[5] = 0;
        tiff.CopyTo(exifPayload, 6);

        var app1Len = 2 + exifPayload.Length; // includes length field itself
        using var ms = new MemoryStream();
        ms.WriteByte(0xFF);
        ms.WriteByte(0xD8); // SOI
        ms.WriteByte(0xFF);
        ms.WriteByte(0xE1); // APP1
        ms.WriteByte((byte)(app1Len >> 8));
        ms.WriteByte((byte)(app1Len & 0xFF));
        ms.Write(exifPayload);

        // SOF0 — baseline, 8-bit, 1 component (enough for marker walk to stop later).
        ms.WriteByte(0xFF);
        ms.WriteByte(0xC0);
        ms.WriteByte(0x00);
        ms.WriteByte(0x0B); // Lf = 11
        ms.WriteByte(0x08); // precision
        ms.WriteByte((byte)(storedH >> 8));
        ms.WriteByte((byte)(storedH & 0xFF));
        ms.WriteByte((byte)(storedW >> 8));
        ms.WriteByte((byte)(storedW & 0xFF));
        ms.WriteByte(0x01); // 1 component
        ms.WriteByte(0x01);
        ms.WriteByte(0x11);
        ms.WriteByte(0x00);

        ms.WriteByte(0xFF);
        ms.WriteByte(0xD9); // EOI
        return ms.ToArray();
    }

    private static void WriteU16Le(byte[] buf, int offset, ushort v)
    {
        buf[offset] = (byte)(v & 0xFF);
        buf[offset + 1] = (byte)(v >> 8);
    }

    private static void WriteU32Le(byte[] buf, int offset, uint v)
    {
        buf[offset] = (byte)(v & 0xFF);
        buf[offset + 1] = (byte)((v >> 8) & 0xFF);
        buf[offset + 2] = (byte)((v >> 16) & 0xFF);
        buf[offset + 3] = (byte)((v >> 24) & 0xFF);
    }
}
