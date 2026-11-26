namespace Palace.Helpers;

/// <summary>
/// Smooth Viewer V2 viewport math (Geo image-pass contract). Zoom is device
/// pixels per level-0 image pixel; the swap chain stays panel-sized so deep
/// zoom never hits the legacy 8192 DIP / DXGI panel cap.
/// </summary>
public static class SmoothViewport
{
    public const float MaxZoom = 32f;
    public const float NearestStart = 4f;
    public const float NearestFull = 6f;
    public const float GridZoomStart = 8f;
    public const float GridZoomFull = 10f;
    public const float AaNearestDefault = 1f;
    public const float CrossfadeMsMin = 80f;
    public const float CrossfadeMsMax = 120f;
    public const float CrossfadeMsDefault = 100f;
    /// <summary>Geo wheel: each notch multiplies zoom by 2^0.25 (Ctrl = half notch).</summary>
    public const float WheelNotchLog2 = 0.25f;
    /// <summary>Max frame dt for ease / snap (seconds) — clamps hitch overshoot.</summary>
    public const float MaxFrameDtSeconds = 0.05f;

    /// <summary>
    /// GPU viewport path sizes the swap chain to the panel and applies zoom as
    /// a per-frame affine — never inflate a XAML/DXGI panel past 8192.
    /// </summary>
    public static bool RemovesDipPanelCap => true;

    public static bool AppliesLegacyDipCap(bool smoothPresenterEnabled) =>
        !smoothPresenterEnabled;

    public enum FilterMode
    {
        BicubicParentBlend = 0,
        Bicubic = 1,
        BicubicToNearest = 2,
        Nearest = 3
    }

    /// <summary>Row-major 2×3 affine: <c>dev = M * img + T</c>.</summary>
    public readonly record struct Affine2x3(float A, float B, float Tx, float C, float D, float Ty)
    {
        public (float X, float Y) Transform(float x, float y) =>
            ((A * x) + (B * y) + Tx, (C * x) + (D * y) + Ty);

        public Affine2x3 Invert()
        {
            var det = (A * D) - (B * C);
            if (MathF.Abs(det) < 1e-12f)
            {
                return Identity;
            }

            var invDet = 1f / det;
            var ia = D * invDet;
            var ib = -B * invDet;
            var ic = -C * invDet;
            var id = A * invDet;
            var itx = ((-ia * Tx) - (ib * Ty));
            var ity = ((-ic * Tx) - (id * Ty));
            return new Affine2x3(ia, ib, itx, ic, id, ity);
        }

        public static Affine2x3 Identity => new(1, 0, 0, 0, 1, 0);

        /// <summary>Orientation (±1/0) then uniform zoom + translate.</summary>
        public static Affine2x3 FromZoomPan(
            float zoom,
            float tx,
            float ty,
            float o00 = 1f,
            float o01 = 0f,
            float o10 = 0f,
            float o11 = 1f) =>
            new(zoom * o00, zoom * o01, tx, zoom * o10, zoom * o11, ty);

        /// <summary>
        /// Compose zoom/pan in oriented space with an EXIF storage→upright matrix
        /// (linear + translation). <paramref name="tx"/>/<paramref name="ty"/> are
        /// device-space pan after orientation.
        /// </summary>
        public static Affine2x3 FromZoomPan(
            float zoom,
            float tx,
            float ty,
            in ExifOrient orient) =>
            // img_dev = zoom * R * img_storage + (zoom * T_orient + pan)
            new(
                zoom * orient.A,
                zoom * orient.B,
                (zoom * orient.Tx) + tx,
                zoom * orient.C,
                zoom * orient.D,
                (zoom * orient.Ty) + ty);
    }

    /// <summary>
    /// EXIF Orientation 1–8 as a storage→upright 2×3 (never baked into tiles).
    /// </summary>
    public readonly record struct ExifOrient(
        float A,
        float B,
        float Tx,
        float C,
        float D,
        float Ty,
        int OrientedWidth,
        int OrientedHeight)
    {
        public static ExifOrient Identity(int width, int height) =>
            new(1, 0, 0, 0, 1, 0, width, height);

        /// <summary>Map EXIF tag 1–8 to matrix + translation for <paramref name="storedW"/>×<paramref name="storedH"/>.</summary>
        public static ExifOrient FromExif(int orientation, int storedW, int storedH)
        {
            if (storedW < 1 || storedH < 1)
            {
                return Identity(Math.Max(1, storedW), Math.Max(1, storedH));
            }

            var (ow, oh) = HdrPixels.OrientedSize(storedW, storedH, (uint)Math.Clamp(orientation, 1, 8));
            // Storage (sx,sy) → upright (ux,uy) in continuous image coords: the quad spans
            // [0,W]×[0,H], so flips translate by W/H (not W-1/H-1, which is the pixel-index form).
            return orientation switch
            {
                2 => new(-1, 0, storedW, 0, 1, 0, ow, oh),           // flip X
                3 => new(-1, 0, storedW, 0, -1, storedH, ow, oh), // 180
                4 => new(1, 0, 0, 0, -1, storedH, ow, oh),            // flip Y
                5 => new(0, 1, 0, 1, 0, 0, ow, oh),                       // transpose
                6 => new(0, -1, storedH, 1, 0, 0, ow, oh),            // 90 CW
                7 => new(0, -1, storedH, -1, 0, storedW, ow, oh), // transverse
                8 => new(0, 1, 0, -1, 0, storedW, ow, oh),            // 90 CCW
                _ => Identity(storedW, storedH),
            };
        }

        /// <summary>Cycle EXIF 1→6→3→8→1 (90° CW) or reverse for CCW.</summary>
        public static int Rotate90(int orientation, bool clockwise)
        {
            orientation = Math.Clamp(orientation, 1, 8);
            // Only the pure-rotation subgroup for R / Shift+R.
            ReadOnlySpan<int> cw = [1, 6, 3, 8];
            var i = cw.IndexOf(orientation);
            if (i < 0)
            {
                // Mirrored tags: snap to nearest rotation then step.
                orientation = orientation switch
                {
                    2 or 5 or 7 => 6,
                    4 => 8,
                    _ => 1,
                };
                i = cw.IndexOf(orientation);
            }

            if (clockwise)
            {
                return cw[(i + 1) % cw.Length];
            }

            return cw[(i + cw.Length - 1) % cw.Length];
        }
    }

