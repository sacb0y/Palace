namespace Palace.Helpers;

/// <summary>
/// Builds one V2 draw list: visible tiles at the zoom-selected level, resident
/// ancestor underlays for cross-fade, and whole-pyramid fallback when the fine
/// tile is missing. Pure — no GPU.
/// </summary>
public static class SmoothViewportFrame
{
    public readonly record struct DrawTile(
        SmoothTileId Id,
        float ImgOriginX,
        float ImgOriginY,
        float ImgSizeX,
        float ImgSizeY,
        float ParentOriginX,
        float ParentOriginY,
        float LevelScale,
        float Level,
        uint Slice,
        uint ParentSlice,
        float Opacity);

    /// <summary>
    /// Pack one <see cref="DrawTile"/> into 12 floats matching
    /// <c>psv_tile_instance</c> (48 bytes).
    /// </summary>
    public static void PackInstance(in DrawTile t, Span<float> dest)
    {
        if (dest.Length < 12)
        {
            throw new ArgumentException("Need 12 floats for psv_tile_instance.", nameof(dest));
        }

        dest[0] = t.ImgOriginX;
        dest[1] = t.ImgOriginY;
        dest[2] = t.ImgSizeX;
        dest[3] = t.ImgSizeY;
        dest[4] = t.ParentOriginX;
        dest[5] = t.ParentOriginY;
        dest[6] = t.LevelScale;
        dest[7] = t.Level;
        dest[8] = BitConverter.UInt32BitsToSingle(t.Slice);
        dest[9] = BitConverter.UInt32BitsToSingle(t.ParentSlice);
        dest[10] = t.Opacity;
        dest[11] = 0f;
    }

