using System.Text;

namespace Palace.Helpers;

/// <summary>
/// Header-only JPEG XL / JPEG XR / PSD / DDS. No Magick, no libjxl.
/// </summary>
public static class StillFormats
{
    public static readonly byte[] JxlContainerSignature =
        [0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A];

    public static bool IsJxl(ReadOnlySpan<byte> data) =>
        (data.Length >= 2 && data[0] == 0xFF && data[1] == 0x0A)
        || HasPrefix(data, JxlContainerSignature);

    public static bool IsJxr(ReadOnlySpan<byte> data) =>
        data.Length >= 3 && data[0] == 0x49 && data[1] == 0x49 && data[2] == 0xBC;

    public static bool IsPsd(ReadOnlySpan<byte> data) =>
        data.Length >= 4
        && data[0] == (byte)'8'
        && data[1] == (byte)'B'
        && data[2] == (byte)'P'
        && data[3] == (byte)'S';

    public static bool IsDds(ReadOnlySpan<byte> data) =>
        data.Length >= 4
        && data[0] == (byte)'D'
        && data[1] == (byte)'D'
        && data[2] == (byte)'S'
        && data[3] == (byte)' ';

    public static (int Width, int Height)? TryReadPsdSize(ReadOnlySpan<byte> data)
    {
        if (!IsPsd(data) || data.Length < 26)
        {
            return null;
        }

        var version = (data[4] << 8) | data[5];
        if (version is not (1 or 2))
        {
            return null;
        }

        var height = ReadBe32(data, 14);
        var width = ReadBe32(data, 18);
        return width > 0 && height > 0 ? (width, height) : null;
    }

    public static (int Width, int Height)? TryReadDdsSize(ReadOnlySpan<byte> data)
    {
        if (!IsDds(data) || data.Length < 20)
        {
            return null;
        }

        var height = ReadLe32(data, 12);
        var width = ReadLe32(data, 16);
        return width > 0 && height > 0 ? (width, height) : null;
    }

    public static (int Width, int Height)? TryReadJxrSize(ReadOnlySpan<byte> data)
    {
        if (!IsJxr(data) || data.Length < 8)
        {
            return null;
        }

        var ifd = ReadLe32(data, 4);
        if (ifd < 8 || ifd + 2 > data.Length)
        {
            return null;
        }

        var count = data[ifd] | (data[ifd + 1] << 8);
        var i = ifd + 2;
        int? width = null;
        int? height = null;
        for (var e = 0; e < count && i + 12 <= data.Length; e++, i += 12)
        {
            var tag = data[i] | (data[i + 1] << 8);
            var value = ReadLe32(data, i + 8);
            if (tag == 0xBC82)
            {
                width = value;
            }
            else if (tag == 0xBC83)
            {
                height = value;
            }
        }

        return width is > 0 && height is > 0 ? (width.Value, height.Value) : null;
    }

    /// <summary>
    /// WIC <c>GUID_WICPixelFormat64bppRGBAHalf</c> (little-endian on disk).
    /// </summary>
    public static ReadOnlySpan<byte> JxrGuidRgbaHalf =>
        [0x24, 0xC3, 0xDD, 0x6F, 0x03, 0x4E, 0xFE, 0x4B, 0xB1, 0x85, 0x3D, 0x77, 0x76, 0x8D, 0xC9, 0x10];

    /// <summary>
    /// WIC <c>GUID_WICPixelFormat128bppRGBAFloat</c>.
    /// </summary>
    public static ReadOnlySpan<byte> JxrGuidRgbaFloat =>
        [0x24, 0xC3, 0xDD, 0x6F, 0x03, 0x4E, 0xFE, 0x4B, 0xB1, 0x85, 0x3D, 0x77, 0x76, 0x8D, 0xC9, 0x1B];

    public static bool JxrLooksHdr(ReadOnlySpan<byte> data)
    {
        if (!IsJxr(data) || data.Length < 8)
        {
            return false;
        }

        var ifd = ReadLe32(data, 4);
        if (ifd < 8 || ifd + 2 > data.Length)
        {
            return false;
        }

        var count = data[ifd] | (data[ifd + 1] << 8);
        var i = ifd + 2;
        for (var e = 0; e < count && i + 12 <= data.Length; e++, i += 12)
        {
            var tag = data[i] | (data[i + 1] << 8);
            if (tag != 0xBC80)
            {
                continue;
            }

            var type = data[i + 2] | (data[i + 3] << 8);
            var fieldCount = ReadLe32(data, i + 4);
            var offset = ReadLe32(data, i + 8);
            // PIXEL_FORMAT is a 16-byte GUID (BYTE/UNDEFINED, count 16).
            if (fieldCount != 16 || type is not (1 or 7))
            {
                return false;
            }

            if (offset < 0 || offset + 16 > data.Length)
            {
                return false;
            }

            return IsHdrJxrPixelFormat(data.Slice(offset, 16));
        }

        return false;
    }

