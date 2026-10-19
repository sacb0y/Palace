#!/usr/bin/env python3
"""Generate the small OpenEXR test fixtures in Palace.Tests/Fixtures/exr.

pip install OpenEXR==3.5.2 numpy, then run from the repo root:
    python3 tools/openexr/make_exr_fixtures.py

Pixel values follow ExrFixtureValue in Palace.Tests/ExrNativeTests.cs:
  R = 0.02 + 0.2*x, G = 0.05*y, B = 0.25 + 0.5*((x + y) % 3), A = (x + 1) / w
with (x, y) relative to the data window. The data window starts at (5, 3)
so decoders must honor its origin; 37x23 is not a multiple of any chunk or
tile size, so partial chunks and edge tiles are exercised too.
"""
import os
import numpy as np
import OpenEXR

W, H = 37, 23
X0, Y0 = 5, 3
OUT = os.path.join("Palace.Tests", "Fixtures", "exr")


def planes(dtype):
    y, x = np.mgrid[0:H, 0:W].astype(np.float64)
    r = 0.02 + 0.2 * x
    g = 0.05 * y
    b = 0.25 + 0.5 * ((x + y) % 3)
    a = (x + 1) / W
    return [p.astype(dtype) for p in (r, g, b, a)]


def header(compression, storage=OpenEXR.scanlineimage, tiles=None):
    h = {
        "compression": compression,
        "type": storage,
        "dataWindow": (np.array([X0, Y0], np.int32), np.array([X0 + W - 1, Y0 + H - 1], np.int32)),
        "displayWindow": (np.array([0, 0], np.int32), np.array([X0 + W + 4, Y0 + H + 4], np.int32)),
    }
    if tiles is not None:
        h["tiles"] = tiles
    return h


def tiles(size, mode):
    t = OpenEXR.TileDescription()
    t.xSize = size
    t.ySize = size
    t.mode = mode
    return t


def rgba(dtype=np.float16):
    r, g, b, a = planes(dtype)
    return {"R": r, "G": g, "B": b, "A": a}


def write(name, hdr, channels):
    path = os.path.join(OUT, name)
    with OpenEXR.File(hdr, channels) as f:
        f.write(path)
    print(f"{path}: {os.path.getsize(path)} bytes")


def main():
    os.makedirs(OUT, exist_ok=True)
    C = OpenEXR
    for name, comp in [
        ("none", C.NO_COMPRESSION), ("rle", C.RLE_COMPRESSION), ("zips", C.ZIPS_COMPRESSION),
        ("zip", C.ZIP_COMPRESSION), ("piz", C.PIZ_COMPRESSION), ("pxr24", C.PXR24_COMPRESSION),
        ("b44", C.B44_COMPRESSION), ("b44a", C.B44A_COMPRESSION), ("dwaa", C.DWAA_COMPRESSION),
        ("dwab", C.DWAB_COMPRESSION), ("zstd", C.ZSTD_COMPRESSION), ("htj2k256", C.HTJ2K256_COMPRESSION),
    ]:
        write(f"rgba-half-{name}.exr", header(comp), rgba())
    # 32-bit float: PXR24 is lossy (24-bit mantissa cut), PIZ/ZIP lossless.
    for name, comp in [("zip", C.ZIP_COMPRESSION), ("piz", C.PIZ_COMPRESSION), ("pxr24", C.PXR24_COMPRESSION)]:
        write(f"rgba-float-{name}.exr", header(comp), rgba(np.float32))
    # Tiled, single level and mipmapped (decoder must use level 0).
    one = tiles(16, C.ONE_LEVEL)
    mip = tiles(8, C.MIPMAP_LEVELS)
    write("tiled-half-piz.exr", header(C.PIZ_COMPRESSION, C.tiledimage, one), rgba())
    write("tiled-half-dwaa.exr", header(C.DWAA_COMPRESSION, C.tiledimage, one), rgba())
    write("tiled-mip-half-zip.exr", header(C.ZIP_COMPRESSION, C.tiledimage, mip), rgba())
    # Multipart: a depth-only part first, the colour part second.
    r, g, b, a = planes(np.float16)
    z = np.full((H, W), 7.0, np.float32)
    parts = [
        C.Part(header(C.ZIP_COMPRESSION), {"Z": z}, "depth"),
        C.Part(header(C.PIZ_COMPRESSION), {"R": r, "G": g, "B": b, "A": a}, "beauty"),
    ]
    path = os.path.join(OUT, "multipart-depth-then-rgba.exr")
    with OpenEXR.File(parts) as f:
        f.write(path)
    print(f"{path}: {os.path.getsize(path)} bytes")
    # Luminance-only and layered-name files.
    write("luminance-y-zip.exr", header(C.ZIP_COMPRESSION), {"Y": g})
    write("layer-diffuse-zip.exr", header(C.ZIP_COMPRESSION),
          {"diffuse.R": r, "diffuse.G": g, "diffuse.B": b, "specular.R": z.astype(np.float16)})


if __name__ == "__main__":
    main()
