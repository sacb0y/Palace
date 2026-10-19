using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Empty organize tokens must keep the original file stem, and
/// "Stay in the current source folders" places the template folder on the watched source root.
/// </summary>
public sealed class OrganizeNamingTests
{
    [Fact]
    public void Empty_character_and_tags_keep_the_original_stem()
    {
        var name = OrganizeNames.RenderFile("{Character}-{tags}.{ext}", AssetNamed("shot.webp"), []);
        Assert.Equal("shot.webp", name);
    }

    [Theory]
    [InlineData("frame.png")]
    [InlineData("still.webp")]
    public void Extension_only_names_are_not_emitted(string fileName)
    {
        var name = OrganizeNames.RenderFile("{Character}-{tags}.{ext}", AssetNamed(fileName), []);
        Assert.Equal(fileName, name);
        Assert.False(name.StartsWith('.'));
        Assert.DoesNotContain(" (2)", name);
    }

    [Fact]
    public void Distinct_files_stay_distinct_when_tokens_are_empty()
    {
        Assert.Equal("a.webp", OrganizeNames.RenderFile("{Character}-{tags}.{ext}", AssetNamed("a.webp"), []));
        Assert.Equal("b.png", OrganizeNames.RenderFile("{Character}-{tags}.{ext}", AssetNamed("b.png"), []));
    }

    [Fact]
    public void Partial_tokens_trim_stray_separators()
    {
        var asset = AssetNamed("ignored.webp");
        var alice = Tag("Alice", "Character");
        var portrait = Tag("portrait");

        Assert.Equal("Alice.webp", OrganizeNames.RenderFile("{Character}-{tags}.{ext}", asset, [alice]));
        Assert.Equal("portrait.webp", OrganizeNames.RenderFile("{Character}-{tags}.{ext}", asset, [portrait]));
        Assert.Equal("Alice-portrait.webp", OrganizeNames.RenderFile("{Character}-{tags}.{ext}", asset, [alice, portrait]));
        Assert.Equal("Alice_portrait.webp", OrganizeNames.RenderFile("{Character}__{tags}.{ext}", asset, [alice, portrait]));
        Assert.Equal("Alice portrait.webp", OrganizeNames.RenderFile("{Character}  {tags}.{ext}", asset, [alice, portrait]));
        Assert.Equal("ignored.webp", OrganizeNames.RenderFile("{model}_{seed}.{ext}", asset, []));
    }

    [Fact]
    public void Empty_folder_tokens_do_not_leave_a_separator_folder()
    {
        Assert.Equal("", OrganizeNames.RenderFolder("{Character}/{tags:2}", AssetNamed("shot.webp"), []));
        Assert.Equal("Alice", OrganizeNames.RenderFolder("{Character}-/{tags:2}", AssetNamed("shot.webp"), [Tag("Alice", "Character")]));
    }

    [Fact]
    public void InSource_template_folder_is_under_the_watched_source_root()
    {
        var sourceRoot = Path.Combine("D:", "Library");
        var fileDir = Path.Combine(sourceRoot, "incoming");
        var asset = new Asset
        {
            FileName = "shot.webp",
            Path = Path.Combine(fileDir, "shot.webp")
        };

        var root = OrganizeNames.Root(DestinationPolicy.InSource, destinationRoot: null, sourceFolderPath: sourceRoot);
        var folder = OrganizeNames.RenderFolder("Sorted", asset, []);
        var file = OrganizeNames.RenderFile("{Character}-{tags}.{ext}", asset, []);
        var dest = Path.GetFullPath(Path.Combine(root!, folder, file));

        Assert.Equal(sourceRoot, root);
        Assert.Equal(Path.GetFullPath(Path.Combine(sourceRoot, "Sorted", "shot.webp")), dest);
        Assert.NotEqual(Path.GetFullPath(Path.Combine(fileDir, "Sorted", "shot.webp")), dest);
    }

    [Fact]
    public void Destination_policy_uses_the_picked_root()
    {
        var sourceRoot = Path.Combine("D:", "Library");
        var picked = Path.Combine("D:", "Elsewhere");
        var root = OrganizeNames.Root(DestinationPolicy.Destination, picked, sourceRoot);
        Assert.Equal(picked, root);
    }

    [Fact]
    public void Stem_that_is_only_separators_becomes_untitled()
    {
        var asset = new Asset { FileName = "---.webp", Path = "---.webp" };
        Assert.Equal("untitled.webp", OrganizeNames.RenderFile("{Character}-{tags}.{ext}", asset, []));
    }

    private static Asset AssetNamed(string fileName) => new()
    {
        Id = "a",
        FileName = fileName,
        Path = Path.Combine("library", "incoming", fileName)
    };

    private static AssignedTag Tag(string name, string? parent = null) => new()
    {
        TagId = name,
        TagName = name,
        Slug = name.ToLowerInvariant(),
        ParentNames = parent is null ? [] : [parent]
    };
}
