using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagChipChromeTests
{
    [Fact]
    public void BoardChip_UsesLeadingDot_NotTagColorFill()
    {
        Assert.True(TagChipChrome.ShowsLeadingDot);
        Assert.False(TagChipChrome.UsesFillBackground);
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
            TagChipChrome.HorizontalPaddingDip
                + TagChipChrome.LeadingStateSlotDip
                + (2 * TagChipChrome.CharacterDip),
            shortChip);
    }

    [Fact]
    public void WrapWidth_AddsStarAndColorDot_NotOnWord()
    {
        var name = "sonic";
        var baseWidth = TagChipChrome.WrapWidth(name);
        Assert.Equal(baseWidth + TagChipChrome.StarGlyphDip, TagChipChrome.WrapWidth(name, starred: true));
        Assert.Equal(baseWidth + TagChipChrome.ColorDotSlotDip, TagChipChrome.WrapWidth(name, hasColor: true));
        // filterSelected no longer adds a face "On" label — state glyph is already in the base width.
        Assert.Equal(baseWidth, TagChipChrome.WrapWidth(name, filterSelected: true));
        Assert.Equal(TagChipChrome.LeadingStateSlotDip, TagChipChrome.FilterOnLabelDip);
    }

    [Fact]
    public void WrapWidth_TreatsNullNameAsEmpty() =>
        Assert.Equal(
            TagChipChrome.HorizontalPaddingDip + TagChipChrome.LeadingStateSlotDip,
            TagChipChrome.WrapWidth(null));
}
