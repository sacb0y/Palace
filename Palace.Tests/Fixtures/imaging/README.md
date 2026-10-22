# Imaging master fixtures (P1)

One 2×2 BT.2020 working-space master (1.0 = 203 nits):

| File | Role |
|------|------|
| `master.bt2020.f32` | Reference working RGBA |
| `master.pq.f32` | PQ-encoded (JXL/AVIF/PNG-cICP decode stand-in) |
| `master.hdr` | Radiance RGBE (absolute nits) |
| `master.scene709.f32` | BT.709 scene-linear (EXR stand-in; whiteLuminance=203) |
| `master.scrgb.f32` | Windows scRGB (JXR stand-in) |

Regenerate with `python3 scripts/gen-imaging-fixtures.py` when tools (`cjxl`, `avifenc`, OpenEXR) are available to replace float stand-ins with real container bytes. Pragmata EXR/.hdr/JXR ratio measurement still pending (files not in CI).
