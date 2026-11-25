# Smooth Viewer HLSL (Geo)

| File | Role |
|---|---|
| `SmoothMipDownsample.hlsl` | **V1** GPU detail levels — `SmoothMipDownsampleCS` (cs_5_0). One dispatch per L+1 tile slot. |
| `SmoothImage.hlsl` | **V2** image pass — `SmoothImageVS` / `SmoothImagePS` (filter-by-zoom, parent blend, tone, grid hooks) |
| `README-mip-downsample.md` | Geo notes for the mip CS |

## V1 mip rules

- Entry: `SmoothMipDownsampleCS`, `[numthreads(8,8,1)]`, dispatch `ceil(512/8)²`
- `Border=2` → **508** content in 512 slots (Catmull-Rom gutter)
- Filter: **box** (`Params.z=0`) default; AntiRing ON; Lanczos-2/Kaiser later
- Premultiplied: `StraightAlpha=0`
- `SMOOTH_SRGB8=0` — RGBA16F linear Working (BT.2020, 1.0 = 203 nits). Do not use `=1` in V1.
- Never bind same array as SRV+UAV: write scratch 512² then `CopyTexture` into the slot
- `SmoothDownsample.hlsl` is **not** used for V1

`SmoothImage.hlsl` `#include "ToneMap.hlsl"` resolves via Diligent stream factory to
`Palace.Imaging/Shaders/ToneMap.hlsl` (**no copy**). Cbuffers: `include/smooth_image_cb.h`.
