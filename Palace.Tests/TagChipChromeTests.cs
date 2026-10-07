using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagChipChromeTests
{
    [Fact]
    public void BoardChip_FillsBackground_NotALeadingDot()
    {
        Assert.False(TagChipChrome.ShowsLeadingDot);
        Assert.True(TagChipChrome.UsesFillBackground);
    }

    [Fact]
    public void BoardChip_DoesNotCapNameWidth() =>
        Assert.False(TagChipChrome.CapsNameWidth);

    [Fact]
    public void WrapWidth_FollowsTagTextLength()
    {
        var shortChip = TagChipChrome.WrapWidth("AI");
        var longChip = TagChipChrome.WrapWidth("character-design-reference");
        Assert.True(longChip > shortChip);
        Assert.Equal(
            TagChipChrome.HorizontalPaddingDip + (2 * TagChipChrome.CharacterDip),
            shortChip);
        Assert.Equal(
            TagChipChrome.HorizontalPaddingDip + ("character-design-reference".Length * TagChipChrome.CharacterDip),
            longChip);
    }

    [Fact]
    public void WrapWidth_AddsStarAndFilterChrome()
    {
        var name = "sonic";
        var baseWidth = TagChipChrome.WrapWidth(name);
        Assert.Equal(baseWidth + TagChipChrome.StarGlyphDip, TagChipChrome.WrapWidth(name, starred: true));
        Assert.Equal(baseWidth + TagChipChrome.FilterOnLabelDip, TagChipChrome.WrapWidth(name, filterSelected: true));
        Assert.Equal(
            baseWidth + TagChipChrome.StarGlyphDip + TagChipChrome.FilterOnLabelDip,
            TagChipChrome.WrapWidth(name, starred: true, filterSelected: true));
    }

    [Fact]
    public void WrapWidth_TreatsNullNameAsEmpty() =>
        Assert.Equal(TagChipChrome.HorizontalPaddingDip, TagChipChrome.WrapWidth(null));
}
