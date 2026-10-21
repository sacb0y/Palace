using System.Text.Json;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SettingsPortableTests
{
    [Fact]
    public void SerializeParse_RoundTripsAllowlistedKeys()
    {
        var doc = SettingsPortable.FromValues(
        [
            new("Theme", "Dark"),
            new(AppearanceSettings.DensityKey, AppearanceSettings.DensityCompact),
            new(PerformanceSettings.ThumbConcurrencyKey, PerformanceSettings.ConcurrencyFewer),
            new("NotExported", "secret"),
            new(MediaCache.AccessTokenKey, "token-should-skip")
        ]);

        var json = SettingsPortable.Serialize(doc);
        Assert.Contains("palace-settings/1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("NotExported", json, StringComparison.Ordinal);
        Assert.DoesNotContain("token-should-skip", json, StringComparison.Ordinal);

        var parsed = SettingsPortable.Parse(json);
        var bag = new Dictionary<string, object>(StringComparer.Ordinal);
        var count = SettingsPortable.ApplyTo(parsed, (k, v) => bag[k] = v);
        Assert.Equal(3, count);
        Assert.Equal("Dark", bag["Theme"]);
        Assert.Equal(AppearanceSettings.DensityCompact, bag[AppearanceSettings.DensityKey]);
        Assert.Equal(PerformanceSettings.ConcurrencyFewer, bag[PerformanceSettings.ThumbConcurrencyKey]);
        Assert.False(bag.ContainsKey(MediaCache.AccessTokenKey));
    }

    [Fact]
    public void Parse_RejectsWrongSchema()
    {
        var bad = """{"schema":"other/1","values":{}}""";
        Assert.Throws<InvalidOperationException>(() => SettingsPortable.Parse(bad));
    }

    [Fact]
    public void ApplyTo_SkipsUnknownKeys()
    {
        var json = """
            {
              "schema": "palace-settings/1",
              "values": {
                "Theme": "Light",
                "CatalogAccessToken": "nope"
              }
            }
            """;
        var doc = SettingsPortable.Parse(json);
        var bag = new Dictionary<string, object>(StringComparer.Ordinal);
        var count = SettingsPortable.ApplyTo(doc, (k, v) => bag[k] = v);
        Assert.Equal(1, count);
        Assert.Equal("Light", bag["Theme"]);
        Assert.False(bag.ContainsKey("CatalogAccessToken"));
    }

    [Fact]
    public void ExportKeys_IncludePerfAndExcludeTokens()
    {
        Assert.Contains(PerformanceSettings.ThumbConcurrencyKey, SettingsPortable.ExportKeys);
        Assert.Contains(PerformanceSettings.PreviewQualityKey, SettingsPortable.ExportKeys);
        Assert.DoesNotContain(MediaCache.AccessTokenKey, SettingsPortable.ExportKeys);
        Assert.DoesNotContain(CatalogStore.AccessTokenKey, SettingsPortable.ExportKeys);
    }

    [Fact]
    public void FromValues_PreservesBoolAndNumber()
    {
        var doc = SettingsPortable.FromValues(
        [
            new(AppearanceSettings.AnimationsKey, false),
            new(MediaCache.MaxMbKey, 4096)
        ]);
        var bag = new Dictionary<string, object>(StringComparer.Ordinal);
        SettingsPortable.ApplyTo(doc, (k, v) => bag[k] = v);
        Assert.Equal(false, bag[AppearanceSettings.AnimationsKey]);
        Assert.Equal(4096, Convert.ToInt32(bag[MediaCache.MaxMbKey]));
        Assert.IsType<JsonElement>(doc.Values![AppearanceSettings.AnimationsKey]);
    }
}
