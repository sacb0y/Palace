using System.Runtime.Versioning;
using Palace.Helpers;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Real libjxl fixture: tiny PQ JXL through <see cref="JxlDecode.TryDecodeFile"/>.
/// Skips when Starward natives are missing (Linux Cloud Agents).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class JxlDecodeFixtureTests
{
    private static string TinyPqPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "jxl", "tiny-pq.jxl");

    [Fact]
    public void TinyPqFixture_ProbesAsHdrJxlPqBt2020()
    {
        var bytes = File.ReadAllBytes(TinyPqPath);
        Assert.True(StillFormats.IsJxl(bytes));
        var probe = HdrFile.Probe(bytes);
        Assert.Equal(HdrKind.HdrJxl, probe.Kind);
        Assert.Equal(HdrTransfer.Pq, probe.Transfer);
        Assert.Equal(9, probe.CicpPrimaries);
    }

    [Fact]
    public void TinyPqFixture_TryDecodeFile_ReportsTransferScaleAndPixelNits()
    {
        var path = TinyPqPath;
        var probe = HdrFile.ProbePath(path);
        Assert.Equal(HdrKind.HdrJxl, probe.Kind);

        var frame = JxlDecode.TryDecodeFile(
            path,
            HdrJxlPresent.PresentMaxPixels,
            probe.Transfer,
            probe.CicpPrimaries);

        if (frame is null)
        {
            // Linux / missing jxl.dll — fixture + probe still covered above.
            Assert.False(OperatingSystem.IsWindows(),
                "Windows CI ships Starward libjxl natives; TryDecodeFile must not return null");
            return;
        }

        Assert.True(frame.Width > 0 && frame.Height > 0);
        Assert.True(frame.Rgba.Length >= 4);
        Assert.True(
            frame.BufferTransfer is JxlBufferTransfer.Linear or JxlBufferTransfer.Pq,
            $"unexpected buffer={frame.BufferTransfer}");

        // One decoded pixel in nits (scRGB 1.0 = 80). Gray PQ fixture → finite mid.
        var nits = frame.Rgba[0] * GalleryPresent.ScrgbNits;
        Assert.True(float.IsFinite(nits), $"pixel nits not finite: {nits}");
        Assert.InRange(nits, 1f, 10_000f);

        if (frame.BufferTransfer == JxlBufferTransfer.Pq)
        {
            Assert.Equal(0f, frame.ScaleApplied);
        }
        else
        {
            var expect = HdrJxlPresent.ScrgbScaleFromIntensityTarget(
                frame.IntensityTarget, HdrTransfer.Pq);
            Assert.Equal(expect, frame.ScaleApplied, 3);
        }
    }
}
