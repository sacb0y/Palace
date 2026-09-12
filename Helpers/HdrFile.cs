using System.Text;

namespace Palace.Helpers;

public enum HdrKind
{
    None,
    UltraHdrJpeg,
    HdrPng,
    WideGamutPng,
    HdrAvif,
    WideGamutAvif
}

public readonly record struct HdrProbe(
    HdrKind Kind,
    int? CicpPrimaries,
    int? CicpTransfer,
    int? MaxCllNits)
{
    public static HdrProbe None { get; } = new(HdrKind.None, null, null, null);

    public bool IsHdr => Kind is HdrKind.UltraHdrJpeg or HdrKind.HdrPng or HdrKind.HdrAvif;

    /// <summary>
    /// HDR PNG / AVIF can be presented from WIC pixels + cICP. Ultra HDR JPEG
    /// needs a gain-map decoder we do not ship (no libultrahdr / Magick).
    /// </summary>
    public bool CanPresentHdr => Kind is HdrKind.HdrPng or HdrKind.HdrAvif;

    public bool IsPq => CicpTransfer == 16;

    public bool IsHlg => CicpTransfer == 18;

    /// <summary>
    /// PQ/HLG only when cICP says so. cLLi/mDCv without transfer is sRGB
    /// (or linear if transfer 8) — treating that as PQ crushes the image.
    /// </summary>
    public HdrTransfer Transfer => CicpTransfer switch
    {
        16 => HdrTransfer.Pq,
        18 => HdrTransfer.Hlg,
        8 => HdrTransfer.Linear,
        _ => HdrTransfer.Srgb
    };
}

public enum HdrTransfer
{
    Srgb,
    Linear,
    Pq,
    Hlg
}

/// <summary>
/// Header-only HDR / wide-gamut detection. Never opens online-only files.
/// </summary>
public static class HdrFile
{
    public const int ProbeByteLimit = 256 * 1024;

    public static HdrProbe ProbePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !CloudFile.Exists(path)
            || CloudFile.IsOnlineOnly(path))
        {
            return HdrProbe.None;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Probe(stream);
        }
        catch
        {
            return HdrProbe.None;
        }
    }

    public static HdrProbe Probe(Stream stream)
    {
        if (!stream.CanRead)
        {
            return HdrProbe.None;
        }

        var take = ProbeByteLimit;
        if (stream.CanSeek)
        {
            stream.Position = 0;
            take = (int)Math.Min(stream.Length, ProbeByteLimit);
        }

        if (take < 10)
        {
            return HdrProbe.None;
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
            Span<byte> head = stackalloc byte[32];
            var headN = stream.Read(head);
            stream.Position = 0;
            if (headN >= 16 && AvifFile.IsAvif(head[..headN]))
            {
                return AvifFile.ToHdrProbe(AvifFile.Probe(stream));
            }
        }

        var buf = new byte[take];
        var n = stream.Read(buf, 0, take);
        return n > 0 ? Probe(buf.AsSpan(0, n)) : HdrProbe.None;
    }

    public static HdrProbe Probe(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8
            && data[0] == 0x89
            && data[1] == (byte)'P'
            && data[2] == (byte)'N'
            && data[3] == (byte)'G')
        {
            return ProbePng(data);
        }

        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            return ProbeJpeg(data);
        }

        if (AvifFile.IsAvif(data))
        {
            return AvifFile.ToHdrProbe(AvifFile.Probe(data));
        }

        return HdrProbe.None;
    }

    private static HdrProbe ProbePng(ReadOnlySpan<byte> data)
    {
        var i = 8;
        int? primaries = null;
        int? transfer = null;
        int? maxCll = null;
        var hasCicp = false;
        var hasMastering = false;

        while (i + 12 <= data.Length)
        {
            var len = ReadBigEndian32(data, i);
            if (len < 0 || i + 12 + len > data.Length)
            {
                break;
            }

            var type = Encoding.ASCII.GetString(data.Slice(i + 4, 4));
            var payload = data.Slice(i + 8, len);
            if (type == "cICP" && payload.Length >= 3)
            {
                hasCicp = true;
                primaries = payload[0];
                transfer = payload[1];
            }
            else if (type is "cLLi" or "cLLI" && payload.Length >= 4)
            {
                hasMastering = true;
                maxCll = (int)Math.Round(ReadBigEndian32(payload, 0) / 10000.0);
            }
            else if (type is "mDCv" or "mDCV")
            {
                hasMastering = true;
            }
            else if (type is "IDAT" or "IEND")
            {
                break;
            }

            i += 12 + len;
        }

        var hdrTransfer = transfer is 16 or 18;
        if (hdrTransfer || hasMastering)
        {
            return new HdrProbe(HdrKind.HdrPng, primaries, transfer, maxCll);
        }

        if (hasCicp && primaries is 9 or 12)
        {
            return new HdrProbe(HdrKind.WideGamutPng, primaries, transfer, maxCll);
        }

        return HdrProbe.None;
    }

    private static HdrProbe ProbeJpeg(ReadOnlySpan<byte> data)
    {
        var ascii = Encoding.ASCII.GetString(data);
        if (ascii.Contains("hdrgm:Version", StringComparison.Ordinal)
            || ascii.Contains("hdrgm:Version", StringComparison.OrdinalIgnoreCase)
            || ascii.Contains("http://ns.adobe.com/hdr-gain-map/1.0/", StringComparison.Ordinal)
            || ascii.Contains("urn:iso:std:iso:ts:21496:-1", StringComparison.Ordinal))
        {
            return new HdrProbe(HdrKind.UltraHdrJpeg, null, null, null);
        }

        return HdrProbe.None;
    }

    private static int ReadBigEndian32(ReadOnlySpan<byte> data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
