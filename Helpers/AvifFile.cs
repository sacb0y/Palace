namespace Palace.Helpers;

/// <summary>
/// Header-only AVIF (ISO BMFF) size + CICP. No decoder, no Magick.
/// </summary>
public readonly record struct AvifInfo(
    int? Width,
    int? Height,
    int? CicpPrimaries,
    int? CicpTransfer,
    int? MaxCllNits,
    bool HasMastering,
    int? CicpMatrix = null,
    bool? FullRange = null,
    int? BitDepth = null,
    bool IsAvif = true);

public static class AvifFile
{
    public const int MaxMetaBytes = 1024 * 1024;

    private const uint Ftyp = 0x66747970;
    private const uint Avif = 0x61766966;
    private const uint Avis = 0x61766973;
    private const uint Meta = 0x6D657461;
    private const uint Iprp = 0x69707270;
    private const uint Ipco = 0x6970636F;
    private const uint Ipma = 0x69706D61;
    private const uint Pitm = 0x7069746D;
    private const uint Ispe = 0x69737065;
    private const uint Colr = 0x636F6C72;
    private const uint Nclx = 0x6E636C78;
    private const uint Clli = 0x636C6C69;
    private const uint Mdcv = 0x6D646376;
    private const uint Mdat = 0x6D646174;
    private const uint Av1C = 0x61763143;
    private const uint Heic = 0x68656963;
    private const uint Heix = 0x68656978;
    private const uint Mif1 = 0x6D696631;

    public static bool IsAvif(ReadOnlySpan<byte> data) =>
        HasBrand(data, Avif) || HasBrand(data, Avis);

    public static bool IsHeif(ReadOnlySpan<byte> data) =>
        IsAvif(data) || HasBrand(data, Heic) || HasBrand(data, Heix) || HasBrand(data, Mif1);

    private static bool HasBrand(ReadOnlySpan<byte> data, uint brand)
    {
        if (data.Length < 16 || TypeAt(data, 4) != Ftyp)
        {
            return false;
        }

        var size = ReadBe32(data, 0);
        if (size < 16)
        {
            return false;
        }

        var boxEnd = Math.Min(size, data.Length);
        if (TypeAt(data, 8) == brand)
        {
            return true;
        }

        for (var i = 16; i + 4 <= boxEnd; i += 4)
        {
            if (TypeAt(data, i) == brand)
            {
                return true;
            }
        }

        return false;
    }

    public static AvifInfo Probe(ReadOnlySpan<byte> data)
    {
        var info = default(AvifInfo);
        if (!IsHeif(data))
        {
            return info;
        }

        var parse = new AvifParse();
        Walk(data, 0, data.Length, 0, parse);
        return Apply(parse) with
        {
            IsAvif = IsAvif(data) || (!HasBrand(data, Heic) && !HasBrand(data, Heix))
        };
    }

    public static AvifInfo Probe(Stream stream) => Probe(stream, sizeOnly: false);

