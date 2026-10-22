# Third-party notices — Palace.Imaging

Palace.Imaging does **not** include libplacebo (or any LGPL) source.

Tone-mapping and knee math are implemented from **published specifications
and reports**, with Palace-owned structure:

| Piece | Public reference | Notes |
|---|---|---|
| PQ EOTF/OETF | Rec. ITU-R BT.2100 / SMPTE ST 2084 | Coefficients only |
| HLG OETF/OOTF | Rec. ITU-R BT.2100 | γ = 1.2, Lw = 1000 |
| HDR EETF | Report ITU-R BT.2390 §5.4.1 | Hermite knee `KS = 1.5·maxLum − 0.5`; black lift `b·(1−E₂)⁴` |
| Reference white | Report ITU-R BT.2408 | 203 cd/m² paper white |
| SDR curve | Palace Hermite/shoulder design | Anchors working 1.0 → display white; knee from scene average (ST 2094-40 *idea* of brightness matching — not a code port) |
| Highlight soft-clip | Palace rational shoulder @ constant Y | Named `HighlightSoftClip` — not a third-party port |

Published numeric constants (203 nits, KS formula, PQ coefficients) are used as
tuning values. **Do not paste LGPL or other copyleft source into this tree.**
