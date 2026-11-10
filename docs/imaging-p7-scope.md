# Imaging P7 scope (B0 — docs only)

Isiac via Constructor (2026-11-10). Last numbered imaging-rewrite slice.

## Decision

**P7 = B0 only.** Document reality; **no behaviour change.**

| Topic | Status |
|---|---|
| Highlight desat (`GamutMap.DesaturateHighlights`) | **Structurally dead** under Option A / BT.2390: maxRGB scale maps peak to `MapScalar(max) ≤ Ld` (SDR display white / HDR Ld), so `smoothstep(Ld, 1.25·Ld, peak)` never fires. Call sites kept as a no-op safety hook. |
| Desat engagement A1–A4 | **Deferred** pending Isiac A/B — not invented in this slice. |
| Gain maps / Ultra HDR | Stay **detect + show SDR base** (hdr-skiv / existing `HdrKind.UltraHdrJpeg` probe). No reconstruct / libultrahdr. |
| `PipelineVersion` | **Unchanged** (6). |
| A3 / R1/R3/R4 | Untouched (deferred / not invented). |

## Not in P7

- A1 pre-map compression desat
- A2 desat vs paper white
- A3 restore headroom above Ld
- A4 per-channel curve
- B1 badge/Info polish beyond current status line
- B2 apply gain maps to Working HDR

## See also

- Store plan: Project `docs/hdr-imaging-rewrite.md`
- In-repo pipeline notes: [`hdr-sdr-tuning.md`](./hdr-sdr-tuning.md)
