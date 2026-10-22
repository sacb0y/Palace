# Imaging master fixtures (P1)

One 2×2 BT.2020 working-space master (1.0 = 203 nits):

| File | Role |
|------|------|
| `master.bt2020.f32` | Reference working RGBA |
| `master.pq.f32` | PQ-encoded float (decode accuracy) |
| `master.pq.cicp.png` | Real PNG + cICP (PQ / BT.2020) |
| `master.jxl` | Real JPEG XL from the PNG master |
| `master.avif` | Real AVIF (AV1 still) from the PNG master |
| `master.jxr` | Real JPEG XR (scRGB float) |
| `master.jxr.decoded.f32` | Decoded JXR RGBA (golden) |
| `master.exr` | Real OpenEXR, **no** whiteLuminance/chromaticities (Pregmata-like); scRGB scale |
| `master.exr.decoded.f32` | Decoded EXR RGBA (golden) |
| `master.hdr` | Radiance RGBE, scRGB scale, no EXPOSURE/PRIMARIES |
| `master.hdr.decoded.f32` | Decoded HDR RGBA (golden) |
| `master.scene709.f32` | BT.709 scene-linear float (use with WhiteLuminance=203) |
| `master.scrgb.f32` | Windows scRGB float |

**M4 (Pregmata_SteamAVIF):** metadata-less EXR / `.hdr` **1.0 = 80 nits**
(`ExrRefWhite = ScrgbNits`). EXR ≡ JXR (ratio 1.0000); .hdr/JXR ≈ 0.9968 (RGBE).

Regenerate floats/Radiance: `python3 scripts/gen-imaging-fixtures.py`.
Containers / decoded dumps: OpenEXR + `imagecodecs` + `ffmpeg` (see agent notes).
