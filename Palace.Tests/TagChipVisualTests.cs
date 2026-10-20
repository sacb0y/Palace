using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagChipVisualTests
{
    [Fact]
    public void NeverShowsFaceOnWord()
    {
        Assert.False(TagChipVisual.ShowsFaceOnWord);
        Assert.Equal("", TagChipVisual.FaceOnLabel);
        Assert.DoesNotContain("On", TagChipVisual.FaceOnLabel, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, TagChipApplyState.Off)]
    [InlineData(false, true, TagChipApplyState.On)]
    [InlineData(true, false, TagChipApplyState.Partial)]
    [InlineData(true, true, TagChipApplyState.Partial)]
    public void Resolve_PartialBeatsOn(bool partial, bool on, TagChipApplyState expected) =>
        Assert.Equal(expected, TagChipVisual.Resolve(partial, on));

    [Fact]
    public void FromSelected_IsOnOrOffOnly()
    {
        Assert.Equal(TagChipApplyState.On, TagChipVisual.FromSelected(true));
        Assert.Equal(TagChipApplyState.Off, TagChipVisual.FromSelected(false));
    }

    [Fact]
    public void PartialTooltip_MatchesGooeyCopy() =>
        Assert.Equal(
            "On 2 of 5 selected. Click to add to all.",
            TagChipVisual.PartialTooltip(2, 5));

    [Fact]
    public void PartialBadge_IsSlashForm() =>
        Assert.Equal("2/5", TagChipVisual.PartialBadge(2, 5));

    [Fact]
    public void Toggle_PartialAddsThenOnRemoves()
    {
        Assert.True(TagChipVisual.ToggleAdds(TagChipApplyState.Off));
        Assert.True(TagChipVisual.ToggleAdds(TagChipApplyState.Partial));
        Assert.True(TagChipVisual.ToggleRemoves(TagChipApplyState.On));
        Assert.False(TagChipVisual.ToggleRemoves(TagChipApplyState.Partial));
        Assert.Equal(TagChipApplyState.On, TagChipVisual.NextAfterToggle(TagChipApplyState.Partial));
        Assert.Equal(TagChipApplyState.Off, TagChipVisual.NextAfterToggle(TagChipApplyState.On));
        Assert.Equal(TagChipApplyState.On, TagChipVisual.NextAfterToggle(TagChipApplyState.Off));
    }

    [Fact]
    public void AssignTooltip_PartialAndOnOff()
    {
        Assert.Equal(
            "On 2 of 5 selected. Click to add to all.",
            TagChipVisual.AssignTooltip(TagChipApplyState.Partial, "Cream", 2, 5));
        Assert.Equal("Remove Cream", TagChipVisual.AssignTooltip(TagChipApplyState.On, "Cream", 0, 1));
        Assert.Equal("Add Cream", TagChipVisual.AssignTooltip(TagChipApplyState.Off, "Cream", 0, 1));
    }

    [Fact]
    public void StarTooltip_AndAutomation()
    {
        Assert.Equal("Star tag", TagChipVisual.StarTooltip(false));
        Assert.Equal("Unstar tag", TagChipVisual.StarTooltip(true));
        Assert.Equal("Cream, On, Starred", TagChipVisual.AutomationName("Cream", TagChipApplyState.On, true));
        Assert.Equal("Cream, Mixed", TagChipVisual.AutomationName("Cream", TagChipApplyState.Partial, false));
        Assert.Equal("Cream, Off", TagChipVisual.AutomationName("Cream", TagChipApplyState.Off, false));
    }

    [Fact]
    public void Announce_Counts()
    {
        Assert.Equal("Added Cream", TagChipVisual.AnnounceAdded("Cream", 1));
        Assert.Equal("Added Cream to all 5 selected", TagChipVisual.AnnounceAdded("Cream", 5));
        Assert.Equal("Removed Cream", TagChipVisual.AnnounceRemoved("Cream", 1));
        Assert.Equal("Removed Cream from 5 selected", TagChipVisual.AnnounceRemoved("Cream", 5));
    }

    [Fact]
    public void ChromeSizes_MatchGooey()
    {
        Assert.Equal(32, TagChipVisual.HeightDip);
        Assert.Equal(28, TagChipVisual.CompactHeightDip);
        Assert.Equal(16, TagChipVisual.CornerRadiusDip);
        Assert.Equal(14, TagChipVisual.LeadingGlyphSlotDip);
        Assert.Equal(8, TagChipVisual.ColorDotDip);
        Assert.Equal(24, TagChipVisual.StarButtonDip);
        Assert.Equal(1.5, TagChipVisual.PartialBorderThickness);
        Assert.Equal("#F2B600", TagChipVisual.StarGoldDarkHex);
        Assert.Equal("#B8860B", TagChipVisual.StarGoldLightHex);
        Assert.Equal("#C2185B", TagChipVisual.LightAccentFillFloorHex);
        Assert.Equal("✓ all selected · – some", TagChipVisual.MultiSelectLegend);
    }

    [Fact]
    public void LightAccentFloor_MeetsTextOnAccentAa()
    {
        var floor = BrandAccent.ParseHex(TagChipVisual.LightAccentFillFloorHex);
        Assert.NotNull(floor);
        Assert.True(BrandAccent.MeetsAaText(new BrandAccent.Rgb(255, 255, 255), floor!.Value));
        var light = BrandAccent.ForLightTheme(BrandAccent.BuildRamp());
        Assert.True(BrandAccent.MeetsAaText(light.TextOnAccent, light.Fill));
    }
}