    public static bool IsHdrJxrPixelFormat(ReadOnlySpan<byte> guid) => IsHdrJxrGuid(guid);

    public static (int Width, int Height)? TryReadJxlSize(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0x0A)
        {
            return TryReadJxlCodestreamSize(data[2..]);
        }

        if (!HasPrefix(data, JxlContainerSignature))
        {
            return null;
        }

        var i = JxlContainerSignature.Length;
        while (i + 8 <= data.Length)
        {
            var size = (uint)ReadBe32(data, i);
            var type = Encoding.ASCII.GetString(data.Slice(i + 4, 4));
            var header = 8;
            long box = size;
            if (size == 1)
            {
                if (i + 16 > data.Length)
                {
                    break;
                }

                box = ReadBe64(data, i + 8);
                header = 16;
            }
            else if (size == 0)
            {
                box = data.Length - i;
            }

            if (box < header)
            {
                break;
            }

            var start = i + header;
            var end = (int)Math.Min(i + box, data.Length);
            if (type == "jxlc" && end > start)
            {
                var payload = data[start..end];
                return payload.Length >= 2 && payload[0] == 0xFF && payload[1] == 0x0A
                    ? TryReadJxlCodestreamSize(payload[2..])
                    : TryReadJxlCodestreamSize(payload);
            }

            if (type == "jxlp" && end - start > 4)
            {
                var payload = data[(start + 4)..end];
                return payload.Length >= 2 && payload[0] == 0xFF && payload[1] == 0x0A
                    ? TryReadJxlCodestreamSize(payload[2..])
                    : TryReadJxlCodestreamSize(payload);
            }

            i = end;
        }

