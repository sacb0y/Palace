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
   `chromaticities` (`ExrHeader` / `NormalizeOptions.Chromaticities`). Strip
   NaN/Inf; clamp neg Y; source-clipped on encoded max / mastering peak only
   (not buffer max — S1). Flag stored on `ImageStats`; nothing reads it in P1.
2. **Analyze** — **PQ-spaced** histogram: p50, p99.995, max, maxRGB; `Lc` safe
   clamp. P50 = scene-average knee.
3. **ToneMap** — `ExposureEV` as `2^EV` before the curve (Analyze Lc unchanged;
   curve uses **Lc·2^EV** — P2). SDR: Hermite reaches **display white at Lc**
   (paper slightly below when Lc&gt;1 — P1); soft-clip safety-net only. HDR:
   BT.2390-8 §5.4.1 EETF. **P5 owns wiring Ld**. Desat vs Ld.
4. **LUT** — PQ-spaced; EV at sample time; non-zero EV re-evaluates Lc·2^EV.
   Default size **256**.

## ExrRefWhite / Pregmata_SteamAVIF (M4)

**`ExrRefWhite = 80`** (`ScrgbNits`) — provisional pending a broader title corpus.
Metadata-less EXR / `.hdr` **1.0 = 80 nits** → working **80/203**. Measured on
Pregmata_SteamAVIF (3840×2160): EXR ≡ JXR (luminance ratio median 1.0000);
.hdr/JXR 0.9968 (RGBE); no EXR whiteLuminance/chromaticities; no .hdr
EXPOSURE/PRIMARIES; peak maxRGB ≈ 10.05 scRGB (~804 nits).

## Fixtures

`Palace.Tests/Fixtures/imaging/` — one master → real **JXL, AVIF, PNG-cICP,
JXR, EXR, HDR** plus float dumps. Regenerate floats:
`python3 scripts/gen-imaging-fixtures.py`.

## Out of scope

Decoders (P2), thumbs/full flags (P3–P4), HLSL / CPU–GPU agreement (P5),
deleting legacy (P6). No FenrirDesktop.
