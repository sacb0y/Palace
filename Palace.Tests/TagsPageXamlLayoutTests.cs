using System.Text.RegularExpressions;
using Xunit;

namespace Palace.Tests;

/// <summary>Source-level TagsPage layout contracts (no WinUI).</summary>
public sealed class TagsPageXamlLayoutTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Palace.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Palace.csproj not found above the test output");
    }

    private static string TagsPageXaml() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "Pages", "TagsPage.xaml"));

    [Fact]
    public void GrdTagColumns_NotWrappedByPageScrollViewer()
    {
        var xaml = TagsPageXaml();
        var grd = xaml.IndexOf("x:Name=\"GrdTagColumns\"", StringComparison.Ordinal);
        Assert.True(grd >= 0, "GrdTagColumns missing");

        var before = xaml[..grd];
        var opens = Regex.Matches(before, @"<ScrollViewer\b").Count;
        var closes = Regex.Matches(before, @"</ScrollViewer>").Count;
        Assert.Equal(
            opens,
            closes);
    }

    [Fact]
    public void WideColumns_AreStaticXaml_NotCodeAllocateDefaults()
    {
        var xaml = TagsPageXaml();
        Assert.Contains("x:Name=\"ColTagSidebar\" Width=\"200\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ColTagBoard\" Width=\"*\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"ColTagDetails\" Width=\"260\" MinWidth=\"240\" MaxWidth=\"300\"",
            xaml,
            StringComparison.Ordinal);
    }
}