        return null;
    }

    public static HdrProbe ProbeJxl(ReadOnlySpan<byte> data)
    {
        if (!IsJxl(data))
        {
            return HdrProbe.None;
        }

        var info = TryReadJxlBasic(data);
        // PQ/HLG CICP only. 10-/16-bit JXL is usually lossless SDR;
        // bit depth alone must not take the scRGB path. ICC-only HDR
        // stays BitmapImage — Transfer would be sRGB.
        if (info.Transfer is 16 or 18)
        {
            return new HdrProbe(
                HdrKind.HdrJxl,
                info.Primaries,
                info.Transfer,
                null,
                info.Matrix,
                info.FullRange,
                info.BitDepth);
        }

        // JXL kP3 is 11 (DCI-P3 / Display P3). CICP 12 is Display P3 on colr.
        if (info.Primaries is 9 or 11 or 12)
        {
            return new HdrProbe(
                HdrKind.WideGamutJxl,
                info.Primaries,
                info.Transfer,
                null,
                info.Matrix,
                info.FullRange,
                info.BitDepth);
        }

        return HdrProbe.None;
    }

    private static bool IsHdrJxrGuid(ReadOnlySpan<byte> guid)
    {
        // WIC pixel-format family 6FDDC324-4E03-4BFE-B185-3D77768DCxxx.
        if (guid.Length >= 16
            && ReadLe32(guid, 0) == unchecked((int)0x6FDDC324)
            && (guid[4] | (guid[5] << 8)) == 0x4E03
            && (guid[6] | (guid[7] << 8)) == 0x4BFE
            && guid[8] == 0xB1 && guid[9] == 0x85
            && guid[10] == 0x3D && guid[11] == 0x77
            && guid[12] == 0x76 && guid[13] == 0x8D
            && guid[14] == 0xC9)
        {
            return guid[15] is
                0x10 // 64bppRGBAHalf
                or 0x11 // 32bppGrayFloat
                or 0x13 // 16bppGrayHalf
                or 0x1B // 128bppRGBAFloat
                or 0x1C // 128bppRGBFloat
                or 0x3A // 32bppRGBA1010102XR
                or 0x3B // 48bppRGBHalf
                or 0x3D // 32bppRGBE
                or 0x42; // 64bppRGBHalf
        }

        // 96bppRGBFloat (different family).
        return MatchesGuid(
            guid,
            0xE3FED0FC,
            0x5B71,
            0x4C75,
            [0x83, 0x2E, 0x6D, 0xCB, 0x5C, 0x17, 0x21, 0x5D]);
    }

    private static bool MatchesGuid(ReadOnlySpan<byte> data, uint a, ushort b, ushort c, byte[] rest)
    {
        if (data.Length < 16 || rest.Length != 8)
        {
            return false;
        }

        return ReadLe32(data, 0) == unchecked((int)a)
            && (data[4] | (data[5] << 8)) == b
            && (data[6] | (data[7] << 8)) == c
            && data[8] == rest[0] && data[9] == rest[1] && data[10] == rest[2] && data[11] == rest[3]
            && data[12] == rest[4] && data[13] == rest[5] && data[14] == rest[6] && data[15] == rest[7];
    }

    private readonly record struct JxlInfo(
        int? Width,
        int? Height,
        int? BitDepth,
        int? Primaries,
        int? Transfer,
        int? Matrix,
        bool? FullRange);

    private static JxlInfo TryReadJxlBasic(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> codestream = default;
        int? colrPrimaries = null;
        int? colrTransfer = null;
        int? colrMatrix = null;
        bool? colrFullRange = null;
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0x0A)
        {
            codestream = data[2..];
        }
        else if (HasPrefix(data, JxlContainerSignature))
        {
            var i = JxlContainerSignature.Length;
            while (i + 8 <= data.Length)
            {
                var size = (uint)ReadBe32(data, i);
                var type = Encoding.ASCII.GetString(data.Slice(i + 4, 4));
                var header = 8;
                long box = size;
                if (size == 1)
                {
                    if (i + 16 > data.Length)
                    {
                        break;
                    }

                    box = ReadBe64(data, i + 8);
                    header = 16;
                }
                else if (size == 0)
                {
                    box = data.Length - i;
                }

                if (box < header)
                {
                    break;
                }

                var start = i + header;
                var end = (int)Math.Min(i + box, data.Length);
                if (type == "colr" && end - start >= 11 && Encoding.ASCII.GetString(data.Slice(start, 4)) == "nclx")
                {
                    colrPrimaries = (data[start + 4] << 8) | data[start + 5];
                    colrTransfer = (data[start + 6] << 8) | data[start + 7];
                    colrMatrix = (data[start + 8] << 8) | data[start + 9];
                    colrFullRange = (data[start + 10] & 0x80) != 0;
                }
                else if (codestream.Length == 0 && type == "jxlc" && end > start)
                {
                    var payload = data[start..end];
                    codestream = payload.Length >= 2 && payload[0] == 0xFF && payload[1] == 0x0A
                        ? payload[2..]
                        : payload;
                }
                else if (codestream.Length == 0 && type == "jxlp" && end - start > 4)
                {
                    var payload = data[(start + 4)..end];
                    codestream = payload.Length >= 2 && payload[0] == 0xFF && payload[1] == 0x0A
                        ? payload[2..]
                        : payload;
                }

                i = end;
            }
        }

        if (codestream.Length == 0)
        {
            return colrTransfer is not null || colrPrimaries is not null
                ? new JxlInfo(null, null, null, colrPrimaries, colrTransfer, colrMatrix, colrFullRange)
                : default;
        }

        try
        {
            var bits = new JxlBits(codestream);
            var div8 = bits.Read(1) != 0;
            int height;
            int width;
            if (div8)
            {
                height = 8 * (bits.Read(5) + 1);
                var ratio = bits.Read(3);
                width = ratio == 0 ? 8 * (bits.Read(5) + 1) : AspectWidth(height, ratio);
            }
            else
            {
                height = bits.ReadU32(9, 13, 18, 30) + 1;
                var ratio = bits.Read(3);
                width = ratio == 0 ? bits.ReadU32(9, 13, 18, 30) + 1 : AspectWidth(height, ratio);
            }

            int? bitDepth = null;
            int? primaries = null;
            int? transfer = null;
            int? matrix = null;
            bool? fullRange = null;
            try
            {
                ReadJxlImageMetadata(ref bits, out bitDepth, out primaries, out transfer);
            }
            catch
            {
                // Size is enough for dimensions; color may live in colr.
            }

            return new JxlInfo(
                width,
                height,
                bitDepth,
                colrPrimaries ?? primaries,
                colrTransfer ?? transfer,
                colrMatrix ?? matrix,
                colrFullRange ?? fullRange);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// ISO 18181-1 / libjxl <c>ImageMetadata</c> after <c>SizeHeader</c>.
    /// Bit depth, then extra channels, then <c>ColorEncoding</c> (CICP-like
    /// primaries / transfer). No libjxl.
    /// </summary>
    private static void ReadJxlImageMetadata(
        ref JxlBits bits,
        out int? bitDepth,
        out int? primaries,
        out int? transfer)
    {
        bitDepth = 8;
        primaries = null;
        transfer = null;
        var allDefault = bits.Read(1) != 0;
        if (allDefault)
        {
            return;
        }

        var extraFields = bits.Read(1) != 0;
        if (extraFields)
        {
            bits.Read(3);
            if (bits.Read(1) != 0)
            {
                SkipJxlSizeHeader(ref bits);
            }

            if (bits.Read(1) != 0)
            {
                SkipJxlPreviewHeader(ref bits);
            }

            if (bits.Read(1) != 0)
            {
                SkipJxlAnimationHeader(ref bits);
            }
        }

        bitDepth = ReadJxlBitDepth(ref bits);
        bits.Read(1);
        var extra = bits.ReadU32Four(0, 0, 0, 1, 4, 2, 12, 1);
        if (extra is < 0 or > 16)
        {
            return;
        }

        for (var i = 0; i < extra; i++)
        {
            SkipJxlExtraChannel(ref bits);
        }

        bits.Read(1);
        ReadJxlColorEncoding(ref bits, out primaries, out transfer);
    }

    private static void SkipJxlSizeHeader(ref JxlBits bits)
    {
        var div8 = bits.Read(1) != 0;
        if (div8)
        {
            bits.Read(5);
            var ratio = bits.Read(3);
            if (ratio == 0)
            {
                bits.Read(5);
            }

            return;
        }

        bits.ReadU32(9, 13, 18, 30);
        var r = bits.Read(3);
        if (r == 0)
        {
            bits.ReadU32(9, 13, 18, 30);
        }
    }

    private static void SkipJxlPreviewHeader(ref JxlBits bits)
    {
        var div8 = bits.Read(1) != 0;
        if (div8)
        {
            bits.ReadU32Four(0, 16, 0, 32, 5, 1, 9, 33);
        }
        else
        {
            bits.ReadU32Four(6, 1, 8, 65, 10, 321, 12, 1345);
        }

        var ratio = bits.Read(3);
        if (ratio != 0)
        {
            return;
        }

        if (div8)
        {
            bits.ReadU32Four(0, 16, 0, 32, 5, 1, 9, 33);
        }
        else
        {
            bits.ReadU32Four(6, 1, 8, 65, 10, 321, 12, 1345);
        }
    }

    private static void SkipJxlAnimationHeader(ref JxlBits bits)
    {
        bits.ReadU32Four(0, 100, 0, 1000, 10, 1, 30, 1);
        bits.ReadU32Four(0, 1, 0, 1001, 8, 1, 10, 1);
        bits.ReadU32Four(0, 0, 3, 0, 16, 0, 32, 0);
        bits.Read(1);
    }

    private static int ReadJxlBitDepth(ref JxlBits bits)
    {
        if (bits.Read(1) != 0)
        {
            var bitsPer = bits.ReadU32Four(0, 32, 0, 16, 0, 24, 6, 1);
            bits.Read(4);
            return bitsPer;
        }

        return bits.ReadU32Four(0, 8, 0, 10, 0, 12, 6, 1);
    }

    private static void SkipJxlExtraChannel(ref JxlBits bits)
    {
        if (bits.Read(1) != 0)
        {
            return;
        }

        var type = bits.ReadEnum();
        ReadJxlBitDepth(ref bits);
        bits.ReadU32Four(0, 0, 0, 3, 0, 4, 3, 1);
        var nameLen = bits.ReadU32Four(0, 0, 4, 0, 5, 16, 10, 48);
        if (nameLen is < 0 or > 256)
        {
            throw new InvalidOperationException("JXL extra name");
        }

        bits.Read(8 * nameLen);
        if (type == 0)
        {
            bits.Read(1);
        }

        if (type == 2)
        {
            bits.Read(16);
            bits.Read(16);
            bits.Read(16);
            bits.Read(16);
        }

        if (type == 5)
        {
            bits.ReadU32Four(0, 1, 2, 0, 4, 3, 8, 19);
        }
    }

    private static void ReadJxlColorEncoding(ref JxlBits bits, out int? primaries, out int? transfer)
    {
        primaries = null;
        transfer = null;
        if (bits.Read(1) != 0)
        {
            primaries = 1;
            transfer = 13;
            return;
        }

        var wantIcc = bits.Read(1) != 0;
        var colorSpace = bits.ReadEnum();
        if (wantIcc)
        {
            return;
        }

        // XYB (2) has an implicit white point and no primaries / TF bits.
        if (colorSpace != 2)
        {
            var white = bits.ReadEnum();
            if (white == 2)
            {
                SkipJxlCustomXy(ref bits);
            }
        }

        if (colorSpace == 0)
        {
            var p = bits.ReadEnum();
            if (p == 2)
            {
                SkipJxlCustomXy(ref bits);
                SkipJxlCustomXy(ref bits);
                SkipJxlCustomXy(ref bits);
            }
            else
            {
                primaries = p;
            }
        }

        if (colorSpace != 2)
        {
            if (bits.Read(1) != 0)
            {
                bits.Read(24);
            }
            else
            {
                transfer = bits.ReadEnum();
            }
        }

        bits.ReadEnum();
    }

    private static void SkipJxlCustomXy(ref JxlBits bits)
    {
        SkipJxlCustomCoord(ref bits);
        SkipJxlCustomCoord(ref bits);
    }

    private static void SkipJxlCustomCoord(ref JxlBits bits)
    {
        var sel = bits.Read(2);
        var n = sel switch
        {
            0 => 19,
            1 => 19,
            2 => 20,
            _ => 21
        };
        bits.Read(n);
    }

    private static (int Width, int Height)? TryReadJxlCodestreamSize(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0x0A)
        {
            var signed = TryReadJxlBasic(data);
            return signed.Width is > 0 && signed.Height is > 0
                ? (signed.Width.Value, signed.Height.Value)
                : null;
        }

        var wrapped = new byte[data.Length + 2];
        wrapped[0] = 0xFF;
        wrapped[1] = 0x0A;
        data.CopyTo(wrapped.AsSpan(2));
        var info = TryReadJxlBasic(wrapped);
        return info.Width is > 0 && info.Height is > 0 ? (info.Width.Value, info.Height.Value) : null;
    }

    private static int AspectWidth(int height, int ratio) =>
        ratio switch
        {
            1 => height,
            2 => height * 12 / 10,
            3 => height * 4 / 3,
            4 => height * 3 / 2,
            5 => height * 16 / 9,
            6 => height * 5 / 4,
            7 => height * 2,
            _ => height
        };

    private struct JxlBits
    {
        private readonly byte[] _data;
        private int _bit;

        public JxlBits(ReadOnlySpan<byte> data)
        {
            _data = data.ToArray();
            _bit = 0;
        }

        public int Read(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++)
            {
                var byteIndex = _bit / 8;
                if (byteIndex >= _data.Length)
                {
                    throw new InvalidOperationException("JXL bits");
                }

                var bit = (_data[byteIndex] >> (_bit % 8)) & 1;
                value |= bit << i;
                _bit++;
            }

            return value;
        }

        public int ReadU32(int b0, int b1, int b2, int b3)
        {
            var selector = Read(2);
            var bits = selector switch
            {
                0 => b0,
                1 => b1,
                2 => b2,
                _ => b3
            };
            return Read(bits);
        }

        public int ReadEnum()
        {
            var sel = Read(2);
            return sel switch
            {
                0 => 0,
                1 => 1,
                2 => 2 + Read(4),
                _ => 18 + Read(6)
            };
        }

        public int ReadU32Four(
            int bits0,
            int off0,
            int bits1,
            int off1,
            int bits2,
            int off2,
            int bits3,
            int off3)
        {
            var sel = Read(2);
            var bits = sel switch
            {
                0 => bits0,
                1 => bits1,
                2 => bits2,
                _ => bits3
            };
            var off = sel switch
            {
                0 => off0,
                1 => off1,
                2 => off2,
                _ => off3
            };
            return off + (bits == 0 ? 0 : Read(bits));
        }
    }

    private static bool HasPrefix(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix)
    {
        if (data.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (data[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    private static int ReadBe32(ReadOnlySpan<byte> data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static long ReadBe64(ReadOnlySpan<byte> data, int offset)
    {
        var hi = (uint)ReadBe32(data, offset);
        var lo = (uint)ReadBe32(data, offset + 4);
        return ((long)hi << 32) | lo;
    }

    private static int ReadLe32(ReadOnlySpan<byte> data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
}
