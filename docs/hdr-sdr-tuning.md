# HDR imaging rewrite (P1)

Isiac-approved Doktor plan v2. This file replaces the earlier HDR→SDR tuning
follow-up note.

**Source of truth (store):** Project Context
`docs/hdr-imaging-rewrite.md` (coordinator store).

**PR:** [#137](https://github.com/sacb0y/Palace/pull/137) — pure `Palace.Imaging`
library + tests. **No UI wiring** (ExposureEV is a library input; Unity-style
slider comes later after Gooey).

## Working space

BT.2020 linear, **1.0 = 203 nits** (BT.2408 reference white).

## Pipeline (P1)

1. **Normalize** — PQ nits/203; HLG BT.2100 OOTF @ 1000 then /203; JXR scRGB
   `×80/203`; SDR linearize; EXR/`.hdr` via `whiteLuminance` or `ExrRefWhite`
   (default 1.0). Strip NaN/Inf; clamp negative luminance; source-clipped flag.
2. **Analyze** (once, full-res) — p50, p99.995, max, maxRGB;
   `Lc = clamp(p99.995, lo=max(1.0,2*p50,ref), hi=metadata peak)`.
3. **ToneMap** — `ExposureEV` (default 0) as `2^EV` before the curve; does
   **not** re-analyze or move Lc. SDR: libplacebo spline @ `02d7e35e`. HDR:
   BT.2390 EETF, identity when `Lc <= Ld`. maxRGB + ratio; desaturate
   `smoothstep(0.8*Ld,Ld,out)`; Mobius gamut soft-clip @ constant Y.
4. **LUT** — 1D bake; cache key includes file, pipeline version, DisplayTarget,
   size, **ExposureEV**. Curve samples are EV-agnostic so a live slider can
   multiply in-shader later.

## Out of scope here

Decoders/registry (P2), thumbs/full flags (P3–P4), HDR passthrough / HLSL
(P5), deleting legacy (P6), gain maps (P7). No FenrirDesktop. No Bugbot —
Doktor reviews.