    public static AvifInfo Probe(Stream stream, bool sizeOnly)
    {
        var info = default(AvifInfo);
        if (!stream.CanRead)
        {
            return info;
        }

        if (!stream.CanSeek)
        {
            var take = (int)Math.Min(HdrFile.ProbeByteLimit, Math.Max(0, stream.Length));
            if (take < 16)
            {
                return info;
            }

            var buf = new byte[take];
            var n = stream.Read(buf, 0, take);
            return n > 0 ? Probe(buf.AsSpan(0, n)) : info;
        }

        stream.Position = 0;
        var length = stream.Length;
        var header = new byte[16];
        var sawAvif = false;
        var isAvifBrand = false;
        var parse = new AvifParse();
        long pos = 0;
        while (pos + 8 <= length)
        {
            stream.Position = pos;
            if (stream.Read(header, 0, 8) != 8)
            {
                break;
            }

            var size = (uint)ReadBe32(header, 0);
            var type = (uint)ReadBe32(header, 4);
            long headerLen = 8;
            var boxSize = (long)size;
            if (size == 1)
            {
                if (stream.Read(header, 8, 8) != 8)
                {
                    break;
                }

                boxSize = ReadBe64(header, 8);
                headerLen = 16;
            }
            else if (size == 0)
            {
                boxSize = length - pos;
            }

            if (boxSize < headerLen)
            {
                break;
            }

            var payloadLen = boxSize - headerLen;
            var next = pos + boxSize;
            if (type == Ftyp)
            {
                if (payloadLen < 8 || payloadLen > MaxMetaBytes)
                {
                    return default;
                }

                var ftyp = ReadExact(stream, payloadLen);
                if (ftyp is null)
                {
                    return default;
                }

                var major = (uint)ReadBe32(ftyp, 0);
                sawAvif = IsHeifBrand(major);
                isAvifBrand = IsAvifOnlyBrand(major);
                if (!sawAvif)
                {
                    for (var i = 8; i + 4 <= ftyp.Length; i += 4)
                    {
                        var brand = (uint)ReadBe32(ftyp, i);
                        if (IsHeifBrand(brand))
                        {
                            sawAvif = true;
                        }

                        if (IsAvifOnlyBrand(brand))
                        {
                            isAvifBrand = true;
                        }
                    }
                }

                if (!sawAvif)
                {
                    return default;
                }
            }
            else if (type == Mdat)
            {
                // Skip media; CICP / ispe live in meta.
            }
            else if (payloadLen > 0 && payloadLen <= MaxMetaBytes)
            {
                var payload = ReadExact(stream, payloadLen);
                if (payload is not null)
                {
                    if (type == Meta)
                    {
                        Walk(payload, 4, payload.Length, 1, parse);
                    }
                    else if (type is Iprp or Ipco)
                    {
                        Walk(payload, 0, payload.Length, 1, parse);
                    }
                    else if (type == Pitm)
                    {
                        parse.PrimaryItemId ??= ReadPitm(payload);
                    }
                    else if (type == Ipma)
                    {
                        ReadIpma(payload, parse.Associations);
                    }
                }
            }

            info = Apply(parse) with { IsAvif = isAvifBrand };
            if (sizeOnly && sawAvif && info.Width is > 0 && info.Height is > 0)
            {
                break;
            }

            pos = next;
        }

        return sawAvif ? info with { IsAvif = isAvifBrand } : default;
    }

    public static (int Width, int Height)? TryReadSize(ReadOnlySpan<byte> data)
    {
        var info = Probe(data);
        return info.Width is > 0 && info.Height is > 0 ? (info.Width.Value, info.Height.Value) : null;
    }

    public static (int Width, int Height)? TryReadSize(Stream stream)
    {
        var info = Probe(stream, sizeOnly: true);
        return info.Width is > 0 && info.Height is > 0 ? (info.Width.Value, info.Height.Value) : null;
    }

    public static HdrProbe ToHdrProbe(AvifInfo info)
    {
        var hdrTransfer = info.CicpTransfer is 16 or 18;
        var tenBit = info.BitDepth is > 8;
        var hdrKind = info.IsAvif ? HdrKind.HdrAvif : HdrKind.HdrHeif;
        var wideKind = info.IsAvif ? HdrKind.WideGamutAvif : HdrKind.WideGamutHeif;
        if (hdrTransfer || info.HasMastering || tenBit)
        {
            return new HdrProbe(
                hdrKind,
                info.CicpPrimaries,
                info.CicpTransfer,
                info.MaxCllNits,
                info.CicpMatrix,
                info.FullRange,
                info.BitDepth);
        }

        if (info.CicpPrimaries is 9 or 12)
        {
            return new HdrProbe(
                wideKind,
                info.CicpPrimaries,
                info.CicpTransfer,
                info.MaxCllNits,
                info.CicpMatrix,
                info.FullRange,
                info.BitDepth);
        }

        return HdrProbe.None;
    }

    private sealed class AvifParse
    {
        public int? PrimaryItemId;
        public List<(uint Type, byte[] Payload)> Properties { get; } = [];
        public Dictionary<int, List<int>> Associations { get; } = [];
    }

    private static void Walk(ReadOnlySpan<byte> data, int start, int end, int depth, AvifParse parse)
    {
        if (depth > 8)
        {
            return;
        }

        var i = start;
        while (i + 8 <= end)
        {
            if (!TryBox(data, i, end, out var type, out var payloadStart, out var payloadEnd, out var next))
            {
                break;
            }

            if (type == Meta && payloadEnd - payloadStart >= 4)
            {
                Walk(data, payloadStart + 4, payloadEnd, depth + 1, parse);
            }
            else if (type == Iprp)
            {
                Walk(data, payloadStart, payloadEnd, depth + 1, parse);
            }
            else if (type == Ipco)
            {
                CollectProperties(data, payloadStart, payloadEnd, parse.Properties);
            }
            else if (type == Pitm)
            {
                parse.PrimaryItemId ??= ReadPitm(data[payloadStart..payloadEnd]);
            }
            else if (type == Ipma)
            {
                ReadIpma(data[payloadStart..payloadEnd], parse.Associations);
            }

            i = next;
        }
    }

