# shaders/smooth: V1 tile-pyramid downsample (Geo, 2026-11-25)

`SmoothMipDownsample.hlsl` holds `SmoothMipDownsampleCS` (cs_5_0, 8×8 threads). One dispatch writes one level L+1 tile slot, including its border, from level-L tiles in a `Texture2DArray`. Compile-checked with DXC 1.9 (cs_6_0) for both variants. It doesn't use SM6 features, so it is written for D3DCompile cs_5_0 too; that still needs a real FXC compile on Windows.

## Variants (compile once per tile format)

| Define | Source SRV | Destination UAV |
|---|---|---|
| `SMOOTH_SRGB8=0` (default) | RGBA16F scRGB | RGBA16F |
| `SMOOTH_SRGB8=1` | `RGBA8_UNORM_SRGB` view (Load already returns linear) | `RGBA8_UNORM` view of an `RGBA8_TYPELESS` texture. UAVs can't be `_SRGB`, so the shader does the sRGB encode itself |

Filtering is always in linear light with premultiplied alpha. Set `StraightAlpha=1` if your tiles store straight alpha; the shader then premultiplies on read and un-premultiplies on write.

## Filters (`Params.z`)

- `0`: box 2×2. This is an exact 2:1 reduction, cheapest, and matches the placeholder.
- `1`: Lanczos-2, a separable 8×8.
- `2`: Kaiser-windowed sinc (β 4), 8×8. It's slightly sharper than Lanczos.
- `AntiRing` (`LevelInfo.w = 1`) clamps the sinc result to the box footprint's min/max. Keep it on: without it, the HDR highlights get halos and the scRGB values go negative.

## Binding and constants

```
Params    = { SlotSize 512, Border 1, Filter, StraightAlpha }
LevelInfo = { levelW, levelH (level-L image px), DstSlice, AntiRing }
Origin    = { 2 * parentContentX, 2 * parentContentY, 0, 0 } // level-L px of the parent's first child content origin
Slices[4] = 4×4 level-L slice grid, row-major: child tiles -1..2 around the parent's 2×2 children
            (0xFFFFFFFF for tiles outside the image; never read, because coords clamp to the image)
t0 Src (Texture2DArray), u0 Dst (RWTexture2DArray), b0 SmoothMipCB
Dispatch(ceil(512/8), ceil(512/8), 1)
```

The one-tile ring is what keeps parent tiles seamless. Border texels and the 8-tap filters read real neighbour pixels, not clamped child borders. There's no separate border-fill pass, and the image edge is edge-replicated. A small Python check of the tile lookup against a full-image box downsample matched exactly (max error 1e-16), including the borders and the image-edge tiles.

- **`Border`:** it's a parameter, so V2 can move to a 2-texel gutter with no shader change if bicubic needs it.
- **Odd level sizes:** level L+1 is `ceil(L/2)`. The last column/row reuses the edge texel, which shifts that edge by at most half a pixel. That's fine for preview.
- **SRV and UAV on the same resource:** you can't bind both in one dispatch. Write to a scratch 512² single-slice texture (`DstSlice 0`), then `CopyTexture` it into the array slot. Or keep one array per level.
- **Order:** build levels bottom-up (L0 tiles → L1 → …) until the level fits in one tile. Keep every level up to 2048 resident.
