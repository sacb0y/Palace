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
    bool HasMastering);

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

    public static bool IsAvif(ReadOnlySpan<byte> data)
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
        if (IsAvifBrand(TypeAt(data, 8)))
        {
            return true;
        }

        for (var i = 16; i + 4 <= boxEnd; i += 4)
        {
            if (IsAvifBrand(TypeAt(data, i)))
            {
                return true;
            }
        }

        return false;
    }

    public static AvifInfo Probe(ReadOnlySpan<byte> data)
    {
        var info = default(AvifInfo);
        if (!IsAvif(data))
        {
            return info;
        }

        var parse = new AvifParse();
        Walk(data, 0, data.Length, 0, parse);
        return Apply(parse);
    }

    public static AvifInfo Probe(Stream stream)
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

                sawAvif = IsAvifBrand(ReadBe32(ftyp, 0));
                if (!sawAvif)
                {
                    for (var i = 8; i + 4 <= ftyp.Length; i += 4)
                    {
                        if (IsAvifBrand(ReadBe32(ftyp, i)))
                        {
                            sawAvif = true;
                            break;
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

            info = Apply(parse);
            if (sawAvif && info.Width is > 0 && (info.CicpTransfer is 16 or 18 || info.HasMastering))
            {
                break;
            }

            pos = next;
        }

        return sawAvif ? info : default;
    }

    public static (int Width, int Height)? TryReadSize(ReadOnlySpan<byte> data)
    {
        var info = Probe(data);
        return info.Width is > 0 && info.Height is > 0 ? (info.Width.Value, info.Height.Value) : null;
    }

    public static (int Width, int Height)? TryReadSize(Stream stream)
    {
        var info = Probe(stream);
        return info.Width is > 0 && info.Height is > 0 ? (info.Width.Value, info.Height.Value) : null;
    }

    public static HdrProbe ToHdrProbe(AvifInfo info)
    {
        var hdrTransfer = info.CicpTransfer is 16 or 18;
        if (hdrTransfer || info.HasMastering)
        {
            return new HdrProbe(HdrKind.HdrAvif, info.CicpPrimaries, info.CicpTransfer, info.MaxCllNits);
        }

        if (info.CicpPrimaries is 9 or 12)
        {
            return new HdrProbe(HdrKind.WideGamutAvif, info.CicpPrimaries, info.CicpTransfer, info.MaxCllNits);
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
                CicpTransfer = ReadBe16(payload, 6)
            };
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

    private static bool IsAvifBrand(int brand) =>
        (uint)brand is Avif or Avis;

    private static bool IsAvifBrand(uint brand) =>
        brand is Avif or Avis;

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