    public static List<DrawTile> Build(
        in SmoothViewport.Affine2x3 imgToDev,
        float panelW,
        float panelH,
        float zoom,
        int fullWidth,
        int fullHeight,
        int maxLevel,
        Func<SmoothTileId, uint?> trySlice,
        SmoothTileCrossfade crossfade,
        float dtMs)
    {
        var level = SmoothViewport.LevelForZoom(zoom, maxLevel);
        var visible = new List<(int Tx, int Ty)>();
        SmoothViewport.CollectVisibleTiles(
            imgToDev, panelW, panelH, level, fullWidth, fullHeight, visible);

        // Dedupe underlay parents so each ancestor is drawn once at opacity 1.
        var underlaySeen = new HashSet<SmoothTileId>();
        var list = new List<DrawTile>(visible.Count * 2);

        foreach (var (tx, ty) in visible)
        {
            var id = new SmoothTileId(level, tx, ty);
            var slice = trySlice(id);

            // Walk coarser ancestors for underlay / fallback (whole pyramid).
            SmoothTileId? ancestorId = null;
            uint? ancestorSlice = null;
            FindNearestResidentAncestor(
                level, tx, ty, maxLevel, trySlice, out ancestorId, out ancestorSlice);

            if (slice is null)
            {
                if (ancestorId is null || ancestorSlice is null)
                {
                    continue;
                }

                // No fine tile — draw the nearest resident ancestor opaque.
                if (underlaySeen.Add(ancestorId.Value))
                {
                    list.Add(MakeDraw(
                        ancestorId.Value,
                        fullWidth,
                        fullHeight,
                        ancestorSlice.Value,
                        parentSlice: 0xFFFFFFFFu,
                        opacity: crossfade.Tick(
                            ancestorId.Value, dtMs, resident: true, seedOpaque: true)));
                }

                continue;
            }

            var hasCoarser = ancestorId is not null && ancestorSlice is not null
                && ancestorId.Value != id;

            // Underlay: resident ancestor at opacity 1 under fading fine tiles.
            if (hasCoarser && underlaySeen.Add(ancestorId!.Value))
            {
                list.Add(MakeDraw(
                    ancestorId.Value,
                    fullWidth,
                    fullHeight,
                    ancestorSlice!.Value,
                    parentSlice: 0xFFFFFFFFu,
                    opacity: 1f));
                // Keep crossfade tracking in sync for the underlay id.
                _ = crossfade.Tick(ancestorId.Value, dtMs, resident: true, seedOpaque: true);
            }

            // N1: ParentSlice only when the resident ancestor is exactly level+1.
            // Deeper ancestors are underlay-only (drawn above at opacity 1).
            uint parentSlice = 0xFFFFFFFFu;
            float parentOx = 0, parentOy = 0;
            if (hasCoarser && ancestorId!.Value.Level == level + 1)
            {
                parentSlice = ancestorSlice!.Value;
                var pScale = 1 << ancestorId.Value.Level;
                var plw = SmoothTileMath.LevelWidth(fullWidth, ancestorId.Value.Level);
                var plh = SmoothTileMath.LevelHeight(fullHeight, ancestorId.Value.Level);
                var (pox, poy, _, _) = SmoothTileMath.ContentRect(
                    ancestorId.Value.TileX, ancestorId.Value.TileY, plw, plh);
                parentOx = pox * pScale;
                parentOy = poy * pScale;
            }

            // Seed opacity 1 when no coarser resident (first paint / no fade).
            var opacity = crossfade.Tick(
                id, dtMs, resident: true, seedOpaque: !hasCoarser);

            var levelScale = 1 << level;
            var lw = SmoothTileMath.LevelWidth(fullWidth, level);
            var lh = SmoothTileMath.LevelHeight(fullHeight, level);
            var (ox, oy, cw, ch) = SmoothTileMath.ContentRect(tx, ty, lw, lh);
            list.Add(new DrawTile(
                id,
                ImgOriginX: ox * levelScale,
                ImgOriginY: oy * levelScale,
                ImgSizeX: cw * levelScale,
                ImgSizeY: ch * levelScale,
                ParentOriginX: parentOx,
                ParentOriginY: parentOy,
                LevelScale: levelScale,
                Level: level,
                Slice: slice.Value,
                ParentSlice: parentSlice,
                Opacity: opacity));
        }

        // Coarse → fine: sort so coarser parents draw first (Geo).
        list.Sort(static (a, b) =>
        {
            var c = b.Level.CompareTo(a.Level);
            return c != 0 ? c : a.Opacity.CompareTo(b.Opacity);
        });
        crossfade.EndFrame();
        return list;
    }

    private static void FindNearestResidentAncestor(
        int level,
        int tileX,
        int tileY,
        int maxLevel,
        Func<SmoothTileId, uint?> trySlice,
        out SmoothTileId? ancestorId,
        out uint? ancestorSlice)
    {
        ancestorId = null;
        ancestorSlice = null;
        var tx = tileX;
        var ty = tileY;
        for (var l = level + 1; l <= maxLevel; l++)
        {
            tx /= 2;
            ty /= 2;
            var id = new SmoothTileId(l, tx, ty);
            var s = trySlice(id);
            if (s is not null)
            {
                ancestorId = id;
                ancestorSlice = s;
                return;
            }
        }
    }

    private static DrawTile MakeDraw(
        SmoothTileId id,
        int fullWidth,
        int fullHeight,
        uint slice,
        uint parentSlice,
        float opacity)
    {
        var levelScale = 1 << id.Level;
        var lw = SmoothTileMath.LevelWidth(fullWidth, id.Level);
        var lh = SmoothTileMath.LevelHeight(fullHeight, id.Level);
        var (ox, oy, cw, ch) = SmoothTileMath.ContentRect(id.TileX, id.TileY, lw, lh);
        return new DrawTile(
            id,
            ImgOriginX: ox * levelScale,
            ImgOriginY: oy * levelScale,
            ImgSizeX: cw * levelScale,
            ImgSizeY: ch * levelScale,
            ParentOriginX: ox * levelScale,
            ParentOriginY: oy * levelScale,
            LevelScale: levelScale,
            Level: id.Level,
            Slice: slice,
            ParentSlice: parentSlice,
            Opacity: opacity);
    }
}
