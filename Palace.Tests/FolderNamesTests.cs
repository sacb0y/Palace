using Palace.Helpers;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

public sealed class FolderNamesTests
{
    [Theory]
    [InlineData("Refs", "Refs")]
    [InlineData("  Refs  ", "Refs")]
    [InlineData("v1.2 final", "v1.2 final")]
    [InlineData("CONSOLE", "CONSOLE")]
    public void TryValidate_accepts_and_trims_good_names(string input, string expected)
    {
        Assert.True(FolderNames.TryValidate(input, out var cleaned, out var error), error);
        Assert.Equal(expected, cleaned);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("what?")]
    [InlineData("star*")]
    [InlineData("\"quoted\"")]
    [InlineData("pipe|")]
    [InlineData("colon:")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("trailing.")]
    [InlineData("con")]
    [InlineData("NUL.txt")]
    [InlineData("COM1")]
    [InlineData("lpt9")]
    public void TryValidate_rejects_names_windows_refuses(string input)
    {
        Assert.False(FolderNames.TryValidate(input, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryValidate_rejects_control_characters_and_overlong_names()
    {
        Assert.False(FolderNames.TryValidate("tab\there", out _, out _));
        Assert.False(FolderNames.TryValidate(new string('a', FolderNames.MaxLength + 1), out _, out _));
        Assert.True(FolderNames.TryValidate(new string('a', FolderNames.MaxLength), out _, out _));
    }

    [Fact]
    public void UniqueChild_counts_up_from_two()
    {
        var parent = Path.Combine(Path.GetTempPath(), "p");
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(parent, "New folder"),
            Path.Combine(parent, "New folder (2)")
        };

        Assert.Equal("New folder (3)", FolderNames.UniqueChild(parent, FolderNames.DefaultNewFolder, taken.Contains));
        Assert.Equal("Other", FolderNames.UniqueChild(parent, "Other", taken.Contains));
    }

    [Fact]
    public void UniqueChild_works_with_ChildExists_when_exists_receives_full_paths()
    {
        var parent = Path.Combine(Path.GetTempPath(), "palace-unique-child-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(parent, "New folder"));
        Directory.CreateDirectory(Path.Combine(parent, "New folder (2)"));
        try
        {
            // Same wiring as LibraryViewModel.NewFolder: UniqueChild passes full paths;
            // ChildExists wants the bare name.
            var name = FolderNames.UniqueChild(
                parent,
                FolderNames.DefaultNewFolder,
                fullPath => FileOpService.ChildExists(parent, Path.GetFileName(fullPath)));
            Assert.Equal("New folder (3)", name);
        }
        finally
        {
            try { Directory.Delete(parent, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void UniqueFilePath_keeps_the_extension()
    {
        var dir = Path.Combine(Path.GetTempPath(), "d");
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(dir, "a.png") };

        Assert.Equal(Path.Combine(dir, "a (2).png"), FolderNames.UniqueFilePath(dir, "a.png", taken.Contains));
        Assert.Equal(Path.Combine(dir, "b.png"), FolderNames.UniqueFilePath(dir, "b.png", taken.Contains));
        Assert.Equal(Path.Combine(dir, "A (2).PNG"), FolderNames.UniqueFilePath(dir, "A.PNG", taken.Contains));
    }

    [Fact]
    public void IsSameOrUnder_needs_a_separator_boundary()
    {
        var root = Path.Combine(Path.GetTempPath(), "Root");
        Assert.True(FolderNames.IsSameOrUnder(root, root));
        Assert.True(FolderNames.IsSameOrUnder(root + Path.DirectorySeparatorChar, root));
        Assert.True(FolderNames.IsSameOrUnder(Path.Combine(root, "a", "b"), root));
        Assert.False(FolderNames.IsSameOrUnder(root + "2", root));
        Assert.False(FolderNames.IsSameOrUnder(Path.GetTempPath(), root));
    }
}
