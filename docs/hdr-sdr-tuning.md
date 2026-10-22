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
   `x * whiteLuminance / 203` (default **`ExrRefWhite = 80`** so metadata-less
   1.0 → working **80/203** — Pregmata_SteamAVIF). Primaries from enum or EXR
   `chromaticities` (`ExrHeader` / `NormalizeOptions.Chromaticities`; MatchKnown
   covers BT.709 / BT.2020 / Display P3). Strip NaN/Inf; clamp neg Y;
   source-clipped on encoded max / mastering peak only (not buffer max — S1).
   Flag stored on `ImageStats`; nothing reads it in P1.
2. **Analyze** — **PQ-spaced** histogram: p50, p99.995, max, maxRGB; `Lc` safe
   clamp. P50 = scene-average knee.
3. **ToneMap** — `ExposureEV` as `2^EV` before the curve (Analyze Lc unchanged;
   curve uses **Lc·2^EV** — P2). SDR **Option A** (Isiac): paper =
   `displayWhite · PaperWhiteFraction(Lc)` with `t=(1−1/L)²` — exactly 1 at
   Lc=1 (C¹, no kink), dip ≤ ~10% (`MaxPaperDip`); shoulder reaches **display
   white at Lc**; **SDR output ≤ display white**. HDR: BT.2390-8 §5.4.1 clamps
   at **Ld** (no headroom above white); **P5 owns wiring Ld**. Desat =
   `smoothstep(Ld, 1.25·Ld, peak)` — with the curve ending at white / Ld, peak
   rarely exceeds Ld, so desat **currently never engages (P7)**.
4. **LUT** — PQ-spaced; EV at sample time (not in cache key). **Non-zero EV
   bypasses the baked LUT** and re-evaluates `MapScalar` with Lc·2^EV (**P5**).
   Default size **256**. `PipelineVersion` = **6**.

## ExrRefWhite / Pregmata_SteamAVIF (M4)

**`ExrRefWhite = 80`** (`ScrgbNits`). Metadata-less EXR / `.hdr` **1.0 = 80 nits**
→ working **80/203**. Measured on Pregmata_SteamAVIF (3840×2160): EXR ≡ JXR
(luminance ratio median 1.0000); .hdr/JXR 0.9968 (RGBE); no EXR
whiteLuminance/chromaticities; no .hdr EXPOSURE/PRIMARIES; peak maxRGB ≈ 10.05
scRGB (~804 nits).

## Fixtures

`Palace.Tests/Fixtures/imaging/` — one master → real **JXL, AVIF, PNG-cICP,
JXR, EXR, HDR** plus float / `*.decoded.f32` dumps. See that folder’s README
for regenerate commands.

## Out of scope (P1)

Thumbs/full flags (P3–P4), HLSL / CPU–GPU agreement (P5), deleting legacy
(P6), gain maps / desat tuning (P7). No FenrirDesktop.

**P2 (decoders):** draft [#138](https://github.com/sacb0y/Palace/pull/138) —
`ImagingStillNormalize` + `HdrFrame.Working` beside scRGB present.
