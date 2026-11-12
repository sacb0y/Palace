using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

public sealed class TagColorTests
{
    [Theory]
    [InlineData("#EAB308")] // HDR amber — was white ink (1.9:1), now black
    [InlineData("#F97316")] // Sunset
    [InlineData("#3B82F6")] // Sky
    [InlineData("#E05A8C")] // Mira / brand pink
    [InlineData("#22C55E")]
    [InlineData("#EF4444")]
    public void ContrastingInk_PicksBlackOnMidLuminanceFills(string hex)
    {
        Assert.True(TagInk.TryParse(hex, out var fill));
        var ink = TagInk.Contrasting(fill);
        Assert.Equal(0, ink.R);
        Assert.Equal(0, ink.G);
        Assert.Equal(0, ink.B);
        Assert.True(ink.A >= 0xE6);
    }

    [Theory]
    [InlineData("#6D28D9")] // Space violet — stays white
    [InlineData("#64748B")] // slate preset
    [InlineData("#000000")]
    public void ContrastingInk_PicksWhiteOnDarkFills(string hex)
    {
        Assert.True(TagInk.TryParse(hex, out var fill));
        var ink = TagInk.Contrasting(fill);
        Assert.Equal(255, ink.R);
        Assert.Equal(255, ink.G);
        Assert.Equal(255, ink.B);
    }

    [Fact]
    public void Presets_AllReachAaWithChosenInk()
    {
        foreach (var hex in TagInk.Presets)
        {
            Assert.True(TagInk.TryParse(hex, out var fill));
            var ink = TagInk.Contrasting(fill);
            var ratio = TagInk.ContrastRatio(fill, ink);
            Assert.True(ratio >= 4.5, $"{hex} with ink {TagInk.ToHex(ink)} is {ratio:F2}:1");
        }
    }

    [Fact]
    public void EqualContrastCrossover_PicksEitherInkNearPointOneSevenNine()
    {
        // White and black have equal contrast at L ≈ 0.179.
        var gray = new TagInk.Rgba(255, 118, 118, 118);
        var ink = TagInk.Contrasting(gray);
        Assert.True(ink.R == 0 || ink.R == 255);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FaceColor_NullOrEmpty_ReturnsOpaquePaletteColour(string? stored)
    {
        var face = TagInk.FaceColor(stored, "group-subject");
        Assert.True(TagInk.TryParse(face, out var rgba));
        Assert.Equal(255, rgba.A);
        Assert.Contains(TagInk.Normalize(face), TagInk.GroupFacePalette.Select(TagInk.Normalize));
        Assert.False(TagInk.IsTooDarkForFace(face));
    }

    [Theory]
    [InlineData("#000000")]
    [InlineData("#010101")]
    [InlineData("#FF0A0A0A")]
    [InlineData("#1C1C1C")]
    public void FaceColor_TooDark_FallsBackToPalette(string stored)
    {
        var face = TagInk.FaceColor(stored, "group-subject");
        Assert.NotEqual(TagInk.Normalize(stored), TagInk.Normalize(face));
        Assert.Contains(TagInk.Normalize(face), TagInk.GroupFacePalette.Select(TagInk.Normalize));
        Assert.False(TagInk.IsTooDarkForFace(face));
    }

    [Fact]
    public void FaceColor_ReadableStored_IsKept()
    {
        var face = TagInk.FaceColor("#14B8A6", "unused-key");
        Assert.Equal("#FF14B8A6", face);
    }

    [Fact]
    public void FaceColor_SameKey_IsDeterministic()
    {
        var a = TagInk.FaceColor(null, "Subject");
        var b = TagInk.FaceColor(null, "Subject");
        Assert.Equal(a, b);
    }

    [Fact]
    public void MapEffectiveColors_UngroupedLeafNull_StaysNull()
    {
        var loose = new Tag { Id = "t-loose", Name = "Loose", Priority = 0 };
        var colors = CatalogService.MapEffectiveColors([loose], []);
        Assert.Null(colors[loose.Id]);
        Assert.Equal("No color", CatalogService.DescribeTagColor(loose, [loose], []));
    }

    [Fact]
    public void MapEffectiveColors_NullGroupColour_ChipsShareResolvedFace()
    {
        var subject = new Tag { Id = "g-subject", Name = "Subject", Priority = 10 };
        var smile = new Tag { Id = "t-smile", Name = "smile", Priority = 1 };
        var looking = new Tag { Id = "t-looking", Name = "looking at viewer", Priority = 1 };
        var tags = new List<Tag> { subject, smile, looking };
        var edges = new List<TagMembership>
        {
            new() { ParentId = subject.Id, ChildId = smile.Id },
            new() { ParentId = subject.Id, ChildId = looking.Id }
        };

        var colors = CatalogService.MapEffectiveColors(tags, edges);
        var groupFace = colors[subject.Id];
        Assert.False(string.IsNullOrWhiteSpace(groupFace));
        Assert.True(TagInk.TryParse(groupFace, out var rgba));
        Assert.Equal(255, rgba.A);
        Assert.Contains(TagInk.Normalize(groupFace), TagInk.GroupFacePalette.Select(TagInk.Normalize));
        Assert.Equal(groupFace, colors[smile.Id]);
        Assert.Equal(groupFace, colors[looking.Id]);
        Assert.Equal("No color", CatalogService.DescribeTagColor(subject, tags, edges));
    }

    [Fact]
    public void MapEffectiveColors_NearBlackGroupColour_ChipsShareResolvedFace()
    {
        var subject = new Tag { Id = "g-dark", Name = "Subject", Priority = 10, Color = "#010101" };
        var smile = new Tag { Id = "t-smile-dark", Name = "smile", Priority = 1 };
        var tags = new List<Tag> { subject, smile };
        var edges = new List<TagMembership>
        {
            new() { ParentId = subject.Id, ChildId = smile.Id }
        };

        var colors = CatalogService.MapEffectiveColors(tags, edges);
        var groupFace = colors[subject.Id];
        Assert.NotEqual("#FF010101", TagInk.Normalize(groupFace));
        Assert.Equal(groupFace, colors[smile.Id]);
        Assert.False(TagInk.IsTooDarkForFace(groupFace));
    }

    [Fact]
    public void MapEffectiveColors_GroupFace_DoesNotColourSiblingUngroupedLeaf()
    {
        var subject = new Tag { Id = "g-subject2", Name = "Subject", Priority = 10 };
        var smile = new Tag { Id = "t-smile2", Name = "smile", Priority = 1 };
        var loose = new Tag { Id = "t-loose2", Name = "Loose", Priority = 0 };
        var tags = new List<Tag> { subject, smile, loose };
        var edges = new List<TagMembership>
        {
            new() { ParentId = subject.Id, ChildId = smile.Id }
        };

        var colors = CatalogService.MapEffectiveColors(tags, edges);
        Assert.False(string.IsNullOrWhiteSpace(colors[subject.Id]));
        Assert.Equal(colors[subject.Id], colors[smile.Id]);
        Assert.Null(colors[loose.Id]);
    }
}
