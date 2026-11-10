using Palace.Imaging;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// P5: reference HLSL under Palace.Imaging/Shaders must stay aligned with the
/// CPU tonemap (constants + nonzero-EV LUT bypass contract).
/// </summary>
public class ImagingHlslAgreementTests
{
    [Fact]
    public void ToneMapHlsl_CarriesCpuContractMarkers()
    {
        var path = FindShader();
        Assert.True(File.Exists(path), $"missing {path}");
        var text = File.ReadAllText(path);
        Assert.Contains("RefWhiteNits = 203.0", text, StringComparison.Ordinal);
        Assert.Contains("MaxPaperDip = 0.10", text, StringComparison.Ordinal);
        Assert.Contains("PalaceBt2390MapLinear", text, StringComparison.Ordinal);
        Assert.Contains("PalaceSdrMap", text, StringComparison.Ordinal);
        Assert.Contains("PalaceLutOrMap", text, StringComparison.Ordinal);
        Assert.Contains("abs(exposureEv) < 1e-6", text, StringComparison.Ordinal);
        Assert.Contains("PipelineVersion = 6", text, StringComparison.Ordinal);
        Assert.Equal(6, ImagingConstants.PipelineVersion);
        Assert.Equal(203f, ImagingConstants.RefWhiteNits);
        Assert.Equal(0.10f, SdrToneCurve.MaxPaperDip);
    }

    [Fact]
    public void CpuHdrMapScalar_MatchesBt2390Helper()
    {
        var stats = new ImageStats(0.4f, 8f, 8f, 8f, 8f, 1f, null, false, 0);
        var display = DisplayTarget.Hdr(1000f);
        var p = new ToneMapParams(stats, display, 0f);
        for (var i = 0; i <= 16; i++)
        {
            var x = (i / 16f) * stats.Lc;
            var y = ToneMapper.MapScalar(x, p);
            var eetf = Bt2390Eetf.MapLinear(x, stats.Lc, display.Ld);
            Assert.InRange(y, eetf - 1e-5f, eetf + 1e-5f);
        }
    }

    private static string FindShader()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Palace.Imaging", "Shaders", "ToneMap.hlsl");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Palace.Imaging", "Shaders", "ToneMap.hlsl"));
    }
}
