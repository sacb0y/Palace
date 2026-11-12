using System.Text.RegularExpressions;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Ban writing Composition <c>Visual.Offset</c> (or animating <c>"Offset"</c>) on
/// XAML hand-out visuals under Pages/ and Controls/. That fought layout arrange:
/// height-only left Preview over the folder tree (Studio 0.3.60–0.3.66).
/// Owned SpriteVisual children (e.g. ShellBackdrop blur pad) are allowed when the
/// assignment is not on <c>GetElementVisual(...)</c>.
/// </summary>
public sealed class CompositionOffsetBanTests
{
    private static readonly Regex OffsetAssign = new(
        @"GetElementVisual\s*\([^;]{0,200}?\)[\s\S]{0,400}?\.Offset\s*=",
        RegexOptions.Compiled);

    private static readonly Regex OffsetAnim = new(
        @"StartAnimation\s*\(\s*[""']Offset(\.[XYZ])?[""']",
        RegexOptions.Compiled);

    private static readonly Regex StopOffsetAnim = new(
        @"StopAnimation\s*\(\s*[""']Offset(\.[XYZ])?[""']",
        RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Palace.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Palace.csproj not found above the test output");
    }

    private static IEnumerable<string> ScanRoots(string root) =>
        new[]
        {
            Path.Combine(root, "Pages"),
            Path.Combine(root, "Controls"),
        }.Where(Directory.Exists);

    private static IEnumerable<string> CsFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"));

    [Fact]
    public void Pages_and_Controls_do_not_write_or_animate_handout_Visual_Offset()
    {
        var root = RepoRoot();
        var hits = new List<string>();
        foreach (var folder in ScanRoots(root))
        {
            foreach (var file in CsFiles(folder))
            {
                var text = File.ReadAllText(file);
                var rel = Path.GetRelativePath(root, file);
                if (OffsetAssign.IsMatch(text))
                {
                    hits.Add($"{rel}: GetElementVisual(...).Offset =");
                }

                if (OffsetAnim.IsMatch(text))
                {
                    hits.Add($"{rel}: StartAnimation(\"Offset\")");
                }

                if (StopOffsetAnim.IsMatch(text))
                {
                    hits.Add($"{rel}: StopAnimation(\"Offset\")");
                }
            }
        }

        Assert.True(
            hits.Count == 0,
            "Hand-out Visual.Offset write/anim banned (use Translation). Hits:\n"
                + string.Join("\n", hits));
    }

    [Fact]
    public void App_wide_Offset_hits_are_documented_owned_SpriteVisuals_only()
    {
        // Helpers/ShellBackdrop.cs assigns Offset on CreateSpriteVisual children
        // (blur pad), not GetElementVisual hand-outs — allowed. Fail if a new
        // GetElementVisual(...).Offset = appears outside Pages/Controls scan.
        var root = RepoRoot();
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part is "bin" or "obj" or "Palace.Tests")))
        {
            var text = File.ReadAllText(file);
            var rel = Path.GetRelativePath(root, file);
            if (rel.StartsWith("Pages" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith("Controls" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue; // covered by the ban test above
            }

            if (OffsetAssign.IsMatch(text) || OffsetAnim.IsMatch(text))
            {
                hits.Add(rel);
            }
        }

        Assert.True(
            hits.Count == 0,
            "Unexpected Visual.Offset write/anim outside Pages/Controls (fix or document):\n"
                + string.Join("\n", hits));
    }
}
