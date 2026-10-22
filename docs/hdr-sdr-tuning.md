# HDR→SDR tuning follow-up

Stacked after [#133](https://github.com/sacb0y/Palace/pull/133) (`cursor/hdr-jxl-present-5642`). Does **not** change #133’s validated SDR curve until A/B ships. Version TBD after [#132](https://github.com/sacb0y/Palace/pull/132) (provisional next free after 0.3.10, likely 0.3.11). Low priority behind grid-layout work.

Reference only: [docs/hdr-skiv.md](hdr-skiv.md). Do **not** copy SKIV code.

## Plan (Doktor / Isiac)

1. **Adopt:** measure content peak (p995) once at decode/load; cache with the image so viewer and thumbs share it. Strip NaN/Inf at decode; keep the present-time guard as a backstop.
2. **A/B behind a debug toggle (off by default):** tone map on ICtCp **I** instead of CIE Y; SKIV-style γ1.18 + chroma rescale after the curve (tunable; watch skin). Side-by-side screenshots on a fixed set (PQ JXL, HLG, saturated neon/sky/fire, skin, near-black) — Isiac chooses later.
3. **Skip** mapping peak → 1.5; **keep** SdrWhiteLevel attenuation.

## Out of scope for the first code PR

- FenrirDesktop HDR verification
- Changing #133 tip or #136
- Shipping the ICtCp path as default without A/B screenshots
