using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class AssignedTagChromeTests
{
    [Theory]
    [InlineData(false, TagSource.Manual, 1.0)]
    [InlineData(true, TagSource.Manual, 0.75)]
    [InlineData(false, TagSource.Implied, 0.62)]
    [InlineData(true, TagSource.Implied, 0.62)]
    [InlineData(false, TagSource.Prompt, 1.0)]
    public void Opacity_MutesImpliedAndPartialManual(bool partial, TagSource source, double expected) =>
        Assert.Equal(expected, AssignedTagChrome.Opacity(partial, source));

    [Theory]
    [InlineData("Implied", "", "Implied")]
    [InlineData("Manual", "", "Manual")]
    [InlineData("Implied", "1/2", "Implied (1/2)")]
    [InlineData("Mixed", "1/3", "Mixed (1/3)")]
    [InlineData("", "1/2", "1/2")]
    [InlineData("  Manual  ", "  ", "Manual")]
    [InlineData(null, null, "")]
    public void Tooltip_SourceAndOptionalCount(string? source, string? count, string expected) =>
        Assert.Equal(expected, AssignedTagChrome.Tooltip(source, count));
}
