using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class XamlRootProbeTests
{
    [Fact]
    public void ShouldWrite_OnlyExactTestIdentity()
    {
        Assert.True(XamlRootProbe.ShouldWrite(XamlRootProbe.AllowedTestPackageName));
        Assert.True(XamlRootProbe.ShouldWrite("palaceorganizetest-5e1f"));
        Assert.False(XamlRootProbe.ShouldWrite("C20A7629-6BDE-449F-958A-5AAF8BCE88F3"));
        Assert.False(XamlRootProbe.ShouldWrite("PalaceOrganizeTest"));
        Assert.False(XamlRootProbe.ShouldWrite(null));
        Assert.False(XamlRootProbe.ShouldWrite(""));
    }

    [Fact]
    public void ResolvePath_UsesLocalStateFileName()
    {
        var path = XamlRootProbe.ResolvePath("/tmp/LocalState");
        Assert.Equal(Path.Combine("/tmp/LocalState", XamlRootProbe.FileName), path);
        Assert.Equal("palace-xamlroot.log", XamlRootProbe.FileName);
    }

    [Fact]
    public void FormatLine_AndTryParseLatest_RoundTrip()
    {
        var line = XamlRootProbe.FormatLine(1280.5, 900.25, 1.5, 4242);
        Assert.Contains("XamlRoot.Size=1280.5x900.25", line, StringComparison.Ordinal);
        Assert.Contains("RasterizationScale=1.5", line, StringComparison.Ordinal);
        Assert.Contains("pid=4242", line, StringComparison.Ordinal);

        var text = "noise\n" + line + "\n" + XamlRootProbe.FormatLine(640, 480, 1.0, 7);
        Assert.True(XamlRootProbe.TryParseLatest(text, out var w, out var h, out var scale, out var pid));
        Assert.Equal(640, w);
        Assert.Equal(480, h);
        Assert.Equal(1.0, scale);
        Assert.Equal(7, pid);
    }

    [Fact]
    public void TryParseLatest_RejectsIncompleteLines()
    {
        Assert.False(XamlRootProbe.TryParseLatest("", out _, out _, out _, out _));
        Assert.False(XamlRootProbe.TryParseLatest("XamlRoot.Size=10x10\tRasterizationScale=1", out _, out _, out _, out _));
        Assert.False(XamlRootProbe.TryParseLatest("XamlRoot.Size=10x10\tpid=1", out _, out _, out _, out _));
        Assert.False(XamlRootProbe.TryParseLatest("XamlRoot.Size=10x10\tRasterizationScale=1\tpid=0", out _, out _, out _, out _));
    }
}
