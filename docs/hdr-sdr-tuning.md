# HDR imaging rewrite (P1)

Isiac-approved Doktor plan v2. **Source of truth (store):**
`docs/hdr-imaging-rewrite.md`.

**PR:** [#137](https://github.com/sacb0y/Palace/pull/137) — pure `Palace.Imaging`
+ tests. **No UI.** Doktor reviews; no Bugbot.

## Licensing

Tone curves are implemented from **published specs** (BT.2100 / ST 2084,
BT.2390 §5.4.1 EETF, BT.2408 203 nits). Palace does **not** ship libplacebo or
other LGPL-derived source. See `Palace.Imaging/THIRD_PARTY.md`.

## Working space

BT.2020 linear, **1.0 = 203 nits**.

## Pipeline (P1)

1. **Normalize** — PQ nits/203; HLG BT.2100 OOTF @ 1000 then /203; JXR scRGB
   `×80/203` then BT.709→2020; SDR sRGB EOTF + 709→2020; EXR/`.hdr`
   `x * whiteLuminance / 203` (default `ExrRefWhite = 203` so relative 1.0 →
   working 1.0). Primaries honored. Strip NaN/Inf; clamp neg Y; source-clipped
   on encoded max / mastering peak (incl. linear EXR/JXR).
2. **Analyze** (histogram, full-res) — p50, p99.995, max, maxRGB;
   `Lc` with safe clamp when metadata peak &lt; floor.
3. **ToneMap** — `ExposureEV` as `2^EV` before the curve (Lc unchanged). SDR:
   Palace Hermite curve anchoring **ref white → display white**. HDR: BT.2390
   EETF; identity when `Lc <= Ld`. maxRGB + ratio; desaturate; `HighlightSoftClip`.
4. **LUT** — 1D samples in **PQ space**; EV applied at sample time (not in cache
   key). Key: file + pipeline version + DisplayTarget content hash + stats hash + size.

## ExrRefWhite / Pragmata

Provisional **`ExrRefWhite = 203`** (relative EXR 1.0 ≡ paper white under
`x*W/203`). Pragmata EXR / `.hdr` / JXR pixel-ratio measurement is **pending**
(files not in CI). Record measured ratios here when available.

## Fixtures

`Palace.Tests/Fixtures/imaging/` — one master → PQ float, Radiance `.hdr`,
scene-709 float, scRGB float. Regenerate: `python3 scripts/gen-imaging-fixtures.py`.
Replace float stand-ins with `cjxl` / `avifenc` / OpenEXR exports when those
tools are on the CI image.

## Out of scope

Decoders (P2), thumbs/full flags (P3–P4), HLSL / CPU–GPU agreement (P5),
deleting legacy (P6). No FenrirDesktop.