    public static float ClampZoom(float zoom, float fitZoom) =>
        Math.Clamp(zoom, Math.Min(fitZoom, 1f), MaxZoom);

    /// <summary>Clamp real frame dt so a hitch cannot overshoot ease/snap.</summary>
    public static float ClampFrameDt(float dtSeconds) =>
        Math.Clamp(dtSeconds, 0f, MaxFrameDtSeconds);

    /// <summary>
    /// Wheel notch factor: <c>2^(notches * 0.25)</c>. Negative notches zoom out.
    /// Ctrl uses half-notch steps (<paramref name="fine"/>).
    /// </summary>
    public static float WheelZoomFactor(int notches, bool fine = false)
    {
        if (notches == 0)
        {
            return 1f;
        }

        var step = fine ? WheelNotchLog2 * 0.5f : WheelNotchLog2;
        return MathF.Pow(2f, notches * step);
    }

    /// <summary>
    /// Round pan T to whole device px. Call only when input is at rest
    /// (ease settled, no active pan/pinch). Safe at any zoom including z≥4 at rest.
    /// </summary>
    public static (float Tx, float Ty) SnapPanTranslation(float tx, float ty) =>
        (MathF.Round(tx), MathF.Round(ty));

    public static bool IsEaseSettled(float zoom, float targetZoom, float epsilon = 1e-4f) =>
        MathF.Abs(zoom - targetZoom) < epsilon;

    public static float FitZoom(float panelW, float panelH, float imgW, float imgH)
    {
        if (panelW <= 0 || panelH <= 0 || imgW <= 0 || imgH <= 0)
        {
            return 1f;
        }

        return MathF.Min(panelW / imgW, panelH / imgH);
    }

    /// <summary>Zoom about cursor <paramref name="cx"/>,<paramref name="cy"/> (device px).</summary>
    public static (float Zoom, float Tx, float Ty) ZoomAbout(
        float zoom,
        float tx,
        float ty,
        float newZoom,
        float cx,
        float cy)
    {
        if (zoom <= 1e-12f)
        {
            return (newZoom, tx, ty);
        }

        var scale = newZoom / zoom;
        return (newZoom, cx - ((cx - tx) * scale), cy - ((cy - ty) * scale));
    }

    /// <summary>
    /// Pinch / touchpad: apply scale 1:1 about centroid (no ease). Same clamp as wheel.
    /// </summary>
    public static (float Zoom, float Tx, float Ty) PinchZoomAbout(
        float zoom,
        float tx,
        float ty,
        float scaleDelta,
        float centroidX,
        float centroidY,
        float fitZoom)
    {
        if (scaleDelta <= 1e-12f)
        {
            return (ClampZoom(zoom, fitZoom), tx, ty);
        }

        var next = ClampZoom(zoom * scaleDelta, fitZoom);
        return ZoomAbout(zoom, tx, ty, next, centroidX, centroidY);
    }

    /// <summary>
    /// Wheel ease toward target: <c>z += (zt - z) * (1 - exp(-dt/tau))</c>, tau≈70 ms.
    /// </summary>
    public static float EaseZoom(float current, float target, float dtSeconds, float tau = 0.07f)
    {
        if (tau <= 1e-6f || dtSeconds <= 0f)
        {
            return target;
        }

        var dt = ClampFrameDt(dtSeconds);
        var a = 1f - MathF.Exp(-dt / tau);
        return current + ((target - current) * a);
    }

    /// <summary>
    /// Pyramid level for current zoom: <c>clamp(floor(log2(1/z)), 0, maxLevel)</c>
    /// so minification stays in (0.5, 1] at the chosen level.
    /// </summary>
    public static int LevelForZoom(float zoom, int maxLevel)
    {
        if (zoom >= 1f || maxLevel <= 0)
        {
            return 0;
        }

        var lod = MathF.Floor(MathF.Log2(1f / zoom));
        return Math.Clamp((int)lod, 0, maxLevel);
    }

