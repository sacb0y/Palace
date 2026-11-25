// Palace Smooth Viewer V1: tile-pyramid downsample (Geo, 2026-11-25). cs_5_0.
// Entry: SmoothMipDownsampleCS, [numthreads(8, 8, 1)], dispatch (ceil(Slot/8), ceil(Slot/8), 1).
//
// Builds ONE level L+1 tile slot (content + border) from level-L tiles in a
// Texture2DArray. The parent's content covers exactly 2x2 child tiles; it also
// reads a one-tile ring around them, so border texels and the wide filters
// get real neighbour pixels and there are no seams between tiles.
//
// Defines (compile one variant per tile format):
//   SMOOTH_SRGB8 = 1 : source SRV is RGBA8_UNORM_SRGB (Load returns linear),
//                      destination UAV is an RGBA8_UNORM view of an
//                      RGBA8_TYPELESS texture; the shader encodes sRGB itself
//                      (UAVs can't be _SRGB).
//   SMOOTH_SRGB8 = 0 : RGBA16F scRGB in and out (default).
//
// All filtering happens in linear light with premultiplied alpha. Set
// StraightAlpha = 1 when tiles store straight alpha, and the shader
// premultiplies on read and un-premultiplies on write.

#ifndef SMOOTH_SRGB8
#define SMOOTH_SRGB8 0
#endif

cbuffer SmoothMipCB : register(b0)
{
    // x: SlotSize (512), y: Border (1; V2 bicubic may want 2), z: Filter (0 box, 1 Lanczos-2, 2 Kaiser), w: StraightAlpha (0/1)
    uint4 Params;
    // x,y: level-L image size in px; z: DstSlice (into Dst); w: AntiRing (1 = clamp wide filters to the 2x2 box footprint; recommended for HDR)
    uint4 LevelInfo;
    // x,y: level-L px of the parent's content origin (= 2 * parent content origin at L+1)
    // z,w: unused
    int4  Origin;
    // 4x4 grid of level-L slices, row-major, covering child tiles -1..2 relative
    // to the parent's first child. 0xFFFFFFFF = outside the image (never read,
    // because coordinates are clamped to the image first).
    uint4 Slices[4];
};

#if SMOOTH_SRGB8
Texture2DArray<float4>              Src : register(t0); // _SRGB view → linear on Load
RWTexture2DArray<unorm float4>      Dst : register(u0); // _UNORM view of TYPELESS
#else
Texture2DArray<float4>              Src : register(t0);
RWTexture2DArray<float4>            Dst : register(u0);
#endif

// Normalised half-kernels for an exact 2:1 reduction, taps at source offsets
// ±0.5, ±1.5, ±2.5, ±3.5 from the destination texel centre.
static const float4 kLanczos2 = float4(0.43430327, 0.11650009, -0.04194003, -0.00886333);
static const float4 kKaiser   = float4(0.43849841, 0.11691984, -0.04299511, -0.01242315); // windowed sinc, beta 4

uint SliceAt(int tx, int ty)
{
    uint4 row = Slices[ty];
    return tx == 0 ? row.x : (tx == 1 ? row.y : (tx == 2 ? row.z : row.w));
}

// p: level-L image px (integer). Clamped to the image, then mapped to the
// child tile that owns it.
float4 FetchL(int2 p)
{
    int2 imgMax = int2(LevelInfo.xy) - 1;
    p = clamp(p, int2(0, 0), imgMax);

    int content = int(Params.x) - 2 * int(Params.y);
    int2 rel = p - Origin.xy;                       // relative to first child's content origin
    int2 t = int2(floor(float2(rel) / content));    // -1..2
    t = clamp(t, int2(-1, -1), int2(2, 2));
    int2 local = rel - t * content + int(Params.y);

    uint slice = SliceAt(t.x + 1, t.y + 1);
    float4 c = Src.Load(int4(local, int(slice), 0));
    if (Params.w != 0u)
    {
        c.rgb *= c.a;
    }
    return c;
}

float Weight(uint filter, int k) // k = 0..3 → offset ±(k + 0.5)
{
    float4 w = filter == 1u ? kLanczos2 : kKaiser;
    return k == 0 ? w.x : (k == 1 ? w.y : (k == 2 ? w.z : w.w));
}

float3 SrgbEncode(float3 c)
{
    c = saturate(c);
    float3 lo = c * 12.92;
    float3 hi = 1.055 * pow(c, 1.0 / 2.4) - 0.055;
    return float3(c.r <= 0.0031308 ? lo.r : hi.r,
                  c.g <= 0.0031308 ? lo.g : hi.g,
                  c.b <= 0.0031308 ? lo.b : hi.b);
}

[numthreads(8, 8, 1)]
void SmoothMipDownsampleCS(uint3 id : SV_DispatchThreadID)
{
    uint slot = Params.x;
    if (id.x >= slot || id.y >= slot)
    {
        return;
    }

    // Destination slot texel → level L+1 content coordinate (border = negative / past content).
    int2 d = int2(id.xy) - int(Params.y);
    // Its 2x2 source footprint in level-L px: s0 = Origin + 2d, s0 + 1.
    int2 s0 = Origin.xy + 2 * d;

    float4 a = FetchL(s0);
    float4 b = FetchL(s0 + int2(1, 0));
    float4 c = FetchL(s0 + int2(0, 1));
    float4 e = FetchL(s0 + int2(1, 1));
    float4 box = (a + b + c + e) * 0.25;
    float4 result = box;

    uint filter = Params.z;
    if (filter != 0u)
    {
        // Separable 8x8 windowed sinc. Source texel s0 + i (i = -3..4) sits at
        // offset (i - 0.5) from the destination centre.
        float4 acc = 0;
        [unroll] for (int j = -3; j <= 4; ++j)
        {
            int kj = j <= 0 ? -j : j - 1;
            float wy = Weight(filter, kj);
            float4 rowAcc = 0;
            [unroll] for (int i = -3; i <= 4; ++i)
            {
                int ki = i <= 0 ? -i : i - 1;
                rowAcc += FetchL(s0 + int2(i, j)) * Weight(filter, ki);
            }
            acc += rowAcc * wy;
        }
        result = acc;
        if (LevelInfo.w != 0u)
        {
            // Sinc lobes ring hard around HDR highlights (and go negative);
            // keep the sharpness but stay inside the box footprint's range.
            float4 lo = min(min(a, b), min(c, e));
            float4 hi = max(max(a, b), max(c, e));
            result = clamp(result, lo, hi);
        }
    }

    if (Params.w != 0u)
    {
        result.rgb = result.a > 1e-6 ? result.rgb / result.a : 0.0;
    }

#if SMOOTH_SRGB8
    result = float4(SrgbEncode(result.rgb), saturate(result.a));
#endif
    Dst[uint3(id.xy, LevelInfo.z)] = result;
}
