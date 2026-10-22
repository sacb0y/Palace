# Third-party notices — Palace.Imaging

Palace.Imaging does **not** include libplacebo (or any LGPL) source. An earlier
draft commit briefly added a libplacebo-derived file; **#137 must be
squash-merged** so that revision never lands in `main` history.

Tone-mapping and knee math are implemented from **published specifications
and reports**, with Palace-owned structure:

| Piece | Public reference | Notes |
|---|---|---|
| PQ EOTF/OETF | Rec. ITU-R BT.2100 / SMPTE ST 2084 | Coefficients only |
| HLG OETF/OOTF | Rec. ITU-R BT.2100 | γ = 1.2, Lw = 1000 |
| HDR EETF | Report ITU-R BT.2390-8 §5.4.1 | `KS = 1.5·maxLum − 0.5`; Hermite P[B]; black lift `b·(1−E₂)⁴` |
| Reference white | Report ITU-R BT.2408 | 203 cd/m² paper white |
| SDR curve | Palace Hermite shoulder | Anchors working 1.0 → display white; knee from scene average (0.5–0.7× ref white) |
| Highlight soft-clip | Palace rational shoulder @ constant Y | `HighlightSoftClip` |

Published numeric constants (203 nits, KS formula, PQ coefficients) are used as
tuning values. **Do not paste LGPL or other copyleft source into this tree.**
