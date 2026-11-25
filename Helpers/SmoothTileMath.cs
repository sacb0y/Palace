namespace Palace.Helpers;

/// <summary>
/// Smooth Viewer V1 tile grid: 512² slots with a 2-texel gutter (508² content)
/// for Catmull-Rom / mip border reads. Working-space pixels are never
/// orientation-baked — EXIF is a view transform only.
/// </summary>
public static class SmoothTileMath
{
    public const int SlotSize = 512;
    public const int Border = 2;
    public const int ContentSize = SlotSize - 2 * Border; // 508

    public const int CoarseMaxExtent = 2048;

    /// <summary>Number of content tiles along one axis at the given level size.</summary>
    public static int TilesAlong(int levelPixels)
    {
        if (levelPixels <= 0)
        {
            return 0;
        }

        return (levelPixels + ContentSize - 1) / ContentSize;
    }

    public static (int TilesX, int TilesY) TileCounts(int levelWidth, int levelHeight) =>
        (TilesAlong(levelWidth), TilesAlong(levelHeight));

    /// <summary>
    /// Content origin (level image px) of tile (tx, ty). May sit past the image
    /// edge for partial tiles — callers clip with <see cref="ContentRect"/>.
    /// </summary>
    public static (int X, int Y) ContentOrigin(int tileX, int tileY) =>
        (tileX * ContentSize, tileY * ContentSize);

    /// <summary>Clipped content rectangle in level image pixels.</summary>
    public static (int X, int Y, int W, int H) ContentRect(int tileX, int tileY, int levelWidth, int levelHeight)
    {
        var (ox, oy) = ContentOrigin(tileX, tileY);
        var w = Math.Max(0, Math.Min(ContentSize, levelWidth - ox));
        var h = Math.Max(0, Math.Min(ContentSize, levelHeight - oy));
        return (ox, oy, w, h);
    }

    /// <summary>
    /// Slot texel for a content pixel relative to the tile's content origin.
    /// Content (0,0) maps to slot (Border, Border).
    /// </summary>
    public static (int Sx, int Sy) ContentToSlot(int contentLocalX, int contentLocalY) =>
        (contentLocalX + Border, contentLocalY + Border);

    public static int LevelWidth(int fullWidth, int level)
    {
        if (level < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        var w = fullWidth;
        for (var i = 0; i < level; i++)
        {
            w = (w + 1) / 2; // ceil
        }

        return Math.Max(1, w);
    }

    public static int LevelHeight(int fullHeight, int level)
    {
        if (level < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        var h = fullHeight;
        for (var i = 0; i < level; i++)
        {
            h = (h + 1) / 2;
        }

        return Math.Max(1, h);
    }

    /// <summary>Finest level index whose max(extent) is ≤ <see cref="CoarseMaxExtent"/>.</summary>
    public static int CoarseLevelIndex(int fullWidth, int fullHeight)
    {
        var level = 0;
        var w = fullWidth;
        var h = fullHeight;
        while (Math.Max(w, h) > CoarseMaxExtent)
        {
            w = (w + 1) / 2;
            h = (h + 1) / 2;
            level++;
        }

        return level;
    }

    public static int MaxLevel(int fullWidth, int fullHeight)
    {
        var level = 0;
        var w = fullWidth;
        var h = fullHeight;
        while (w > ContentSize || h > ContentSize)
        {
            w = (w + 1) / 2;
            h = (h + 1) / 2;
            level++;
            if (level > 64)
            {
                break;
            }
        }

        return level;
    }

    public static long BytesPerTileRgba16F() => SlotSize * SlotSize * 8L; // 4 × float16
}

/// <summary>Tile identity in the pyramid.</summary>
public readonly record struct SmoothTileId(int Level, int TileX, int TileY)
{
    public override string ToString() => $"L{Level}:{TileX},{TileY}";
}