    public static float LodFrac(float zoom) =>
        zoom <= 0f ? 0f : MathF.Log2(1f / Math.Max(zoom, 1e-6f));

    public static FilterMode FilterForZoom(float zoom)
    {
        if (zoom < 1f)
        {
            return FilterMode.BicubicParentBlend;
        }

        if (zoom < NearestStart)
        {
            return FilterMode.Bicubic;
        }

        if (zoom < NearestFull)
        {
            return FilterMode.BicubicToNearest;
        }

        return FilterMode.Nearest;
    }

    /// <summary>Fill <see cref="psv_view_cb"/>-shaped floats (32 floats / 128 bytes).</summary>
    public static void PackViewCb(
        Span<float> dest,
        in Affine2x3 imgToDev,
        float panelW,
        float panelH,
        float zoom,
        float gridOpacity = 0f,
        float aaNearest = AaNearestDefault,
        float slotSize = SmoothTileMath.SlotSize,
        float gutter = SmoothTileMath.Border)
    {
        if (dest.Length < 32)
        {
            throw new ArgumentException("Need 32 floats for psv_view_cb.", nameof(dest));
        }

        var inv = imgToDev.Invert();
        dest[0] = imgToDev.A;
        dest[1] = imgToDev.B;
        dest[2] = imgToDev.Tx;
        dest[3] = 0f;
        dest[4] = imgToDev.C;
        dest[5] = imgToDev.D;
        dest[6] = imgToDev.Ty;
        dest[7] = 0f;
        dest[8] = inv.A;
        dest[9] = inv.B;
        dest[10] = inv.Tx;
        dest[11] = 0f;
        dest[12] = inv.C;
        dest[13] = inv.D;
        dest[14] = inv.Ty;
        dest[15] = 0f;
        dest[16] = panelW > 0 ? 2f / panelW : 0f;
        dest[17] = panelH > 0 ? 2f / panelH : 0f;
        dest[18] = zoom;
        dest[19] = LodFrac(zoom);
        dest[20] = NearestStart;
        dest[21] = NearestFull;
        dest[22] = aaNearest;
        dest[23] = 0f;
        dest[24] = Math.Clamp(gridOpacity, 0f, 1f);
        dest[25] = GridZoomStart;
        dest[26] = GridZoomFull;
        dest[27] = 0f;
        dest[28] = slotSize;
        dest[29] = gutter;
        dest[30] = slotSize > 0 ? 1f / slotSize : 0f;
        dest[31] = 0f;
    }

    /// <summary>
    /// Visible tile indices at <paramref name="level"/> for the device viewport
    /// [0,panelW]×[0,panelH], using the inverse of <paramref name="imgToDev"/>.
    /// </summary>
    public static void CollectVisibleTiles(
        in Affine2x3 imgToDev,
        float panelW,
        float panelH,
        int level,
        int fullWidth,
        int fullHeight,
        List<(int Tx, int Ty)> dest)
    {
        dest.Clear();
        var lw = SmoothTileMath.LevelWidth(fullWidth, level);
        var lh = SmoothTileMath.LevelHeight(fullHeight, level);
        var (nx, ny) = SmoothTileMath.TileCounts(lw, lh);
        if (nx < 1 || ny < 1)
        {
            return;
        }

        var inv = imgToDev.Invert();
        var scale = 1 << level;
        Span<(float X, float Y)> corners =
        [
            inv.Transform(0, 0),
            inv.Transform(panelW, 0),
            inv.Transform(0, panelH),
            inv.Transform(panelW, panelH)
        ];

        var minX = corners[0].X;
        var maxX = corners[0].X;
        var minY = corners[0].Y;
        var maxY = corners[0].Y;
        for (var i = 1; i < 4; i++)
        {
            minX = MathF.Min(minX, corners[i].X);
            maxX = MathF.Max(maxX, corners[i].X);
            minY = MathF.Min(minY, corners[i].Y);
            maxY = MathF.Max(maxY, corners[i].Y);
        }

        // Corners are level-0 image px; convert to level image px.
        var lMinX = minX / scale;
        var lMaxX = maxX / scale;
        var lMinY = minY / scale;
        var lMaxY = maxY / scale;

        var tx0 = Math.Max(0, (int)MathF.Floor(lMinX / SmoothTileMath.ContentSize) - 1);
        var ty0 = Math.Max(0, (int)MathF.Floor(lMinY / SmoothTileMath.ContentSize) - 1);
        var tx1 = Math.Min(nx - 1, (int)MathF.Floor(lMaxX / SmoothTileMath.ContentSize) + 1);
        var ty1 = Math.Min(ny - 1, (int)MathF.Floor(lMaxY / SmoothTileMath.ContentSize) + 1);

        for (var ty = ty0; ty <= ty1; ty++)
        {
            for (var tx = tx0; tx <= tx1; tx++)
            {
                dest.Add((tx, ty));
            }
        }
    }
}
