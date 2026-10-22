#!/usr/bin/env python3
"""Regenerate Palace.Tests/Fixtures/imaging from the BT.2020 working master."""
from __future__ import annotations

import math
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "Palace.Tests" / "Fixtures" / "imaging"

MASTER = [
    0.18, 0.18, 0.18, 1.0,
    1.00, 1.00, 1.00, 1.0,
    4.00, 4.00, 4.00, 1.0,
    0.50, 0.10, 0.05, 1.0,
]
W, H = 2, 2


def pq_oetf(nits: float) -> float:
    y = max(nits, 0.0) / 10000.0
    m1, m2 = 2610 / 16384, 2523 / 32
    c1, c2, c3 = 3424 / 4096, 2413 / 128, 2392 / 128
    ym = y**m1
    return ((c1 + c2 * ym) / (1 + c3 * ym)) ** m2


def bt2020_to_709(r: float, g: float, b: float) -> tuple[float, float, float]:
    return (
        1.6604910 * r - 0.5876411 * g - 0.0728499 * b,
        -0.1245505 * r + 1.1328999 * g - 0.0083494 * b,
        -0.0181508 * r - 0.1005789 * g + 1.1187297 * b,
    )


def float_to_rgbe(r: float, g: float, b: float) -> bytes:
    v = max(r, g, b)
    if v < 1e-32:
        return bytes([0, 0, 0, 0])
    f, e = math.frexp(v)
    scale = f * 256.0 / v
    return bytes(
        [
            min(255, int(r * scale)),
            min(255, int(g * scale)),
            min(255, int(b * scale)),
            e + 128,
        ]
    )


def main() -> None:
    ROOT.mkdir(parents=True, exist_ok=True)
    (ROOT / "master.bt2020.f32").write_bytes(struct.pack(f"<{len(MASTER)}f", *MASTER))

    pq = []
    for i in range(0, len(MASTER), 4):
        for c in range(3):
            pq.append(pq_oetf(MASTER[i + c] * 203))
        pq.append(MASTER[i + 3])
    (ROOT / "master.pq.f32").write_bytes(struct.pack(f"<{len(pq)}f", *pq))

    # Pregmata_SteamAVIF: metadata-less .hdr is scRGB-scale (1.0 = 80 nits),
    # not absolute nits. No EXPOSURE / PRIMARIES lines.
    hdr = bytearray(b"#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n")
    hdr.extend(f"-Y {H} +X {W}\n".encode())
    for i in range(0, len(MASTER), 4):
        r, g, b = bt2020_to_709(MASTER[i], MASTER[i + 1], MASTER[i + 2])
        scale = 203.0 / 80.0
        hdr.extend(float_to_rgbe(r * scale, g * scale, b * scale))
    (ROOT / "master.hdr").write_bytes(hdr)

    scene, scrgb = [], []
    for i in range(0, len(MASTER), 4):
        r, g, b = bt2020_to_709(MASTER[i], MASTER[i + 1], MASTER[i + 2])
        scene.extend([r, g, b, MASTER[i + 3]])
        scrgb.extend([r * 203 / 80, g * 203 / 80, b * 203 / 80, MASTER[i + 3]])
    (ROOT / "master.scene709.f32").write_bytes(struct.pack(f"<{len(scene)}f", *scene))
    (ROOT / "master.scrgb.f32").write_bytes(struct.pack(f"<{len(scrgb)}f", *scrgb))
    print("wrote", ROOT)


if __name__ == "__main__":
    main()
