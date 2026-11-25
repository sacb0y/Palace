// Palace Smooth Viewer V1: image pass (Geo, 2026-11-25).
// Target: D3DCompile vs_5_0 / ps_5_0 via Diligent (no SM6 features used).
// Entry points: SmoothImageVS, SmoothImagePS.
//
// Input tiles hold Palace.Imaging Working: BT.2020 linear, 1.0 = 203 nits,
// PREMULTIPLIED alpha, RGBA16F, 512x512 slots with a 2-texel gutter
// (508x508 content). Output is scRGB FP16 (BT.709 linear, 1.0 = 80 nits),
// premultiplied, blended ONE / INV_SRC_ALPHA over the cleared stage.
//
// Tone mapping is the P5 reference in Palace.Imaging/Shaders/ToneMap.hlsl
// (include it, do not copy it) so the GPU stays in lockstep with ToneMapper.

#include "ToneMap.hlsl"

// ---------------------------------------------------------------------------
// Constants. Everything is float4 so the C side packs trivially (see
// smooth_image_cb.h). All matrices are row-major 2x3 affines in device pixels.
// ---------------------------------------------------------------------------
cbuffer SmoothViewCB : register(b0)
{
    float4 ImgToDev0;    // xy: row 0 (dev.x = dot(xy, img) + z), z: tx, w: unused
    float4 ImgToDev1;    // xy: row 1 (dev.y = dot(xy, img) + z), z: ty, w: unused
    float4 DevToImg0;    // inverse of the above, same layout
    float4 DevToImg1;
    float4 Viewport;     // x: 2/W, y: 2/H (device px), z: Zoom (dev px per level-0 px), w: Lod = log2(1/Zoom)
    float4 FilterParams; // x: NearestStart (4), y: NearestFull (6), z: AaNearest (1 = sharp-bilinear, 0 = hard nearest), w: unused
    float4 GridParams;   // x: GridOpacity (toggle * eased 0..1), y: GridZoomStart (8), z: GridZoomFull (10), w: unused
    float4 TileParams;   // x: SlotSize (512), y: Gutter (2), z: 1/SlotSize, w: unused
};

cbuffer SmoothToneCB : register(b1)
{
    float4 ToneA;        // x: Lc, y: P50, z: PeakNormalized, w: Ld          (ImageStats / DisplayTarget)
    float4 ToneB;        // x: ExposureEv, y: ExposureGain = 2^EV, z: UseHdrPath (0/1), w: OutputLimit (Ld or PeakNormalized)
    float4 LutParams;    // x: InputMinPq, y: InputMaxPq, z: LutSize, w: HasLut (0/1)
    float4 PresentParams;// x: OutputScale, y: ClipScrgb, z: SoftNegatives (0/1), w: PaperScrgb (paper white in scRGB, for the grid)
};

struct TileInstance
{
    float2 ImgOrigin;    // level-0 image px of this tile's content top-left
    float2 ImgSize;      // level-0 image px covered (clipped at the image edge)
    float2 ParentOrigin; // level-0 image px of the parent tile's content top-left
    float  LevelScale;   // 2^Level
    float  Level;        // pyramid level of this tile
    uint   Slice;        // array slice of this tile
    uint   ParentSlice;  // array slice of the Level+1 tile covering this one, 0xFFFFFFFF if not resident
    float  Opacity;      // 0..1, sharpen crossfade (80-120 ms) when this tile replaces a coarser one
    float  Pad;
};

StructuredBuffer<TileInstance> Tiles   : register(t0);
Texture2DArray<float4>         TileArr : register(t1);
Texture2D<float>               ToneLut : register(t2); // LutSize x 1, R32F, ToneMapLut.Samples (EV = 0)
SamplerState                   LinearClamp : register(s0);

struct VSOut
{
    float4 Pos       : SV_Position;
    float2 Img       : TEXCOORD0;   // level-0 image px
    nointerpolation uint  Inst : TEXCOORD1;
};

