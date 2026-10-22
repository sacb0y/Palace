# HDR imaging rewrite (P1)

Isiac-approved Doktor plan v2. **Source of truth (store):**
`docs/hdr-imaging-rewrite.md`.

**PR:** [#137](https://github.com/sacb0y/Palace/pull/137) — pure `Palace.Imaging`
+ tests. **No UI.** Doktor reviews; no Bugbot.

## Licensing

Tone curves are implemented from **published specs** (BT.2100 / ST 2084,
BT.2390-8 §5.4.1 EETF, BT.2408 203 nits). Palace does **not** ship libplacebo or
other LGPL-derived source. See `Palace.Imaging/THIRD_PARTY.md`.

**#137 must be squash-merged** — an earlier draft commit briefly added a
libplacebo-derived file; squash keeps that revision out of `main` history.

## Working space

BT.2020 linear, **1.0 = 203 nits**.

## Pipeline (P1)

1. **Normalize** — PQ nits/203; HLG BT.2100 OOTF @ 1000 then /203; JXR scRGB
   `×80/203` then BT.709→2020; SDR sRGB EOTF + 709→2020; EXR/`.hdr`
   `x * whiteLuminance / 203` (default `ExrRefWhite = 203` so relative 1.0 →
   working 1.0). Primaries from enum or EXR `chromaticities` attribute
   (`ExrHeader` / `NormalizeOptions.Chromaticities`). Strip NaN/Inf; clamp
   neg Y; source-clipped on encoded max / mastering peak **or** buffer max
   when linear sources lack peak metadata (flag consumed via `ImageStats`).
2. **Analyze** — **PQ-spaced** histogram (not linear bins / full sort): p50,
   p99.995, max, maxRGB; `Lc` with safe clamp when metadata peak &lt; floor.
   P50 is the scene-average knee input for the SDR curve.
3. **ToneMap** — `ExposureEV` as `2^EV` before the curve (Lc unchanged). SDR:
   Palace Hermite shoulder (knee→ref white→Lc, strictly increasing). HDR:
   BT.2390-8 §5.4.1 EETF (input clamped to Lc); identity when `Lc <= Ld`.
   **P5 owns wiring Ld** on the HDR present path. maxRGB + ratio; desat on
   tone-mapped output vs Ld (`smoothstep(Ld, 1.25·Ld)`); `HighlightSoftClip`.
4. **LUT** — 1D samples in **PQ space**; EV applied at sample time (not in cache
   key). Default size **256** (also verified at 1024). Key: file + pipeline
   version + DisplayTarget **stable** content hash + stats hash + size.

## ExrRefWhite / Pragmata

Provisional **`ExrRefWhite = 203`** (relative EXR 1.0 ≡ paper white under
`x*W/203`). **M4 left provisional** — Isiac will measure Pragmata on laptop
and send numbers. Record measured ratios here when available.

## Fixtures

`Palace.Tests/Fixtures/imaging/` — one master → real **JXL, AVIF, PNG-cICP,
JXR, EXR, HDR** plus float dumps. Regenerate floats:
`python3 scripts/gen-imaging-fixtures.py`.

## Out of scope

Decoders (P2), thumbs/full flags (P3–P4), HLSL / CPU–GPU agreement (P5),
deleting legacy (P6). No FenrirDesktop.
