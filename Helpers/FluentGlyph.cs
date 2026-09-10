using FluentIcons.Common;

namespace Palace.Helpers;

public static class FluentGlyph
{
    public static Icon Parse(string? id)
    {
        var name = RoomIcons.Normalize(id);
        return Enum.TryParse<Icon>(name, true, out var icon) ? icon : Icon.BuildingBank;
    }
}