// ---------------------------------------------------------------------------
// VS: one instanced quad per resident tile, 4 verts as a triangle strip.
// ---------------------------------------------------------------------------
VSOut SmoothImageVS(uint vid : SV_VertexID, uint iid : SV_InstanceID)
{
    TileInstance t = Tiles[iid];
    float2 corner = float2(vid & 1, vid >> 1);
    float2 img = t.ImgOrigin + corner * t.ImgSize;

    float2 dev;
    dev.x = dot(ImgToDev0.xy, img) + ImgToDev0.z;
    dev.y = dot(ImgToDev1.xy, img) + ImgToDev1.z;

    VSOut o;
    o.Pos = float4(dev.x * Viewport.x - 1.0, 1.0 - dev.y * Viewport.y, 0.0, 1.0);
    o.Img = img;
    o.Inst = iid;
    return o;
}

// ---------------------------------------------------------------------------
// Filters. p is in slot texels (texel centres at i + 0.5).
// ---------------------------------------------------------------------------
float4 LoadTexel(float2 i, uint slice)
{
    return TileArr.Load(int4(int2(i), int(slice), 0));
}

// Catmull-Rom with 9 bilinear taps; footprint is floor(p - 0.5) - 1 .. + 2,
// which is why the gutter is 2 texels. Clamped to the 2x2 neighbourhood so
// HDR highlights don't ring (Catmull-Rom overshoot is very visible at 10x
// the paper level).
float4 SampleBicubic(float2 p, uint slice)
{
    float inv = TileParams.z;
    float2 c1 = floor(p - 0.5) + 0.5;
    float2 f  = p - c1;

    float2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f));
    float2 w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
    float2 w2 = f * (0.5 + f * (2.0 - 1.5 * f));
    float2 w3 = f * f * (-0.5 + 0.5 * f);
    float2 w12 = w1 + w2;
    float2 o12 = w2 / w12;

    float2 t0  = (c1 - 1.0) * inv;
    float2 t3  = (c1 + 2.0) * inv;
    float2 t12 = (c1 + o12) * inv;
    float s = (float)slice;

    float4 r = 0;
    r += TileArr.SampleLevel(LinearClamp, float3(t0.x,  t0.y,  s), 0) * w0.x  * w0.y;
    r += TileArr.SampleLevel(LinearClamp, float3(t12.x, t0.y,  s), 0) * w12.x * w0.y;
    r += TileArr.SampleLevel(LinearClamp, float3(t3.x,  t0.y,  s), 0) * w3.x  * w0.y;
    r += TileArr.SampleLevel(LinearClamp, float3(t0.x,  t12.y, s), 0) * w0.x  * w12.y;
    r += TileArr.SampleLevel(LinearClamp, float3(t12.x, t12.y, s), 0) * w12.x * w12.y;
    r += TileArr.SampleLevel(LinearClamp, float3(t3.x,  t12.y, s), 0) * w3.x  * w12.y;
    r += TileArr.SampleLevel(LinearClamp, float3(t0.x,  t3.y,  s), 0) * w0.x  * w3.y;
    r += TileArr.SampleLevel(LinearClamp, float3(t12.x, t3.y,  s), 0) * w12.x * w3.y;
    r += TileArr.SampleLevel(LinearClamp, float3(t3.x,  t3.y,  s), 0) * w3.x  * w3.y;

    float2 i0 = c1 - 0.5;
    float4 a = LoadTexel(i0, slice);
    float4 b = LoadTexel(i0 + float2(1, 0), slice);
    float4 c = LoadTexel(i0 + float2(0, 1), slice);
    float4 d = LoadTexel(i0 + float2(1, 1), slice);
    float4 lo = min(min(a, b), min(c, d));
    float4 hi = max(max(a, b), max(c, d));
    return clamp(r, lo, hi);
}

