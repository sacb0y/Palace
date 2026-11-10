using System.Text;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ExifTiffEndianTests
{
    [Fact]
    public void TryRead_TiffIiHeader_IsLittleEndian()
    {
        var path = Path.Combine(Path.GetTempPath(), "palace-tiff-le-" + Guid.NewGuid().ToString("N") + ".tif");
        try
        {
            // Minimal II*\0 TIFF header (no IFDs needed for endian probe).
            File.WriteAllBytes(path, [(byte)'I', (byte)'I', 0x2A, 0x00, 0, 0, 0, 0]);
            Assert.True(ExifTiffEndian.TryReadLittleEndian(path, out var le));
            Assert.True(le);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public void TryRead_TiffMmHeader_IsBigEndian()
    {
        var path = Path.Combine(Path.GetTempPath(), "palace-tiff-be-" + Guid.NewGuid().ToString("N") + ".tif");
        try
        {
            File.WriteAllBytes(path, [(byte)'M', (byte)'M', 0x00, 0x2A, 0, 0, 0, 0]);
            Assert.True(ExifTiffEndian.TryReadLittleEndian(path, out var le));
            Assert.False(le);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public void TryRead_JpegWithExifApp1_ReadsIi()
    {
        var path = Path.Combine(Path.GetTempPath(), "palace-jpeg-exif-" + Guid.NewGuid().ToString("N") + ".jpg");
        try
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0xFF);
                ms.WriteByte(0xD8); // SOI
                ms.WriteByte(0xFF);
                ms.WriteByte(0xE1); // APP1
                // length includes itself: 2 + 6 Exif\0\0 + 8 TIFF header = 16
                ms.WriteByte(0x00);
                ms.WriteByte(0x10);
                ms.Write(Encoding.ASCII.GetBytes("Exif\0\0"));
                ms.Write([(byte)'I', (byte)'I', 0x2A, 0x00, 0, 0, 0, 0]);
                ms.WriteByte(0xFF);
                ms.WriteByte(0xD9); // EOI
                File.WriteAllBytes(path, ms.ToArray());
            }

            Assert.True(ExifTiffEndian.TryReadLittleEndian(path, out var le));
            Assert.True(le);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
