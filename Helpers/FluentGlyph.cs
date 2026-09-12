using FluentIcons.Common;
using Palace.Models;

namespace Palace.Helpers;

public static class FluentGlyph
{
    public static Icon Parse(string? id)
    {
        var name = RoomIcons.Normalize(id);
        return Enum.TryParse<Icon>(name, true, out var icon) ? icon : Icon.BuildingBank;
    }

    /// <summary>Mosaic / preview fallback when a cloud item has no provider JPEG.</summary>
    public static Icon Placeholder(AssetKind kind) => kind switch
    {
        AssetKind.Video => Icon.Video,
        AssetKind.Gif => Icon.Gif,
        AssetKind.Image => Icon.Image,
        _ => Icon.Document
    };
}
