using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Bundled OpenEXR (palace_exr) against the fixtures from
/// tools/openexr/make_exr_fixtures.py: one file per compression, float
/// channels, tiled (incl. mipmapped), multi-part, luminance and layered.
/// On Windows the library ships next to the tests and must load; elsewhere
/// these run only when a libpalace_exr build was dropped into the test
/// output (tools/openexr/build-openexr.sh out linux).
/// </summary>
public sealed class ExrNativeTests
{
    private const int W = 37;
    private const int H = 23;

    private static bool NativeOrSkip()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.True(ExrNative.IsAvailable, "palace_exr.dll should ship next to the tests on Windows");
        }

        return ExrNative.IsAvailable;
    }

    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "exr", name));

    /// <summary>Same formula as make_exr_fixtures.py (data-window relative x, y).</summary>
    private static (float R, float G, float B, float A) ExrFixtureValue(int x, int y) =>
        (0.02f + (0.2f * x), 0.05f * y, 0.25f + (0.5f * ((x + y) % 3)), (x + 1) / (float)W);

    private static void AssertMatches(ExrFile.Frame frame, double maxRel, double meanRel, bool alpha = true, bool grey = false)
    {
        Assert.Equal((W, H), (frame.Width, frame.Height));
        double sum = 0;
        var worst = 0.0;
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                var (r, g, b, a) = ExrFixtureValue(x, y);
                if (grey)
                {
                    r = g;
                    b = g;
                }

                if (!alpha)
                {
                    a = 1f;
                }

                var o = ((y * W) + x) * 4;
                float[] want = [r, g, b, a];
                for (var c = 0; c < 4; c++)
                {
                    var rel = Math.Abs(frame.Rgba[o + c] - want[c]) / (Math.Abs(want[c]) + 0.05);
                    worst = Math.Max(worst, rel);
                    sum += rel;
                }
            }
        }

        Assert.True(worst <= maxRel, $"max rel error {worst}");
        Assert.True(sum / (W * H * 4) <= meanRel, $"mean rel error {sum / (W * H * 4)}");
    }

    [Theory]
    // Lossless (half precision only).
    [InlineData("rgba-half-none.exr", "none", 0.002, 0.0005)]
    [InlineData("rgba-half-rle.exr", "rle", 0.002, 0.0005)]
    [InlineData("rgba-half-zips.exr", "zips", 0.002, 0.0005)]
    [InlineData("rgba-half-zip.exr", "zip", 0.002, 0.0005)]
    [InlineData("rgba-half-piz.exr", "piz", 0.002, 0.0005)]
    [InlineData("rgba-half-pxr24.exr", "pxr24", 0.002, 0.0005)]
    [InlineData("rgba-half-zstd.exr", "zstd", 0.002, 0.0005)]
    [InlineData("rgba-half-htj2k256.exr", "htj2k256", 0.002, 0.0005)]
    [InlineData("rgba-float-zip.exr", "zip", 1e-6, 1e-7)]
    [InlineData("rgba-float-piz.exr", "piz", 1e-6, 1e-7)]
    // Lossy.
    [InlineData("rgba-float-pxr24.exr", "pxr24", 1e-4, 1e-5)]
    [InlineData("rgba-half-b44.exr", "b44", 0.08, 0.005)]
    [InlineData("rgba-half-b44a.exr", "b44a", 0.08, 0.005)]
    [InlineData("rgba-half-dwaa.exr", "dwaa", 0.06, 0.01)]
    [InlineData("rgba-half-dwab.exr", "dwab", 0.06, 0.01)]
    public void EveryCompression_DecodesThroughExrFile(string file, string compression, double maxRel, double meanRel)
    {
        if (!NativeOrSkip())
        {
            return;
        }

        var data = Fixture(file);
        var info = ExrNative.TryInfo(data)!.Value;
        Assert.Equal(compression, ExrFile.CompressionName(info.Compression));
        var frame = ExrFile.TryDecode(data)!;
        Assert.Equal(compression, frame.Compression);
        AssertMatches(frame, maxRel, meanRel);

        // Probe, size and HDR decode all go through the same reader.
        Assert.True(ExrFile.IsDecodable(data));
        Assert.Equal((W, H), ExrFile.TryReadSize(data));
        var probe = HdrFile.Probe(data);
        Assert.Equal(HdrKind.HdrExr, probe.Kind);
        Assert.True(probe.CanPresentHdr);
        var scrgb = ExrFile.TryDecodeScrgb(data)!;
        Assert.Equal(frame.Rgba[0], scrgb.ScrgbRgba[0]);
        Assert.True(scrgb.MaxScrgb > 7f);
    }

    [Theory]
    [InlineData("tiled-half-piz.exr", 0.002, 0.0005)]
    [InlineData("tiled-half-dwaa.exr", 0.06, 0.01)]
    [InlineData("tiled-mip-half-zip.exr", 0.002, 0.0005)]
    public void Tiled_DecodesLevelZeroWithEdgeTiles(string file, double maxRel, double meanRel)
    {
        if (!NativeOrSkip())
        {
            return;
        }

        var data = Fixture(file);
        Assert.True(ExrNative.TryInfo(data)!.Value.Tiled);
        Assert.Null(ExrFile.TryDecodeInHouse(data));
        AssertMatches(ExrFile.TryDecode(data)!, maxRel, meanRel);
        Assert.Equal(HdrKind.HdrExr, HdrFile.Probe(data).Kind);
    }

    [Fact]
    public void Multipart_PicksTheFirstColourPart()
    {
        if (!NativeOrSkip())
        {
            return;
        }

        var data = Fixture("multipart-depth-then-rgba.exr");
        var info = ExrNative.TryInfo(data)!.Value;
        Assert.True(info.Multipart);
        Assert.Equal("piz", ExrFile.CompressionName(info.Compression));
        AssertMatches(ExrFile.TryDecode(data)!, 0.002, 0.0005);
    }

    [Fact]
    public void LuminanceAndLayeredChannels()
    {
        if (!NativeOrSkip())
        {
            return;
        }

        var y = ExrFile.TryDecode(Fixture("luminance-y-zip.exr"))!;
        Assert.True((ExrNative.TryInfo(Fixture("luminance-y-zip.exr"))!.Value.Flags & ExrNative.FlagLuminance) != 0);
        AssertMatches(y, 0.002, 0.0005, alpha: false, grey: true);

        // diffuse.R/G/B is picked; specular.R is ignored.
        AssertMatches(ExrFile.TryDecode(Fixture("layer-diffuse-zip.exr"))!, 0.002, 0.0005, alpha: false);
    }

    [Fact]
    public void ProbeHead_IsEnoughForInfo()
    {
        if (!NativeOrSkip())
        {
            return;
        }

        // HdrFile.Probe(Stream) sees at most ProbeByteLimit bytes.
        var data = Fixture("rgba-half-dwab.exr");
        var head = data.AsSpan(0, Math.Min(data.Length, 400));
        Assert.Equal((W, H), ExrNative.TryInfo(head)!.Value is var i ? (i.Width, i.Height) : default);
    }

    [Fact]
    public void Garbage_ReturnsNullNotThrow()
    {
        if (!NativeOrSkip())
        {
            return;
        }

        var data = Fixture("rgba-half-piz.exr");
        var cut = data.AsSpan(0, data.Length / 2).ToArray();
        Assert.Null(ExrFile.TryDecode(cut));
        var noise = new byte[512];
        new Random(7).NextBytes(noise);
        BitConverter.GetBytes(ExrFile.Magic).CopyTo(noise, 0);
        Assert.Null(ExrNative.TryInfo(noise));
        Assert.Null(ExrFile.TryDecode(noise));
        Assert.StartsWith("OpenEXR 3.", ExrNative.Version);
    }

    [Fact]
    public void InHouseReader_StillDecodesItsSubset()
    {
        foreach (var name in new[] { "rgba-half-none.exr", "rgba-half-rle.exr", "rgba-half-zips.exr", "rgba-half-zip.exr" })
        {
            var data = Fixture(name);
            Assert.True(ExrFile.IsDecodableInHouse(data));
            AssertMatches(ExrFile.TryDecodeInHouse(data)!, 0.002, 0.0005);
        }

        Assert.False(ExrFile.IsDecodableInHouse(Fixture("rgba-half-piz.exr")));
        Assert.Null(ExrFile.TryDecodeInHouse(Fixture("rgba-half-dwaa.exr")));
    }
}