    private static void CollectProperties(
        ReadOnlySpan<byte> data,
        int start,
        int end,
        List<(uint Type, byte[] Payload)> properties)
    {
        var i = start;
        while (i + 8 <= end)
        {
            if (!TryBox(data, i, end, out var type, out var payloadStart, out var payloadEnd, out var next))
            {
                break;
            }

            properties.Add((type, data[payloadStart..payloadEnd].ToArray()));
            i = next;
        }
    }

    private static AvifInfo Apply(AvifParse parse)
    {
        var info = default(AvifInfo);
        if (parse.Properties.Count == 0)
        {
            return info;
        }

        if (parse.PrimaryItemId is int id
            && parse.Associations.TryGetValue(id, out var indices)
            && indices.Count > 0)
        {
            foreach (var index in indices)
            {
                if (index is >= 1 && index <= parse.Properties.Count)
                {
                    var (type, payload) = parse.Properties[index - 1];
                    ParseProperty(type, payload, ref info);
                }
            }

            return info;
        }

        ApplyFirst(parse.Properties, Ispe, ref info);
        ApplyFirst(parse.Properties, Colr, ref info);
        ApplyFirst(parse.Properties, Av1C, ref info);
        ApplyFirst(parse.Properties, Clli, ref info);
        ApplyFirst(parse.Properties, Mdcv, ref info);
        return info;
    }

    private static void ApplyFirst(
        List<(uint Type, byte[] Payload)> properties,
        uint type,
        ref AvifInfo info)
    {
        foreach (var property in properties)
        {
            if (property.Type == type)
            {
                ParseProperty(property.Type, property.Payload, ref info);
                return;
            }
        }
    }

    private static int? ReadPitm(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 6)
        {
            return null;
        }

        var version = payload[0];
        if (version == 0)
        {
            return ReadBe16(payload, 4);
        }

