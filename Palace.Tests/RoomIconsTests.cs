using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class RoomIconsTests
{
    [Fact]
    public void Normalize_NullOrUnknown_ReturnsDefault()
    {
        Assert.Equal(RoomIcons.Default, RoomIcons.Normalize(null));
        Assert.Equal(RoomIcons.Default, RoomIcons.Normalize(""));
        Assert.Equal(RoomIcons.Default, RoomIcons.Normalize("   "));
        Assert.Equal(RoomIcons.Default, RoomIcons.Normalize("NotAnIcon"));
    }

    [Fact]
    public void Normalize_KnownId_IsUnchanged()
    {
        Assert.Equal("Door", RoomIcons.Normalize("Door"));
        Assert.Equal("BuildingBank", RoomIcons.Normalize("buildingbank"));
        Assert.Equal("MusicNote2", RoomIcons.Normalize(" MusicNote2 "));
    }

    [Fact]
    public void Find_ReturnsSharedChoiceFromAll()
    {
        var choice = RoomIcons.Find("Cube");
        Assert.Same(RoomIcons.All.First(c => c.Id == "Cube"), choice);
        Assert.Equal("Cube", choice.Label);
    }
}
