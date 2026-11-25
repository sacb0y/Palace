namespace Palace.Helpers;

/// <summary>
/// Builds one V2 draw list: visible tiles at the zoom-selected level, parent
/// slice for trilinear blend, and cross-fade opacity. Pure — no GPU.
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
        // slice / parent_slice as uint bit patterns in float slots — callers
        // that marshal to native should use SmoothTileInstanceBytes instead.
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

        var levelScale = 1 << level;
        var lw = SmoothTileMath.LevelWidth(fullWidth, level);
        var lh = SmoothTileMath.LevelHeight(fullHeight, level);
        var parentLevel = Math.Min(maxLevel, level + 1);
        var parentScale = 1 << parentLevel;
        var plw = SmoothTileMath.LevelWidth(fullWidth, parentLevel);
        var plh = SmoothTileMath.LevelHeight(fullHeight, parentLevel);

        var list = new List<DrawTile>(visible.Count);
        foreach (var (tx, ty) in visible)
        {
            var id = new SmoothTileId(level, tx, ty);
            var slice = trySlice(id);
            if (slice is null)
            {
                // Fall back to coarser parent so the viewport never blanks.
                var ptx = tx / 2;
                var pty = ty / 2;
                var parentId = new SmoothTileId(parentLevel, ptx, pty);
                var parentSliceOnly = trySlice(parentId);
                if (parentSliceOnly is null)
                {
                    continue;
                }

                var (pox, poy, pw, ph) = SmoothTileMath.ContentRect(ptx, pty, plw, plh);
                var opacityFallback = crossfade.Tick(parentId, dtMs, resident: true);
                list.Add(new DrawTile(
                    parentId,
                    ImgOriginX: pox * parentScale,
                    ImgOriginY: poy * parentScale,
                    ImgSizeX: pw * parentScale,
                    ImgSizeY: ph * parentScale,
                    ParentOriginX: pox * parentScale,
                    ParentOriginY: poy * parentScale,
                    LevelScale: parentScale,
                    Level: parentLevel,
                    Slice: parentSliceOnly.Value,
                    ParentSlice: 0xFFFFFFFFu,
                    Opacity: opacityFallback));
                continue;
            }

            var (ox, oy, cw, ch) = SmoothTileMath.ContentRect(tx, ty, lw, lh);
            uint parentSlice = 0xFFFFFFFFu;
            float parentOx = 0, parentOy = 0;
            if (level < maxLevel)
            {
                var ptx = tx / 2;
                var pty = ty / 2;
                var parentId = new SmoothTileId(parentLevel, ptx, pty);
                var ps = trySlice(parentId);
                if (ps is not null)
                {
                    parentSlice = ps.Value;
                    var (pox, poy, _, _) = SmoothTileMath.ContentRect(ptx, pty, plw, plh);
                    parentOx = pox * parentScale;
                    parentOy = poy * parentScale;
                }
            }

            var opacity = crossfade.Tick(id, dtMs, resident: true);
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
        list.Sort(static (a, b) => b.Level.CompareTo(a.Level));
        crossfade.EndFrame();
        return list;
    }
}