        return payload.Length >= 8 ? ReadBe32(payload, 4) : null;
    }

    private static void ReadIpma(ReadOnlySpan<byte> payload, Dictionary<int, List<int>> associations)
    {
        if (payload.Length < 8)
        {
            return;
        }

        var version = payload[0];
        var flags = (payload[1] << 16) | (payload[2] << 8) | payload[3];
        var longIndex = (flags & 1) != 0;
        var count = ReadBe32(payload, 4);
        var i = 8;
        for (var entry = 0; entry < count && i < payload.Length; entry++)
        {
            int itemId;
            if (version < 1)
            {
                if (i + 2 > payload.Length)
                {
                    return;
                }

                itemId = ReadBe16(payload, i);
                i += 2;
            }
            else
            {
                if (i + 4 > payload.Length)
                {
                    return;
                }

                itemId = ReadBe32(payload, i);
                i += 4;
            }

            if (i >= payload.Length)
            {
                return;
            }

            var assocCount = payload[i++];
            var list = new List<int>(assocCount);
            for (var a = 0; a < assocCount; a++)
            {
                if (longIndex)
                {
                    if (i + 2 > payload.Length)
                    {
                        return;
                    }

                    list.Add(ReadBe16(payload, i) & 0x7FFF);
                    i += 2;
                }
                else
                {
                    if (i >= payload.Length)
                    {
                        return;
                    }

                    list.Add(payload[i++] & 0x7F);
                }
            }

            associations[itemId] = list;
        }
    }

    private static void ParseProperty(uint type, ReadOnlySpan<byte> payload, ref AvifInfo info)
    {
        if (type == Ispe && payload.Length >= 12)
        {
            var width = ReadBe32(payload, 4);
            var height = ReadBe32(payload, 8);
            if (width > 0 && height > 0)
            {
                info = info with { Width = width, Height = height };
            }

            return;
        }

        if (type == Colr && payload.Length >= 11 && ReadBe32(payload, 0) == Nclx)
        {
            info = info with
            {
                CicpPrimaries = ReadBe16(payload, 4),
                CicpTransfer = ReadBe16(payload, 6),
                CicpMatrix = payload.Length >= 10 ? ReadBe16(payload, 8) : info.CicpMatrix,
                FullRange = payload.Length >= 11 && (payload[10] & 0x80) != 0
            };
            return;
        }

        if (type == Av1C && payload.Length >= 4)
        {
            ParseAv1C(payload, ref info);
            return;
        }

        if (type == Clli && payload.Length >= 2)
        {
            var maxCll = payload.Length >= 6 && payload[0] == 0 && payload[1] == 0 && payload[2] == 0
                ? ReadBe16(payload, 4)
                : ReadBe16(payload, 0);
            if (maxCll > 0)
            {
                info = info with { MaxCllNits = maxCll, HasMastering = true };
            }

            return;
        }

        if (type == Mdcv)
        {
            info = info with { HasMastering = true };
        }
    }

    private static bool TryBox(
        ReadOnlySpan<byte> data,
        int offset,
        int end,
        out uint type,
        out int payloadStart,
        out int payloadEnd,
        out int next)
    {
        type = 0;
        payloadStart = 0;
        payloadEnd = 0;
        next = 0;
        if (offset + 8 > end)
        {
            return false;
        }

        var size = (uint)ReadBe32(data, offset);
        type = (uint)ReadBe32(data, offset + 4);
        var header = 8;
        long boxSize = size;
        if (size == 1)
        {
            if (offset + 16 > end)
            {
                return false;
            }

            boxSize = ReadBe64(data, offset + 8);
            header = 16;
        }
        else if (size == 0)
        {
            boxSize = end - offset;
        }

        if (boxSize < header)
        {
            return false;
        }

        var endOffset = offset + boxSize;
        if (endOffset > end || endOffset > int.MaxValue)
        {
            endOffset = end;
        }

        payloadStart = offset + header;
        payloadEnd = (int)endOffset;
        next = payloadEnd;
        return payloadStart <= payloadEnd;
    }

    private static void ParseAv1C(ReadOnlySpan<byte> payload, ref AvifInfo info)
    {
        var flags = payload[2];
        var highBitdepth = (flags & 0x40) != 0;
        var twelveBit = (flags & 0x20) != 0;
        var bitDepth = highBitdepth ? (twelveBit ? 12 : 10) : 8;
        if (info.BitDepth is null or 0)
        {
            info = info with { BitDepth = bitDepth };
        }

        if (payload.Length <= 4)
        {
            return;
        }

        var profile = payload[1] >> 5;
        TryParseAv1SequenceColor(payload[4..], profile, ref info);
    }

    private static void TryParseAv1SequenceColor(ReadOnlySpan<byte> obu, int av1cProfile, ref AvifInfo info)
    {
        try
        {
            var bits = new Av1Bits(obu);
            if (bits.Read(1) != 0)
            {
                return;
            }

            var type = bits.Read(4);
            var extension = bits.Read(1) != 0;
            var hasSize = bits.Read(1) != 0;
            bits.Read(1);
            if (extension)
            {
                bits.Read(8);
            }

            if (hasSize)
            {
                bits.ReadLeb128();
            }

            if (type != 1)
            {
                return;
            }

            var profile = bits.Read(3);
            var still = bits.Read(1) != 0;
            var reduced = bits.Read(1) != 0;
            _ = still;
            if (reduced)
            {
                bits.Read(5);
            }
            else
            {
                var timing = bits.Read(1) != 0;
                var decoderModel = false;
                if (timing)
                {
                    bits.Read(32);
                    bits.Read(32);
                    var equal = bits.Read(1) != 0;
                    if (equal)
                    {
                        bits.ReadUvlc();
                    }
                    else
                    {
                        var ticks = bits.ReadUvlc();
                        for (var i = 0; i < ticks; i++)
                        {
                            bits.ReadUvlc();
                        }
                    }

                    decoderModel = bits.Read(1) != 0;
                    if (decoderModel)
                    {
                        bits.Read(5);
                        bits.Read(32);
                        bits.Read(5);
                    }
                }

                var initialDelay = bits.Read(1) != 0;
                var opCount = bits.Read(5);
                for (var i = 0; i <= opCount; i++)
                {
                    bits.Read(12);
                    var level = bits.Read(5);
                    if (level > 7)
                    {
                        bits.Read(1);
                    }

                    if (decoderModel)
                    {
                        var present = bits.Read(1) != 0;
                        if (present)
                        {
                            bits.Read(4);
                            bits.Read(4);
                            bits.ReadUvlc();
                        }
                    }

                    if (initialDelay)
                    {
                        var present = bits.Read(1) != 0;
                        if (present)
                        {
                            bits.Read(4);
                        }
                    }
                }
            }

            var widthBits = bits.Read(4) + 1;
            var heightBits = bits.Read(4) + 1;
            bits.Read(widthBits);
            bits.Read(heightBits);
            if (!reduced)
            {
                var frameId = bits.Read(1) != 0;
                if (frameId)
                {
                    bits.Read(4);
                    bits.Read(3);
                }
            }

            bits.Read(1);
            bits.Read(1);
            bits.Read(1);
            if (!reduced)
            {
                bits.Read(1);
                bits.Read(1);
                bits.Read(1);
                bits.Read(1);
                var orderHint = bits.Read(1) != 0;
                if (orderHint)
                {
                    bits.Read(1);
                    bits.Read(1);
                }

                var chooseScreen = bits.Read(1) != 0;
                var forceScreen = 0;
                if (chooseScreen)
                {
                    forceScreen = 2;
                }
                else
                {
                    forceScreen = bits.Read(1);
                }

                var chooseInteger = 0;
                if (forceScreen > 0)
                {
                    chooseInteger = bits.Read(1);
                    if (chooseInteger == 0)
                    {
                        bits.Read(1);
                    }
                }

                if (orderHint)
                {
                    bits.Read(3);
                }
            }

            bits.Read(1);
            bits.Read(1);
            bits.Read(1);

            var highBitdepth = bits.Read(1) != 0;
            if (profile == 2 && highBitdepth)
            {
                bits.Read(1);
            }

            var mono = false;
            if (profile != 1)
            {
                mono = bits.Read(1) != 0;
            }

            var colorPresent = bits.Read(1) != 0;
            var primaries = 2;
            var transfer = 2;
            var matrix = 2;
            if (colorPresent)
            {
                primaries = bits.Read(8);
                transfer = bits.Read(8);
                matrix = bits.Read(8);
                if (info.CicpPrimaries is null)
                {
                    info = info with { CicpPrimaries = primaries };
                }

                if (info.CicpTransfer is null)
                {
                    info = info with { CicpTransfer = transfer };
                }

                if (info.CicpMatrix is null)
                {
                    info = info with { CicpMatrix = matrix };
                }
            }

            // color_config: identity sRGB forces full range and omits the
            // bit. Everything else — including unspecified CICP — carries
            // color_range (0 limited, 1 full).
            bool fullRange;
            if (mono)
            {
                fullRange = bits.Read(1) != 0;
            }
            else if (primaries == 1 && transfer == 13 && matrix == 0)
            {
                fullRange = true;
            }
            else
            {
                fullRange = bits.Read(1) != 0;
            }

            if (info.FullRange is null)
            {
                info = info with { FullRange = fullRange };
            }
        }
        catch
        {
            _ = av1cProfile;
        }
    }

    private struct Av1Bits
    {
        private readonly ReadOnlyMemory<byte> _data;
        private int _bit;

        public Av1Bits(ReadOnlySpan<byte> data)
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
                    throw new InvalidOperationException("AV1 bits");
                }

                var bit = (_data.Span[byteIndex] >> (7 - (_bit % 8))) & 1;
                value = (value << 1) | bit;
                _bit++;
            }

            return value;
        }

        public int ReadLeb128()
        {
            var value = 0;
            for (var i = 0; i < 8; i++)
            {
                var b = Read(8);
                value |= (b & 0x7F) << (7 * i);
                if ((b & 0x80) == 0)
                {
                    return value;
                }
            }

            return value;
        }

        public int ReadUvlc()
        {
            var zeros = 0;
            while (Read(1) == 0)
            {
                zeros++;
                if (zeros > 31)
                {
                    throw new InvalidOperationException("AV1 uvlc");
                }
            }

            return zeros == 0 ? 0 : ((1 << zeros) - 1) + Read(zeros);
        }
    }

    private static bool IsAvifOnlyBrand(uint brand) =>
        brand is Avif or Avis;

    private static bool IsHeifBrand(uint brand) =>
        brand is Avif or Avis or Heic or Heix or Mif1;

    private static byte[]? ReadExact(Stream stream, long count)
    {
        if (count <= 0 || count > MaxMetaBytes)
        {
            return null;
        }

        var buf = new byte[count];
        var n = 0;
        while (n < buf.Length)
        {
            var read = stream.Read(buf, n, buf.Length - n);
            if (read <= 0)
            {
                return null;
            }

            n += read;
        }

        return buf;
    }

    private static uint TypeAt(ReadOnlySpan<byte> data, int offset) =>
        (uint)ReadBe32(data, offset);

    private static int ReadBe16(ReadOnlySpan<byte> data, int offset) =>
        (data[offset] << 8) | data[offset + 1];

    private static int ReadBe32(ReadOnlySpan<byte> data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static long ReadBe64(ReadOnlySpan<byte> data, int offset)
    {
        var hi = (uint)ReadBe32(data, offset);
        var lo = (uint)ReadBe32(data, offset + 4);
        return ((long)hi << 32) | lo;
    }
}
