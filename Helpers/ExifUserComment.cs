using System.Text;

namespace Palace.Helpers;

/// <summary>
/// EXIF UserComment (tag 0x9286): 8-byte character code + encoded text.
/// MetadataExtractor's <c>GetString</c> returns space-separated decimals for
/// the raw UNDEFINEDINED payload — decode the bytes ourselves.
/// </summary>
public static class ExifUserComment
{
    private static readonly Encoding Latin1 = Encoding.Latin1;

    /// <param name="payload">Raw UserComment bytes including the 8-byte code.</param>
    /// <param name="tiffLittleEndian">TIFF/EXIF byte order (Intel = LE). Used for UNICODE when no BOM.</param>
    public static string Decode(ReadOnlySpan<byte> payload, bool tiffLittleEndian = true)
    {
        if (payload.IsEmpty)
        {
            return "";
        }

        if (payload.Length < 8)
        {
            return DecodeUnknown(payload);
        }

        var code = Latin1.GetString(payload[..8]);
        var data = payload[8..];

        if (StartsWithCode(code, "ASCII"))
        {
            return TrimNul(Latin1.GetString(data));
        }

        if (StartsWithCode(code, "UNICODE"))
        {
            return DecodeUnicode(data, tiffLittleEndian);
        }

        if (StartsWithCode(code, "JIS"))
        {
            return DecodeJis(data);
        }

        if (IsAllNul(code))
        {
            return DecodeUnknown(data.IsEmpty ? payload : data);
        }

        // Unknown / non-spec writers sometimes omit the header entirely.
        return DecodeUnknown(payload);
    }

    public static string Decode(byte[]? payload, bool tiffLittleEndian = true) =>
        payload is null || payload.Length == 0 ? "" : Decode(payload.AsSpan(), tiffLittleEndian);

    private static bool StartsWithCode(string code, string prefix) =>
        code.StartsWith(prefix, StringComparison.Ordinal);

    private static bool IsAllNul(string code)
    {
        foreach (var c in code)
        {
            if (c != '\0')
            {
                return false;
            }
        }

        return true;
    }

    private static string DecodeUnicode(ReadOnlySpan<byte> data, bool tiffLittleEndian)
    {
        if (data.IsEmpty)
        {
            return "";
        }

        var littleEndian = tiffLittleEndian;
        var body = data;
        if (data.Length >= 2)
        {
            if (data[0] == 0xFF && data[1] == 0xFE)
            {
                littleEndian = true;
                body = data[2..];
            }
            else if (data[0] == 0xFE && data[1] == 0xFF)
            {
                littleEndian = false;
                body = data[2..];
            }
        }

        var primaryEnc = littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode;
        var altEnc = littleEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
        var primary = primaryEnc.GetString(body);
        if (body.Length < 2)
        {
            return TrimNul(primary);
        }

        var alt = altEnc.GetString(body);
        return TrimNul(PickUnicodeDecode(primary, alt));
    }

    /// <summary>
    /// Prefer the endian that yields A1111 markers or denser ASCII. Mis-reading
    /// LE as BE turns ASCII into CJK-looking BMP chars with no NULs, so a
    /// NUL-ratio check alone is not enough.
    /// </summary>
    private static string PickUnicodeDecode(string primary, string alt)
    {
        var primaryA1111 = LooksLikeA1111(primary);
        var altA1111 = LooksLikeA1111(alt);
        if (primaryA1111 && !altA1111)
        {
            return primary;
        }

        if (altA1111 && !primaryA1111)
        {
            return alt;
        }

        return AsciiScore(alt) > AsciiScore(primary) ? alt : primary;
    }

    private static string DecodeJis(ReadOnlySpan<byte> data)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return TrimNul(Encoding.GetEncoding(50222).GetString(data)); // ISO-2022-JP
        }
        catch
        {
            return DecodeUnknown(data);
        }
    }

    private static string DecodeUnknown(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return "";
        }

        // UTF-8 first (reject invalid sequences), then Latin-1.
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return TrimNul(utf8.GetString(data));
        }
        catch (DecoderFallbackException)
        {
            return TrimNul(Latin1.GetString(data));
        }
    }

    private static bool LooksLikeA1111(string text) =>
        !string.IsNullOrEmpty(text)
        && (text.Contains("Negative prompt:", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Steps:", StringComparison.OrdinalIgnoreCase));

    private static int AsciiScore(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var score = 0;
        foreach (var c in text)
        {
            if (c is >= (char)0x20 and <= (char)0x7E or '\n' or '\r' or '\t')
            {
                score++;
            }
            else if (c == '\0')
            {
                score -= 2;
            }
            else
            {
                score--;
            }
        }

        return score;
    }

    private static string TrimNul(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return text.TrimEnd('\0').Trim();
    }
}
