using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SettingsConfirmTests
{
    [Fact]
    public void RemoveFolderBody_IncludesAssetCount_AndDiskUntouched()
    {
        var body = SettingsConfirm.RemoveFolderBody(@"D:\Art", 12);
        Assert.Contains("12 catalog assets", body);
        Assert.Contains(@"D:\Art", body);
        Assert.Contains("Files on disk are not touched", body);
    }

    [Fact]
    public void RemoveFolderBody_SingularAsset()
    {
        var body = SettingsConfirm.RemoveFolderBody("/photos", 1);
        Assert.Contains("1 catalog asset", body);
        Assert.DoesNotContain("1 catalog assets", body);
    }

    [Fact]
    public void RemoveFolderBody_ZeroAssets()
    {
        var body = SettingsConfirm.RemoveFolderBody("empty", 0);
        Assert.Contains("0 catalog assets", body);
        Assert.Contains("Files on disk are not touched", body);
    }

    [Fact]
    public void ClearCacheBody_MentionsThumbsNotOriginals()
    {
        var body = SettingsConfirm.ClearCacheBody();
        Assert.Contains("Thumbnail", body);
        Assert.Contains("originals", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResetCatalogBody_MentionsRestart()
    {
        var body = SettingsConfirm.ResetCatalogBody();
        Assert.Contains("restart", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not deleted", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("OneDrive")]
    [InlineData("Dropbox")]
    public void Disconnect_TitlesAndBodies(string provider)
    {
        Assert.Equal("Disconnect " + provider, SettingsConfirm.DisconnectTitle(provider));
        var body = SettingsConfirm.DisconnectBody(provider);
        Assert.Contains(provider, body);
        Assert.Contains("sign-in", body);
    }
}
