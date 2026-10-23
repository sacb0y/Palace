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

## Regenerate

Floats + Radiance (scRGB-scale `.hdr`):

```bash
python3 scripts/gen-imaging-fixtures.py
```

Containers from the PQ cICP PNG:

```bash
ffmpeg -y -i Palace.Tests/Fixtures/imaging/master.pq.cicp.png -frames:v 1 -c:v libjxl -update 1 \
  Palace.Tests/Fixtures/imaging/master.jxl
ffmpeg -y -i Palace.Tests/Fixtures/imaging/master.pq.cicp.png -frames:v 1 \
  -pix_fmt yuv444p10le -c:v libaom-av1 -still-picture 1 -crf 18 \
  Palace.Tests/Fixtures/imaging/master.avif
```

EXR (metadata-less, scRGB-scale RGB) + JXR + `*.decoded.f32` dumps
(`pip install OpenEXR==3.5.2 imagecodecs`):

```bash
python3 - <<'PY'
import OpenEXR, numpy as np, struct, math, imagecodecs
from pathlib import Path
ROOT = Path('Palace.Tests/Fixtures/imaging')
rgb = np.frombuffer((ROOT/'master.scrgb.f32').read_bytes(), dtype='<f4').reshape(2,2,4)[:,:,:3].copy()
# EXR without chromaticities / whiteLuminance
hdr = {"compression": OpenEXR.NO_COMPRESSION, "type": OpenEXR.scanlineimage,
       "dataWindow": (np.array([0,0],np.int32), np.array([1,1],np.int32)),
       "displayWindow": (np.array([0,0],np.int32), np.array([1,1],np.int32))}
with OpenEXR.File(hdr, {"R": rgb[:,:,0], "G": rgb[:,:,1], "B": rgb[:,:,2]}) as f:
    f.write(str(ROOT/'master.exr'))
with OpenEXR.File(str(ROOT/'master.exr')) as f:
    dec = np.array(f.channels()['RGB'].pixels, dtype=np.float32)
exr = np.concatenate([dec, np.ones((*dec.shape[:2],1), np.float32)], -1).reshape(-1)
(ROOT/'master.exr.decoded.f32').write_bytes(np.asarray(exr, dtype='<f4').tobytes())
(ROOT/'master.jxr').write_bytes(imagecodecs.jpegxr_encode(rgb.astype(np.float32)))
jxr = imagecodecs.jpegxr_decode((ROOT/'master.jxr').read_bytes()).astype(np.float32)
jxr_rgba = np.concatenate([jxr, np.ones((*jxr.shape[:2],1), np.float32)], -1).reshape(-1)
(ROOT/'master.jxr.decoded.f32').write_bytes(np.asarray(jxr_rgba, dtype='<f4').tobytes())
# HDR decoded dump (matches ImagingFixtureIO.LoadHdr)
data = (ROOT/'master.hdr').read_bytes(); text = data.decode('latin1')
he = text.index('\n\n'); res = text[he+2:].split('\n')[0].strip().split()
h, w = int(res[1]), int(res[3]); ps = he+2+len(text[he+2:].split('\n')[0])+1
rgba = []
for i in range(w*h):
    p = ps+i*4; rb,gb,bb,eb = data[p:p+4]
    if eb == 0: rgba += [0,0,0,1.]
    else:
        f = 2.**(eb-128-8); rgba += [rb*f, gb*f, bb*f, 1.]
(ROOT/'master.hdr.decoded.f32').write_bytes(struct.pack(f'<{len(rgba)}f', *rgba))
print('wrote decoded dumps')
PY
```