// Nearest for magnification. aa = 1: "sharp bilinear", pixel edges get a
// 1-device-px ramp so non-integer zooms don't show uneven column widths and
// motion doesn't shimmer; aa = 0: hard nearest (use when snapped at rest if
// Isiac wants literal pixels). zoom = device px per slot texel (>= 1 here).
float4 SampleNearest(float2 p, uint slice, float zoom, float aa)
{
    if (aa < 0.5)
    {
        return LoadTexel(floor(p), slice);
    }
    float2 t  = p - 0.5;
    float2 i  = floor(t);
    float2 f  = t - i;
    float2 f2 = saturate((f - 0.5) * zoom + 0.5);
    float2 uv = (i + f2 + 0.5) * TileParams.z;
    return TileArr.SampleLevel(LinearClamp, float3(uv, (float)slice), 0);
}

float4 SampleTile(float2 p, uint slice, float zoomTexel)
{
    float nStart = FilterParams.x;
    float nFull  = FilterParams.y;
    float zoom   = Viewport.z;
    if (zoom <= nStart)
    {
        return SampleBicubic(p, slice);
    }
    float4 n = SampleNearest(p, slice, zoomTexel, FilterParams.z);
    if (zoom >= nFull)
    {
        return n;
    }
    float k = smoothstep(nStart, nFull, zoom);
    return lerp(SampleBicubic(p, slice), n, k);
}

// ---------------------------------------------------------------------------
// Tone map: mirrors ToneMapLut.MapPixel / ToneMapper.MapPixel exactly
// (gain, maxRGB, LUT when EV = 0 else MapScalar, scale, clip at the limit).
// ---------------------------------------------------------------------------
float SampleToneLut(float postExposureMaxRgb)
{
    if (postExposureMaxRgb <= 0.0)
    {
        return 0.0;
    }
    float pq = PalaceNitsToPq(postExposureMaxRgb * RefWhiteNits);
    float t = saturate((pq - LutParams.x) / max(LutParams.y - LutParams.x, 1e-8));
    float idx = t * (LutParams.z - 1.0);
    float i0 = floor(idx);
    float i1 = min(LutParams.z - 1.0, i0 + 1.0);
    float f = idx - i0;
    float s0 = ToneLut.Load(int3(int(i0), 0, 0));
    float s1 = ToneLut.Load(int3(int(i1), 0, 0));
    return s0 * (1.0 - f) + s1 * f;
}

float3 ToneMapWorking(float3 rgb)
{
    float lc = ToneA.x, p50 = ToneA.y, peakN = ToneA.z, ld = ToneA.w;
    float ev = ToneB.x, gain = ToneB.y;
    bool hdr = ToneB.z > 0.5;
    float limit = ToneB.w;

    rgb *= gain;
    float m = max(rgb.r, max(rgb.g, rgb.b));
    if (m <= 1e-12)
    {
        return 0.0;
    }

    float mapped;
    if (LutParams.w > 0.5 && abs(ev) < 1e-6)
    {
        mapped = SampleToneLut(m);
    }
    else
    {
        mapped = PalaceMapScalar(m, lc, p50, peakN, ld, hdr, gain);
    }
    rgb *= mapped / m;

    // GamutMap.HighlightSoftClip: identity unless maxRGB > limit, then per
    // channel clamp to [0, limit]. (Desat is structurally dead, P7 B0.)
    if (max(rgb.r, max(rgb.g, rgb.b)) > limit + 1e-6)
    {
        rgb = clamp(rgb, 0.0, limit);
    }
    return rgb;
}

float3 Bt2020To709(float3 c)
{
    return float3(
        dot(float3( 1.6604910, -0.5876411, -0.0728499), c),
        dot(float3(-0.1245505,  1.1328999, -0.0083494), c),
        dot(float3(-0.0181508, -0.1005789,  1.1187297), c));
}

// GalleryPresent: ×OutputScale, then HDR keeps negatives and clips the top,
// SDR clamps to [0, clip].
float3 PresentScrgb(float3 c709)
{
    float outScale = PresentParams.x, clipHi = PresentParams.y;
    if (PresentParams.z < 0.5)
    {
        c709 = max(c709, 0.0);
    }
    c709 *= outScale;
    if (PresentParams.z > 0.5)
    {
        return min(c709, clipHi);
    }
    return clamp(c709, 0.0, clipHi);
}

