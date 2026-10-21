using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

[Collection("PerformanceSettingsStatic")]
public sealed class PerformanceSettingsTests
{
    public PerformanceSettingsTests() =>
        PerformanceSettings.Apply(
            PerformanceSettings.DefaultConcurrency,
            PerformanceSettings.DefaultPreviewQuality);

    [Fact]
    public void Normalize_FallsBackToDefaults()
    {
        Assert.Equal(PerformanceSettings.ConcurrencyAuto, PerformanceSettings.NormalizeConcurrency(null));
        Assert.Equal(PerformanceSettings.ConcurrencyAuto, PerformanceSettings.NormalizeConcurrency("nope"));
        Assert.Equal(PerformanceSettings.ConcurrencyFewer, PerformanceSettings.NormalizeConcurrency("fewer"));
        Assert.Equal(PerformanceSettings.QualityStandard, PerformanceSettings.NormalizePreviewQuality(null));
        Assert.Equal(PerformanceSettings.QualityHigh, PerformanceSettings.NormalizePreviewQuality("High"));
    }

    [Theory]
    [InlineData(8, PerformanceSettings.ConcurrencyAuto, 4)]
    [InlineData(4, PerformanceSettings.ConcurrencyAuto, 3)]
    [InlineData(2, PerformanceSettings.ConcurrencyAuto, 2)]
    [InlineData(16, PerformanceSettings.ConcurrencyFewer, 2)]
    [InlineData(2, PerformanceSettings.ConcurrencyMore, 4)]
    public void ResolveConcurrency_HonorsMode(int cores, string mode, int expected) =>
        Assert.Equal(expected, PerformanceSettings.ResolveConcurrency(cores, mode));

    [Fact]
    public void ResolveMaxDecodePixels_StandardVsHigh()
    {
        Assert.Equal(
            ThumbScheduler.MaxDecodePixels,
            PerformanceSettings.ResolveMaxDecodePixels(PerformanceSettings.QualityStandard));
        Assert.Equal(
            PerformanceSettings.HighMaxDecodePixels,
            PerformanceSettings.ResolveMaxDecodePixels(PerformanceSettings.QualityHigh));
    }

    [Fact]
    public void Apply_UpdatesStaticsAndEffectiveCap()
    {
        PerformanceSettings.Apply(PerformanceSettings.ConcurrencyFewer, PerformanceSettings.QualityHigh);
        Assert.Equal(PerformanceSettings.ConcurrencyFewer, PerformanceSettings.ThumbConcurrency);
        Assert.Equal(PerformanceSettings.QualityHigh, PerformanceSettings.PreviewQuality);
        Assert.Equal(2, ThumbScheduler.EffectiveConcurrencyCap(16));
        Assert.Equal(PerformanceSettings.HighMaxDecodePixels, ThumbScheduler.EffectiveMaxDecodePixels);
    }
}
