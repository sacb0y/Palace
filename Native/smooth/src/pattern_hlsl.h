#pragma once

/* Placeholder HDR scRGB test pattern until Geo lands tile/tone-map HLSL.
 * Outputs linear scRGB (1.0 = 80 nits). Bright lobe peaks ~4.0 (~320 nits). */

static const char kPatternVS[] = R"(
struct VSOut
{
    float4 Pos : SV_Position;
    float2 Uv  : TEXCOORD0;
};

VSOut main(uint id : SV_VertexID)
{
    VSOut o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.Pos = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    o.Uv = uv;
    return o;
}
)";

static const char kPatternPS[] = R"(
struct PSIn
{
    float4 Pos : SV_Position;
    float2 Uv  : TEXCOORD0;
};

float4 main(PSIn i) : SV_Target
{
    float2 uv = i.Uv;
    float3 dark = float3(0.02, 0.025, 0.04);
    float3 mid  = float3(0.35, 0.55, 0.85);
    float3 hot  = float3(4.0, 2.8, 1.2); /* >1.0 = HDR scRGB */

    float3 base = lerp(dark, mid, uv.x);
    float2 c = uv - float2(0.5, 0.45);
    float r = length(c);
    float lobe = saturate(1.0 - r * 2.2);
    lobe = lobe * lobe;
    float3 rgb = base + hot * lobe;

    /* Soft grid so resize/DPI evidence is visible */
    float gx = abs(frac(uv.x * 8.0) - 0.5);
    float gy = abs(frac(uv.y * 8.0) - 0.5);
    float grid = smoothstep(0.48, 0.5, max(gx, gy));
    rgb = lerp(rgb, rgb * 0.65, grid * 0.35);

    return float4(rgb, 1.0);
}
)";
