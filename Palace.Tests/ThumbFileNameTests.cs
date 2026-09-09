using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ThumbFileNameTests
{
    [Fact]
    public void Sanitize_HexHash_IsUnchanged()
    {
        const string hash = "aabbccddeeff00112233445566778899";
        Assert.Equal(hash, ThumbFileName.Sanitize(hash));
        Assert.Equal(hash + ".jpg", ThumbFileName.FileName(hash));
    }

    [Fact]
    public void Sanitize_CloudStub_ReplacesColon()
    {
        var hash = "cloud:" + new string('a', 64);
        var safe = ThumbFileName.Sanitize(hash);
        Assert.DoesNotContain(':', safe);
        Assert.StartsWith("cloud_", safe);
        Assert.Equal(safe + ".jpg", ThumbFileName.FileName(hash));
        Assert.Equal(safe + "_lg.jpg", ThumbFileName.FileName(hash, "_lg"));
    }

    [Fact]
    public void Sanitize_PathSeparators_BecomeUnderscores()
    {
        Assert.Equal("a_b_c", ThumbFileName.Sanitize("a/b\\c"));
    }
}
