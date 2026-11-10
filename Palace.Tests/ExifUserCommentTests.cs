using System.Text;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ExifUserCommentTests
{
    [Fact]
    public void Decode_UnicodeHeader_Utf16Le()
    {
        var text = "a cat, detailed\nNegative prompt: blurry\nSteps: 20, Sampler: Euler a, CFG scale: 7, Seed: 42, Size: 512x512, Model: sdxl";
        var payload = BuildUnicode(text, littleEndian: true, bom: false);

        var decoded = ExifUserComment.Decode(payload, tiffLittleEndian: true);

        Assert.Equal(text, decoded);
    }

    [Fact]
    public void Decode_UnicodeHeader_Utf16Be()
    {
        var text = "portrait\nNegative prompt: lowres\nSteps: 28, Seed: 99, Model: pony";
        var payload = BuildUnicode(text, littleEndian: false, bom: false);

        var decoded = ExifUserComment.Decode(payload, tiffLittleEndian: false);

        Assert.Equal(text, decoded);
    }

    [Fact]
    public void Decode_UnicodeHeader_BomOverridesTiffEndian()
    {
        var text = "Steps: 10, Seed: 1";
        // Payload is BE with BOM even though TIFF says LE.
        var payload = BuildUnicode(text, littleEndian: false, bom: true);

        var decoded = ExifUserComment.Decode(payload, tiffLittleEndian: true);

        Assert.Equal(text, decoded);
    }

    [Fact]
    public void Decode_AsciiHeader()
    {
        var text = "hello ASCII comment";
        var payload = BuildHeader("ASCII\0\0\0", Encoding.Latin1.GetBytes(text));

        Assert.Equal(text, ExifUserComment.Decode(payload));
    }

    [Fact]
    public void Decode_AllZeroHeader_FallsBackUtf8()
    {
        var text = "utf8 fallback ✓";
        var payload = BuildHeader(new string('\0', 8), Encoding.UTF8.GetBytes(text));

        Assert.Equal(text, ExifUserComment.Decode(payload));
    }

    [Fact]
    public void Decode_UnknownHeader_TriesWholePayloadAsUtf8()
    {
        var text = "no header here";
        var decoded = ExifUserComment.Decode(Encoding.UTF8.GetBytes(text));
        Assert.Equal(text, decoded);
    }

    [Fact]
    public void Decode_ReproBytes_EmptyPromptStartsAtSteps()
    {
        // Matches the Generation panel bug: UNICODE\0 + UTF-16LE "\nSteps: …"
        // (empty positive prompt). Palace used to print "85 78 73 67…" decimals.
        var text =
            "\nSteps: 30, Sampler: DPM++ 2M Karras, CFG scale: 7, Seed: 1234567890, Size: 768x1152, Model hash: abcd1234, Model: demo_model";
        var payload = BuildUnicode(text, littleEndian: true, bom: false);

        // Spot-check the header bytes from the screenshot.
        Assert.Equal([85, 78, 73, 67, 79, 68, 69, 0], payload.AsSpan(0, 8).ToArray());
        Assert.Equal(10, payload[8]); // '\n'
        Assert.Equal(0, payload[9]);

        var decoded = ExifUserComment.Decode(payload, tiffLittleEndian: true);
        Assert.Equal(text.Trim(), decoded);

        var parsed = A1111Parameters.Parse(decoded);
        Assert.Equal("", parsed.Prompt);
        Assert.Null(parsed.NegativePrompt);
        Assert.Equal("1234567890", parsed.Seed);
        Assert.Equal("30", parsed.Steps);
        Assert.Equal("DPM++ 2M Karras", parsed.Sampler);
        Assert.Equal("demo_model", parsed.Model);
        Assert.Equal("7", parsed.CfgScale);
        Assert.Equal("768x1152", parsed.Size);
    }

    [Fact]
    public void Decode_WrongTiffEndian_RecoversViaHeuristic()
    {
        var text = "\nSteps: 12, Seed: 5, Model: recover";
        var payload = BuildUnicode(text, littleEndian: true, bom: false);

        // Claim BE even though bytes are LE — LooksLikeText should flip.
        var decoded = ExifUserComment.Decode(payload, tiffLittleEndian: false);
        Assert.Contains("Steps:", decoded, StringComparison.Ordinal);
        Assert.Equal("5", A1111Parameters.Parse(decoded).Seed);
    }

    private static byte[] BuildUnicode(string text, bool littleEndian, bool bom)
    {
        var encoding = littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode;
        var body = encoding.GetBytes(text);
        if (bom)
        {
            var preamble = encoding.GetPreamble();
            var withBom = new byte[preamble.Length + body.Length];
            preamble.CopyTo(withBom, 0);
            body.CopyTo(withBom, preamble.Length);
            body = withBom;
        }

        return BuildHeader("UNICODE\0", body);
    }

    private static byte[] BuildHeader(string code8, byte[] data)
    {
        var code = Encoding.Latin1.GetBytes(code8);
        Assert.Equal(8, code.Length);
        var payload = new byte[8 + data.Length];
        code.CopyTo(payload, 0);
        data.CopyTo(payload, 8);
        return payload;
    }
}