// ---------------------------------------------------------------------------
// Pixel grid (V4 spec): 1 device px lines on source-pixel boundaries, drawn
// from 8x and full by 10x; light on dark, dark on light, ~30% (36% on mid
// greys so it never sinks below ~24% visible contrast). Works for any
// 90-degree EXIF orientation because the footprint is taken from the inverse
// affine. Half-open (lo, hi] so a boundary on a device-pixel edge is drawn
// exactly once.
// ---------------------------------------------------------------------------
float GridCoverage(float2 devPx)
{
    float2 lo, hi;
    float2 d0 = devPx;                  // pixel's top-left corner
    float2 d1 = devPx + 1.0;            // bottom-right
    float2 a = float2(dot(DevToImg0.xy, d0) + DevToImg0.z, dot(DevToImg1.xy, d0) + DevToImg1.z);
    float2 b = float2(dot(DevToImg0.xy, d1) + DevToImg0.z, dot(DevToImg1.xy, d1) + DevToImg1.z);
    lo = min(a, b);
    hi = max(a, b);
    const float eps = 1e-4;
    float2 hit = step(0.5, floor(hi - eps) - floor(lo - eps));
    return max(hit.x, hit.y);
}

float3 ApplyGrid(float3 scrgb, float2 devPx)
{
    float zoom = Viewport.z;
    float k = GridParams.x * saturate((zoom - GridParams.y) / max(GridParams.z - GridParams.y, 1e-3));
    if (k <= 0.0)
    {
        return scrgb;
    }
    float cov = GridCoverage(devPx);
    if (cov <= 0.0)
    {
        return scrgb;
    }
    float paper = max(PresentParams.w, 1e-3);
    float3 rel = max(scrgb / paper, 0.0);
    float y = dot(rel, float3(0.2126, 0.7152, 0.0722));
    float l = pow(saturate(y), 1.0 / 2.2);
    float target = l > 0.5 ? 0.0 : 1.0;
    float alpha = lerp(0.30, 0.36, 1.0 - saturate(abs(l - 0.5) * 2.0)) * k * cov;
    // Blend in a gamma-2.2 space so 30% reads as 30% on screen; values above
    // paper white (HDR highlights) are compressed to 1 for the blend only.
    float3 g = pow(saturate(rel), 1.0 / 2.2);
    float3 gOut = lerp(g, target.xxx, alpha);
    float3 outRel = pow(gOut, 2.2);
    // Keep the HDR excess on unlined channels rather than clipping the image.
    float3 excess = max(rel - 1.0, 0.0) * (1.0 - alpha);
    return (outRel + excess) * paper;
}

// ---------------------------------------------------------------------------
// PS
// ---------------------------------------------------------------------------
float4 SmoothImagePS(VSOut i) : SV_Target
{
    TileInstance t = Tiles[i.Inst];
    float gutter = TileParams.y;

    float2 p = gutter + (i.Img - t.ImgOrigin) / t.LevelScale;
    float zoomTexel = Viewport.z * t.LevelScale;
    float4 w = SampleTile(p, t.Slice, zoomTexel);

    // Trilinear-style blend toward the Level+1 tile so zooming out never
    // steps in sharpness when the CPU switches level at 1/2, 1/4, ...
    if (t.ParentSlice != 0xFFFFFFFFu && Viewport.z < 1.0)
    {
        float f = saturate(Viewport.w - t.Level);
        if (f > 0.0)
        {
            float2 pp = gutter + (i.Img - t.ParentOrigin) / (t.LevelScale * 2.0);
            float4 wp = SampleBicubic(pp, t.ParentSlice);
            w = lerp(w, wp, f);
        }
    }

    float a = saturate(w.a);
    float3 straight = a > 1e-5 ? w.rgb / a : 0.0;
    float3 mapped = ToneMapWorking(straight);
    float3 scrgb = PresentScrgb(Bt2020To709(mapped));
    scrgb = ApplyGrid(scrgb, floor(i.Pos.xy));

    float o = a * saturate(t.Opacity);
    return float4(scrgb * o, o);
}
