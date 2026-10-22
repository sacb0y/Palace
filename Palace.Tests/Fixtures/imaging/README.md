# Imaging master fixtures (P1)

One 2×2 BT.2020 working-space master (1.0 = 203 nits):

| File | Role |
|------|------|
| `master.bt2020.f32` | Reference working RGBA |
| `master.pq.f32` | PQ-encoded float (decode accuracy) |
| `master.pq.cicp.png` | Real PNG + cICP (PQ / BT.2020) |
| `master.jxl` | Real JPEG XL from the PNG master |
| `master.avif` | Real AVIF (AV1 still) from the PNG master |
| `master.jxr` | Real JPEG XR (scRGB float encode) |
| `master.exr` | Real OpenEXR (chromaticities + whiteLuminance=203) |
| `master.hdr` | Radiance RGBE (absolute nits) |
| `master.scene709.f32` | BT.709 scene-linear float |
| `master.scrgb.f32` | Windows scRGB float |

Regenerate floats/Radiance: `python3 scripts/gen-imaging-fixtures.py`.
Containers: encode from `master.pq.cicp.png` via `ffmpeg` (`libjxl` / `libaom-av1`)
and `imagecodecs.jpegxr_encode` for JXR; EXR is written by the same script when
tools are present.

**M4:** `ExrRefWhite` stays provisional at 203 until Isiac measures Pragmata
EXR / `.hdr` / JXR ratios on the laptop.
