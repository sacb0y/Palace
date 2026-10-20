#!/usr/bin/env python3
"""Palace built-in background presets: deterministic procedural renders.

Original work for Palace. Every pixel comes from the math below (numpy + Pillow only):
no stock photos, no traced references, no AI images. Re-running with the same seed
gives the same bytes, so the assets can be rebuilt in CI.

Usage:
  python generate_backgrounds.py --out ./proposed            # all presets, 2560x1440 + thumbs
  python generate_backgrounds.py --only aero ribbons --scale 0.4   # quick preview
"""
from __future__ import annotations

import argparse
import math
import time
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

F32 = np.float32


# --------------------------------------------------------------------------- helpers
def hx(s: str) -> np.ndarray:
    s = s.lstrip("#")
    return np.array([int(s[i:i + 2], 16) / 255 for i in (0, 2, 4)], F32)


def mix(a, b, t):
    return a + (b - a) * t


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def ramp(t, stops):
    """Vectorised colour ramp. stops = [(pos, '#hex'), ...]; t any shape -> t.shape+(3,)."""
    pos = np.array([p for p, _ in stops], F32)
    cols = np.stack([hx(c) for _, c in stops])
    t = np.asarray(t, F32)
    return np.stack([np.interp(t, pos, cols[:, k]).astype(F32) for k in range(3)], -1)


def resize_f(a: np.ndarray, w: int, h: int, flt=Image.BICUBIC) -> np.ndarray:
    return np.asarray(Image.fromarray(a.astype(F32), "F").resize((w, h), flt), F32)


def _box(a, r, axis):
    if r < 1:
        return a
    pad = [(0, 0)] * a.ndim
    pad[axis] = (r + 1, r)
    c = np.cumsum(np.pad(a, pad, mode="edge"), axis=axis, dtype=np.float64)
    n = a.shape[axis]
    hi = np.take(c, np.arange(2 * r + 1, 2 * r + 1 + n), axis=axis)
    lo = np.take(c, np.arange(0, n), axis=axis)
    return ((hi - lo) / (2 * r + 1)).astype(F32)


def _small_gauss(a, sigma):
    r = max(1, int(math.ceil(sigma * 3)))
    k = np.exp(-0.5 * (np.arange(-r, r + 1) / sigma) ** 2).astype(F32)
    k /= k.sum()
    for axis in (0, 1):
        p = np.pad(a, [(r, r) if i == axis else (0, 0) for i in range(2)], mode="edge")
        out = np.zeros_like(a)
        for i, kv in enumerate(k):
            out += kv * (p[i:i + a.shape[0]] if axis == 0 else p[:, i:i + a.shape[1]])
        a = out
    return a


def blur(a: np.ndarray, sigma: float) -> np.ndarray:
    """Approximate Gaussian blur (downsample + 3x box + upsample). 2D or HxWxC."""
    if a.ndim == 3:
        return np.stack([blur(a[..., c], sigma) for c in range(a.shape[2])], -1)
    if sigma < 0.4:
        return a
    if sigma < 3:
        return _small_gauss(a, sigma)
    h, w = a.shape
    f = max(1, int(sigma // 3))
    s = sigma / f
    small = resize_f(a, max(2, w // f), max(2, h // f), Image.BOX) if f > 1 else a
    r = max(1, int(round((math.sqrt(4 * s * s + 1) - 1) / 2)))
    for _ in range(3):
        small = _box(_box(small, r, 0), r, 1)
    return resize_f(small, w, h, Image.BICUBIC) if f > 1 else small


def fbm(h, w, seed, base=3.0, octaves=6, pers=0.5, sx=1.0, sy=1.0):
    """Fractal value noise in [0,1] (bicubic-upsampled random lattices)."""
    rng = np.random.default_rng(seed)
    tot = np.zeros((h, w), F32)
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        gx = int(base * 2 ** o * sx) + 3
        gy = int(base * 2 ** o * sy * h / w) + 3
        g = rng.random((gy, gx)).astype(F32)
        # oversize then crop a random window so lattice edges never line up
        up = resize_f(g, int(w * gx / (gx - 2)), int(h * gy / (gy - 2)))
        ox = rng.integers(0, up.shape[1] - w + 1)
        oy = rng.integers(0, up.shape[0] - h + 1)
        tot += amp * up[oy:oy + h, ox:ox + w]
        norm += amp
        amp *= pers
    tot /= norm
    lo, hi = np.percentile(tot, [0.5, 99.5])
    return np.clip((tot - lo) / (hi - lo), 0, 1)


def noise1d(n, seed, base=8, octaves=5, pers=0.55):
    rng = np.random.default_rng(seed)
    x = np.linspace(0, 1, n, dtype=F32)
    tot = np.zeros(n, F32)
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        k = int(base * 2 ** o) + 2
        g = rng.random(k).astype(F32)
        xs = np.linspace(0, 1, k)
        tot += amp * np.interp(x, xs, g).astype(F32)  # linear
        norm += amp
        amp *= pers
    tot /= norm
    return (tot - tot.min()) / (np.ptp(tot) + 1e-6)


def sample(img, xs, ys):
    """Bilinear sample img (HxW or HxWxC) at float pixel coords."""
    h, w = img.shape[:2]
    xs = np.clip(xs, 0, w - 1.001)
    ys = np.clip(ys, 0, h - 1.001)
    x0 = xs.astype(np.int32)
    y0 = ys.astype(np.int32)
    fx = (xs - x0)[..., None] if img.ndim == 3 else xs - x0
    fy = (ys - y0)[..., None] if img.ndim == 3 else ys - y0
    a = img[y0, x0]; b = img[y0, x0 + 1]; c = img[y0 + 1, x0]; d = img[y0 + 1, x0 + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def shift(a, dy, dx=0):
    """Translate a 2D array, replicating edges (np.roll would wrap shadows around the frame)."""
    h, w = a.shape
    p = np.pad(a, ((abs(dy), abs(dy)), (abs(dx), abs(dx))), mode="edge")
    y0 = abs(dy) - dy
    x0 = abs(dx) - dx
    return p[y0:y0 + h, x0:x0 + w]


def screen(dst, col, a):
    a = a[..., None] if np.ndim(a) == 2 else a
    return 1 - (1 - dst) * (1 - col * a)


def over(dst, col, a):
    a = a[..., None] if np.ndim(a) == 2 else a
    return dst * (1 - a) + col * a


def radial(X, Y, cx, cy, r):
    return np.exp(-((X - cx) ** 2 + (Y - cy) ** 2) / (2 * r * r)).astype(F32)


def bbox(cx, cy, r, w, h):
    x0, x1 = max(0, int(cx - r)), min(w, int(cx + r) + 2)
    y0, y1 = max(0, int(cy - r)), min(h, int(cy + r) + 2)
    return y0, y1, x0, x1


class Canvas:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.S = h / 1440.0  # pixel scale: all sizes authored at 1440p
        y, x = np.mgrid[0:h, 0:w].astype(F32)
        self.X, self.Y = x, y
        self.u, self.v = x / w, y / h


def stars(cv: Canvas, seed, n=2600, bright=1.0, glints=6, tint="#CFE8FF"):
    """Returns HxWx3 additive star layer: dust, small stars, and glinting stars with spikes."""
    rng = np.random.default_rng(seed)
    h, w, S = cv.h, cv.w, cv.S
    layer = np.zeros((h, w), F32)
    for count, sig, pw in ((int(n * cv.w * cv.h / (2560 * 1440)), 0.6, 3.2), (n // 8, 1.2, 2.2)):
        xi = rng.integers(0, w, count); yi = rng.integers(0, h, count)
        b = (rng.random(count) ** pw).astype(F32)
        acc = np.zeros((h, w), F32)
        np.add.at(acc, (yi, xi), b)
        layer += _small_gauss(acc, max(0.45, sig * S)) * (6.0 * sig * sig * S * S)
    col = np.clip(layer, 0, 1.5)[..., None] * hx(tint)
    X, Y = cv.X, cv.Y
    for _ in range(glints):
        cx, cy = rng.uniform(0.05, 0.95) * w, rng.uniform(0.05, 0.95) * h
        L = rng.uniform(18, 46) * S
        y0, y1, x0, x1 = bbox(cx, cy, L * 4, w, h)
        dx = X[y0:y1, x0:x1] - cx; dy = Y[y0:y1, x0:x1] - cy
        th = 0.9 * S
        spike = (np.exp(-np.abs(dx) / L) * np.exp(-(dy / th) ** 2)
                 + np.exp(-np.abs(dy) / L) * np.exp(-(dx / th) ** 2))
        dd = (dx + dy) / 1.414; da = (dx - dy) / 1.414
        spike += 0.35 * (np.exp(-np.abs(dd) / (L * .4)) * np.exp(-(da / th) ** 2)
                         + np.exp(-np.abs(da) / (L * .4)) * np.exp(-(dd / th) ** 2))
        core = np.exp(-(dx * dx + dy * dy) / (2 * (1.6 * S) ** 2))
        halo = 0.25 * np.exp(-(dx * dx + dy * dy) / (2 * (L * 0.35) ** 2))
        c = mix(hx(tint), np.ones(3, F32), 0.6)
        col[y0:y1, x0:x1] += (spike + core * 1.2 + halo)[..., None] * c
    return col * bright


# --------------------------------------------------------------------------- presets
def p_ribbons(cv: Canvas, dark: bool):
    """Glossy glass ribbons: twisting bands with specular highlights, edge lines, soft shadows."""
    w, h, S, X, Y, u, v = cv.w, cv.h, cv.S, cv.X, cv.Y, cv.u, cv.v
    rng = np.random.default_rng(101)
    if dark:
        img = ramp(v * 0.7 + u * 0.3, [(0, "#090420"), (0.5, "#12062E"), (1, "#05030F")])
        img += radial(u, v, 0.62, 0.10, 0.28)[..., None] * hx("#4A12A8") * 0.9
        img += radial(u, v, 0.05, 0.95, 0.30)[..., None] * hx("#0B2A8A") * 0.6
        pal = ["#13E8FF", "#7A2BFF", "#1FF0B0", "#2F45FF", "#C03BFF", "#17C9FF", "#3A5BFF", "#26E58A",
               "#8A3CFF", "#25D9E8"]
        shadow_k, glass = 0.55, 0.82
    else:
        img = ramp(v * 0.6 + u * 0.4, [(0, "#F3F0FF"), (0.55, "#E6F3FF"), (1, "#F7EAFB")])
        img += radial(u, v, 0.62, 0.12, 0.30)[..., None] * (hx("#B9A4FF") - 0.85) * 0.6
        pal = ["#1ED2F2", "#8B5CFF", "#33DFA6", "#5468FF", "#D06BFA", "#2BC4FF", "#6A7DFF", "#3ED98C",
               "#A877FF", "#3FE0D8"]
        shadow_k, glass = 0.22, 0.80
    white = np.ones(3, F32)
    ux = (np.arange(w, dtype=F32) / w)
    N = 8
    curves = []
    for i in range(N):
        t = i / (N - 1)
        bundle = i % 2  # two interleaved bundles that cross each other
        ph = (0.4 if bundle == 0 else 2.3) + 0.35 * t + rng.uniform(-0.08, 0.08)
        amp = (0.24 if bundle == 0 else 0.17) - 0.03 * t
        base = (0.30 if bundle == 0 else 0.62) + 0.16 * t
        fq = 0.70 + 0.12 * t
        y0 = h * (base + amp * np.sin(2 * np.pi * fq * ux + ph) + 0.06 * np.sin(2 * np.pi * 1.6 * ux + 1.7 * ph))
        tw = np.cos(np.pi * (ux * rng.uniform(0.6, 1.1) + t * 0.8 + rng.uniform(0, 1)))
        hw = h * rng.uniform(0.045, 0.10) * (0.22 + 0.78 * np.abs(tw)) + 2 * S
        curves.append((y0, tw, hw, i))
    for y0, tw, hw, i in curves:
        slope = np.gradient(y0)
        cosv = (1 / np.sqrt(1 + slope ** 2)).astype(F32)
        s = (Y - y0[None]) * cosv[None] / hw[None]
        edge = (1 - np.abs(s)) * hw[None]
        alpha = np.clip(edge + 0.5, 0, 1)
        if not alpha.any():
            continue
        # shadow onto what is already painted
        sh = blur(alpha, 16 * S)
        sh = shift(sh, int(16 * S))
        img *= (1 - shadow_k * sh)[..., None]
        c0, c1, c2 = hx(pal[i]), hx(pal[(i + 3) % len(pal)]), hx(pal[(i + 6) % len(pal)])
        cu = np.where(ux[:, None] < 0.5, mix(c0, c1, (ux[:, None] / 0.5)), mix(c1, c2, (ux[:, None] - 0.5) / 0.5))
        col = np.broadcast_to(cu[None], (h, w, 3))
        face = (0.78 + 0.22 * np.tanh(tw * 5))[None, :, None]  # back face of the twist a bit darker
        diffuse = (0.50 + 0.50 * np.sqrt(np.clip(1 - s * s, 0, 1)))[..., None]
        spec = np.exp(-((s + 0.38 * np.tanh(tw * 4)[None]) / 0.12) ** 2) * (0.25 + 0.75 * np.abs(tw)[None])
        spec2 = 0.35 * np.exp(-((s - 0.62 * np.tanh(tw * 4)[None]) / 0.06) ** 2)
        body = col * diffuse * face
        body = mix(body, white, (np.clip(spec + spec2, 0, 1) * 0.85)[..., None])
        a = alpha * (glass + (1 - glass) * np.clip(spec, 0, 1))
        img = over(img, body, a)
        top = np.exp(-((edge - 1.2 * S) / (1.0 * S)) ** 2) * (s < 0)
        bot = np.exp(-((edge - 1.0 * S) / (0.9 * S)) ** 2) * (s > 0)
        img = screen(img, mix(col, white, 0.55), np.clip(top * 0.85, 0, 1))
        img *= (1 - 0.35 * bot)[..., None]
    # neon hairlines / light streaks riding the flow
    for k in range(3):
        t = 0.2 + 0.3 * k
        y0 = h * (0.30 + 0.45 * t + (0.19 - 0.05 * t) * np.sin(2 * np.pi * (0.82 + 0.18 * t) * ux + 0.5 + t)
                  + 0.05 * np.sin(2 * np.pi * 1.85 * ux + 1.7 * (0.5 + t))) + 0.035 * h
        d = np.abs(Y - y0[None])
        fade = smoothstep(0.0, 0.35, ux) * smoothstep(1.0, 0.6, ux)
        ln = (np.exp(-(d / (1.1 * S)) ** 2) + 0.35 * np.exp(-(d / (9 * S)) ** 2)) * fade[None]
        img = screen(img, hx(pal[(k * 4) % len(pal)]) * 0.5 + 0.5, np.clip(ln * 0.7, 0, 1))
    return img


def p_capsules(cv: Canvas, dark: bool):
    """Glass capsules at 45 deg with real drop shadows, gloss bands and rim light."""
    w, h, S, X, Y, u, v = cv.w, cv.h, cv.S, cv.X, cv.Y, cv.u, cv.v
    rng = np.random.default_rng(202)
    t = np.clip(u * 0.62 + v * 0.38, 0, 1)
    if dark:
        img = ramp(t, [(0, "#06245A"), (0.35, "#14308F"), (0.7, "#3A1796"), (1, "#5A0C86")])
        img += radial(u, v, 0.05, 0.05, 0.35)[..., None] * hx("#0B7FB8") * 0.7
        pills = [("#25E4FF", "#2F7BFF"), ("#B23CFF", "#FF4FC8"), ("#3D6BFF", "#8A4DFF"),
                 ("#1FF2C8", "#25A8FF"), ("#FF5FA2", "#B03CFF")]
        sk, shcol = 0.55, hx("#05031A")
    else:
        img = ramp(t, [(0, "#62F5FF"), (0.35, "#56AEFF"), (0.7, "#7A66FF"), (1, "#A43CE8")])
        img += radial(u, v, 0.0, 0.0, 0.30)[..., None] * 0.18
        pills = [("#7DF6FF", "#47C8FF"), ("#E36BFF", "#FF7FCB"), ("#8FB4FF", "#9C7BFF"),
                 ("#9BFFE8", "#5FD3FF"), ("#FF8FC0", "#C16BFF")]
        sk, shcol = 0.38, hx("#2A1670")
    white = np.ones(3, F32)
    dx, dy = math.cos(math.radians(45)), -math.sin(math.radians(45))  # up-right
    nx, ny = -dy, dx  # perpendicular pointing down-right
    specs = []
    for i in range(14):
        depth = rng.uniform(0, 1)
        r = (40 + 110 * (1 - depth) * rng.uniform(0.5, 1.0)) * S
        L = rng.uniform(2.5, 7.5) * r
        specs.append((depth, rng.uniform(-0.05, 1.05) * w, rng.uniform(-0.05, 1.05) * h, r, L, i))
    specs.sort(key=lambda s: -s[0])  # far (depth 1) first
    for depth, cx, cy, r, L, i in specs:
        ax, ay = cx - dx * L / 2, cy - dy * L / 2
        ext = L / 2 + r * 2.2
        y0, y1, x0, x1 = bbox(cx, cy, ext, w, h)
        if y1 <= y0 or x1 <= x0:
            continue
        px = X[y0:y1, x0:x1] - ax; py = Y[y0:y1, x0:x1] - ay
        tt = np.clip((px * dx + py * dy) / L, 0, 1)
        qx = px - tt * L * dx; qy = py - tt * L * dy
        d = np.sqrt(qx * qx + qy * qy) - r
        q = np.clip((px * nx + py * ny) / r, -1.2, 1.2)
        alpha = np.clip(0.5 - d, 0, 1)
        far = depth > 0.62
        # drop shadow
        sig = r * (0.22 + 0.25 * depth)
        sh = blur(alpha, sig)
        off = int(r * 0.30)
        sh = shift(sh, off, int(off * 0.6))
        k = sk * (0.55 if far else 1.0)
        reg = img[y0:y1, x0:x1]
        reg = mix(reg, reg * shcol * 2.5, (sh * k)[..., None])
        c1, c2 = hx(pills[i % len(pills)][0]), hx(pills[i % len(pills)][1])
        body = mix(c1, c2, tt[..., None]) * (1 - 0.22 * (q[..., None] + 1) / 2)
        gloss = np.exp(-((q + 0.52) / 0.18) ** 2) * smoothstep(0.0, 0.25, tt) * smoothstep(1.0, 0.7, tt)
        rim = np.exp(-((d + 1.8 * S) / max(0.9, 1.2 * S)) ** 2) * (0.35 + 0.65 * np.clip(0.5 - 0.5 * q, 0, 1))
        a_body = (0.30 if far else 0.80) * alpha
        reg = over(reg, body, a_body)
        if not far:
            reg = mix(reg, white, (gloss * 0.38 * alpha)[..., None])
            # inner bottom glow (light passing through the glass)
            glow = np.exp(-((q - 0.75) / 0.25) ** 2) * alpha
            reg = screen(reg, mix(c2, white, 0.3), glow * 0.25)
        reg = screen(reg, white * 0.9, np.clip(rim * (0.35 if far else 0.85), 0, 1))
        img[y0:y1, x0:x1] = reg
    # a few small glass dots / rings for rhythm
    for i in range(9):
        cx, cy = rng.uniform(0, w), rng.uniform(0, h)
        r = rng.uniform(8, 26) * S
        y0, y1, x0, x1 = bbox(cx, cy, r + 4, w, h)
        d = np.hypot(X[y0:y1, x0:x1] - cx, Y[y0:y1, x0:x1] - cy) - r
        if i % 2:
            ring = np.clip(1.6 * S - np.abs(d), 0, 1)
            img[y0:y1, x0:x1] = screen(img[y0:y1, x0:x1], white, ring * 0.6)
        else:
            img[y0:y1, x0:x1] = over(img[y0:y1, x0:x1], white, np.clip(0.5 - d, 0, 1) * 0.35)
    return img


def p_bokeh(cv: Canvas, dark: bool):
    """Out-of-focus lights: depth layers of discs with bright rims over a warped colour field."""
    w, h, S, X, Y, u, v = cv.w, cv.h, cv.S, cv.X, cv.Y, cv.u, cv.v
    rng = np.random.default_rng(303)
    n1 = fbm(h, w, 31, base=1.5, octaves=4)
    n2 = fbm(h, w, 32, base=1.5, octaves=4)
    wu = u + (n1 - 0.5) * 0.25
    wv = v + (n2 - 0.5) * 0.25
    blobs = [(0.18, 0.22, 0.20, "#FF6A1F"), (0.48, 0.18, 0.18, "#F0306E"), (0.80, 0.30, 0.16, "#7A2AD8"),
             (0.30, 0.70, 0.20, "#2E3BEA"), (0.70, 0.82, 0.18, "#1E6BFF"), (0.08, 0.60, 0.12, "#FFA13A"),
             (0.92, 0.70, 0.14, "#C02CC8")]
    base_col = hx("#0A0620") if dark else hx("#FFF4F8")
    num = np.zeros((h, w, 3), F32) + base_col * 0.55
    den = np.zeros((h, w), F32) + 0.55
    for bx, by, br, c in blobs:
        g = radial(wu * (w / h), wv, bx * (w / h), by, br * 1.15)
        num += g[..., None] * hx(c); den += g
    img = num / den[..., None]
    if dark:
        img *= (0.55 + 0.45 * smoothstep(1.1, 0.2, np.hypot(u - 0.45, v - 0.5)))[..., None]
    else:
        img = mix(img, np.ones(3, F32), 0.42)
    field = blur(img, 40 * S)
    white = np.ones(3, F32)
    layers = [  # count, rmin, rmax, soft, fill, rim
        (14, 140, 300, (14, 32), (0.08, 0.16), 0.06),
        (90, 34, 120, (2.0, 6.0), (0.18, 0.38), 0.16),
        (200, 8, 36, (0.9, 2.2), (0.22, 0.50), 0.22),
    ]
    for count, rmin, rmax, soft, fill, rim in layers:
        for _ in range(count):
            cx, cy = rng.uniform(-0.03, 1.03) * w, rng.uniform(-0.03, 1.03) * h
            r = math.exp(rng.uniform(math.log(rmin), math.log(rmax))) * S
            sf = rng.uniform(*soft) * S
            y0, y1, x0, x1 = bbox(cx, cy, r + 3 * sf + 2, w, h)
            if y1 <= y0 or x1 <= x0:
                continue
            ddx = X[y0:y1, x0:x1] - cx; ddy = Y[y0:y1, x0:x1] - cy
            d = np.sqrt(ddx * ddx + ddy * ddy) - r
            bodyv = np.clip(0.5 - d / sf, 0, 1)
            rimv = np.exp(-((d + sf * 1.4 + 1.2 * S) / (sf * 0.9 + 1.0 * S)) ** 2) * bodyv
            shade = 1 + 0.25 * (-ddx - ddy) / (r * 1.414)  # brighter toward top-left
            c = field[int(np.clip(cy, 0, h - 1)), int(np.clip(cx, 0, w - 1))]
            c = np.clip(c * 1.5 + 0.12, 0, 1)
            c = mix(c, white, 0.25 + rng.uniform(0, 0.25))
            a = np.clip(bodyv * rng.uniform(*fill) * shade + rimv * rim, 0, 1)
            if dark:
                img[y0:y1, x0:x1] = screen(img[y0:y1, x0:x1], c, a)
            else:
                cc = mix(c, white, 0.35)
                img[y0:y1, x0:x1] = over(img[y0:y1, x0:x1], cc, a * 1.2)
                img[y0:y1, x0:x1] = mix(img[y0:y1, x0:x1], c * 0.85, (rimv * 0.12)[..., None])
    return img


def p_nebula(cv: Canvas, dark: bool):
    """Fractal-noise cloud band (domain warped) over a star field with a few glinting stars."""
    w, h, S, X, Y, u, v = cv.w, cv.h, cv.S, cv.X, cv.Y, cv.u, cv.v
    wx = fbm(h, w, 41, base=2, octaves=5)
    wy = fbm(h, w, 42, base=2, octaves=5)
    big = fbm(h, w, 43, base=3, octaves=7, pers=0.55)
    dens = sample(big, cv.X + (wx - 0.5) * 260 * S, cv.Y + (wy - 0.5) * 260 * S)
    fil = fbm(h, w, 44, base=6, octaves=6, pers=0.6, sy=0.6)
    yb = 0.46 + 0.10 * np.sin(2 * np.pi * 0.62 * u + 1.1) + 0.035 * np.sin(2 * np.pi * 1.7 * u + 0.3)
    yb = yb + (wx - 0.5) * 0.10
    dist = (v - yb)  # >0 below the band
    lower = smoothstep(0.03, -0.01, dist + (dens - 0.5) * 0.12 + (fil - 0.5) * 0.05)   # crisp, ragged lit underside
    upper = np.exp(-np.clip(-dist, 0, None) / 0.20)                 # clouds thin out upward
    band = np.where(dist > 0, lower, upper)
    cloud = np.clip(dens * 2.4 - 0.5, 0, 1) * band
    cloud = np.clip(cloud + 0.45 * np.exp(-np.clip(-dist, 0, None) / 0.07) * lower * (0.5 + dens), 0, 1)
    cloud = np.clip(cloud + 0.35 * np.clip(fil - 0.55, 0, 1) * band, 0, 1)
    edge = np.exp(-((dist - 0.004) / 0.012) ** 2) * np.clip(dens * 1.6 - 0.4, 0, 1)
    haze = smoothstep(-0.02, 0.35, -dist) * (0.6 + 0.4 * fbm(h, w, 45, base=1.5, octaves=4))
    if dark:
        img = ramp(v, [(0, "#2A0F3E"), (0.45, "#0E1B3A"), (0.7, "#062A30"), (1, "#03131C")])
        img += (fbm(h, w, 46, base=2, octaves=5) * smoothstep(0.4, 1.0, v))[..., None] * hx("#0A4A48") * 0.45
        warm = ramp(np.clip(-dist / 0.5, 0, 1), [(0, "#FF4FA3"), (0.35, "#FF7A3D"), (0.75, "#FFB347"), (1, "#FFD27A")])
        img = mix(img, warm, (haze * 0.85)[..., None])
        st = stars(cv, 47, n=3200, bright=1.0, glints=7)
        img += st * (1 - 0.8 * cloud)[..., None]
        ccol = ramp(cloud, [(0, "#5A1890"), (0.35, "#B42CC8"), (0.7, "#F25ADC"), (1, "#FF9CEB")])
        img = mix(img, ccol, np.clip(cloud * 1.6, 0, 0.92)[..., None])
        img = screen(img, hx("#FFC4F0"), np.clip(edge * 0.5, 0, 1))
        dust = np.clip(fil - 0.62, 0, 1) * 2.0 * band * (dist < 0)
        img *= (1 - 0.45 * dust)[..., None]
    else:
        img = ramp(v, [(0, "#FFE0B8"), (0.4, "#FFD0E2"), (0.7, "#E4E2FF"), (1, "#D2F0F6")])
        warm = ramp(np.clip(-dist / 0.5, 0, 1), [(0, "#FF8CC6"), (0.4, "#FFB07A"), (1, "#FFE3A0")])
        img = mix(img, warm, (haze * 0.55)[..., None])
        img += stars(cv, 47, n=600, bright=0.25, glints=4, tint="#FFFFFF")
        ccol = ramp(cloud, [(0, "#F4A6E6"), (0.5, "#E25ACB"), (1, "#FFF0FA")])
        img = mix(img, ccol, np.clip(cloud * 1.5, 0, 1)[..., None])
        img = screen(img, np.ones(3, F32), np.clip(edge * 0.8, 0, 1))
        img = mix(img, hx("#9A7BD8"), (np.clip(fil - 0.62, 0, 1) * 1.2 * band * (dist < 0))[..., None])
    return img


def p_aero(cv: Canvas, dark: bool):
    """Retro glass (Aero era): vivid sky, light rays, glossy green swooshes, refracting glass orbs."""
    w, h, S, X, Y, u, v = cv.w, cv.h, cv.S, cv.X, cv.Y, cv.u, cv.v
    rng = np.random.default_rng(505)
    white = np.ones(3, F32)
    if dark:
        img = ramp(v, [(0, "#020B2A"), (0.45, "#06306A"), (0.75, "#0A6A8C"), (1, "#0FA3A8")])
        sun, rays_k = hx("#7FF2FF"), 0.22
        hills = [("#0B6B3A", "#043A22", "#7CFF6B"), ("#0F8A44", "#06502C", "#B6FF5A")]
    else:
        img = ramp(v, [(0, "#0078D8"), (0.35, "#1FA8F0"), (0.68, "#7FDCFF"), (1, "#E2FBFF")])
        sun, rays_k = white, 0.38
        hills = [("#4CC94A", "#1E8E34", "#E6FFB0"), ("#7EE23C", "#2FA83A", "#F4FFD0")]
    sx, sy = 0.20 * w, -0.12 * h
    ang = np.arctan2(Y - sy, X - sx)
    rn = noise1d(4096, 51, base=24, octaves=3)
    ri = np.interp((ang + np.pi) / (2 * np.pi) * 4095, np.arange(4096), rn).astype(F32)
    dist = np.hypot(X - sx, Y - sy) / h
    rays = smoothstep(0.45, 0.85, ri) * np.exp(-dist / 1.0)
    img = screen(img, sun, np.clip(rays * rays_k + radial(u * w / h, v, 0.2 * w / h, -0.12, 0.35) * 0.55, 0, 1))
    # clouds (soft, but with shaped tops)
    cl = fbm(h, w, 52, base=2.5, octaves=6)
    cmask = smoothstep(0.58, 0.78, cl) * smoothstep(0.75, 0.35, v)
    img = mix(img, white if not dark else hx("#3A8FC8"), (cmask * (0.55 if not dark else 0.35))[..., None])
    ux = np.arange(w, dtype=F32) / w
    for k, (top, bot, edgec) in enumerate(hills):
        yh = h * (0.72 + 0.10 * k + 0.07 * np.sin(2 * np.pi * (0.55 + 0.25 * k) * ux + 2.2 + 1.7 * k)
                  + 0.025 * np.sin(2 * np.pi * 2.1 * ux + k))
        dd = Y - yh[None]
        a = np.clip(dd + 0.5, 0, 1)
        sh = shift(blur(a, 14 * S), -int(6 * S))
        img *= (1 - 0.25 * sh * (1 - a))[..., None]
        grad = mix(hx(top), hx(bot), np.clip(dd / (0.25 * h), 0, 1)[..., None])
        gloss = np.exp(-np.clip(dd, 0, None) / (26 * S)) * 0.55
        grad = mix(grad, mix(hx(edgec), white, 0.3), gloss[..., None])
        img = over(img, grad, a)
        line = np.exp(-((dd - 1.5 * S) / (1.3 * S)) ** 2)
        img = screen(img, mix(hx(edgec), white, 0.5), np.clip(line, 0, 1) * 0.9)
    # glossy light swoosh across the hills
    yS = h * (0.70 + 0.08 * np.sin(2 * np.pi * 0.7 * ux + 2.6))
    dd = (Y - yS[None]) / (14 * S)
    sw = np.exp(-dd * dd) * (smoothstep(0.05, 0.4, ux) * smoothstep(1.0, 0.7, ux))[None]
    img = screen(img, white, np.clip(sw * 0.75, 0, 1))
    # lens streaks
    for _ in range(4):
        cx, cy = rng.uniform(0.3, 0.95) * w, rng.uniform(0.1, 0.6) * h
        th = math.radians(rng.uniform(-35, -20))
        L = rng.uniform(0.15, 0.35) * w
        px = (X - cx) * math.cos(th) + (Y - cy) * math.sin(th)
        py = -(X - cx) * math.sin(th) + (Y - cy) * math.cos(th)
        st = np.exp(-(py / (1.4 * S)) ** 2) * np.exp(-(px / L) ** 2)
        img = screen(img, white, np.clip(st * 0.6, 0, 1))
    # glass orbs / bubbles
    base = img.copy()
    orbs = []
    for i in range(18):
        r = math.exp(rng.uniform(math.log(22), math.log(240))) * S
        orbs.append((r, rng.uniform(0.02, 0.98) * w, rng.uniform(0.05, 0.85) * h))
    orbs.sort()
    tint = hx("#9FF6FF") if not dark else hx("#3FE6FF")
    for r, cx, cy in orbs:
        y0, y1, x0, x1 = bbox(cx, cy, r + 4 * S, w, h)
        ddx = X[y0:y1, x0:x1] - cx; ddy = Y[y0:y1, x0:x1] - cy
        rr = np.sqrt(ddx * ddx + ddy * ddy)
        rn_ = np.clip(rr / r, 0, 1)
        alpha = np.clip(r - rr + 0.5, 0, 1)
        # refraction: inverted, magnified view of the scene behind
        k = 0.55 + 0.35 * rn_ ** 2
        refr = sample(base, cx - ddx * k, cy - ddy * k)
        refr = mix(refr, tint, 0.12)
        fres = rn_ ** 3.2
        body = mix(refr, mix(tint, white, 0.6), (fres * 0.55)[..., None])
        # specular highlight: soft ellipse upper-left, fading downward
        ex, ey = (ddx + 0.30 * r), (ddy + 0.42 * r)
        cth, sth = math.cos(math.radians(-28)), math.sin(math.radians(-28))
        ax_ = (ex * cth - ey * sth) / (0.55 * r); ay_ = (ex * sth + ey * cth) / (0.30 * r)
        e = ax_ * ax_ + ay_ * ay_
        hl = np.clip(1 - e, 0, 1) ** 1.2 * np.clip(0.4 - ay_ * 0.6 + 0.6, 0, 1)
        dot = np.exp(-((ddx - 0.48 * r) ** 2 + (ddy - 0.46 * r) ** 2) / (2 * (0.07 * r) ** 2))
        cres = np.exp(-((rn_ - 0.86) / 0.05) ** 2) * np.clip((ddx + ddy) / (1.414 * r), 0, 1)
        body = mix(body, white, np.clip(hl * 0.9 + dot * 0.9 + cres * 0.45, 0, 1)[..., None])
        rim = np.clip(1.6 * S - np.abs(rr - r + 1.2 * S), 0, 1)
        reg = img[y0:y1, x0:x1]
        reg = over(reg, body, alpha)
        reg = screen(reg, white, rim * 0.75)
        img[y0:y1, x0:x1] = reg
    return img


def p_petal(cv: Canvas, dark: bool):
    """Momoirosoft pink (#E05A8C): layered paper-cut waves, edge highlights, shadows, halftone."""
    w, h, S, X, Y, u, v = cv.w, cv.h, cv.S, cv.X, cv.Y, cv.u, cv.v
    white = np.ones(3, F32)
    if dark:
        img = ramp(v * 0.6 + u * 0.4, [(0, "#2A0B22"), (0.6, "#1A0718"), (1, "#12051A")])
        img += radial(u, v, 0.8, 0.15, 0.3)[..., None] * hx("#6A1A4A") * 0.6
        layers = ["#5A1648", "#8A2160", "#B8336F", "#E05A8C", "#F27FA8", "#FFB3CB"]
        dots, sk = hx("#E05A8C"), 0.55
    else:
        img = ramp(v * 0.6 + u * 0.4, [(0, "#FFF2F7"), (1, "#FFE1EC")])
        layers = ["#FFD3E2", "#FFB6CE", "#F78DB0", "#E05A8C", "#FF8FA0", "#FFE4EC"]
        dots, sk = hx("#E05A8C"), 0.30
    paper = fbm(h, w, 61, base=120 * cv.w / 2560, octaves=3, pers=0.6)
    ux = np.arange(w, dtype=F32) / w
    # halftone dots, upper-left, fading out
    cell = 26 * S
    gx = (X % cell) - cell / 2; gy = (Y % cell) - cell / 2
    cxg = X - gx; cyg = Y - gy
    fall = np.clip(1 - np.hypot(cxg / w - 0.05, cyg / h - 0.08) / 0.55, 0, 1)
    rad = fall * cell * 0.36
    dmask = np.clip(rad - np.hypot(gx, gy) + 0.5, 0, 1)
    img = over(img, dots, dmask * (0.45 if dark else 0.35))
    for k, c in enumerate(layers):
        t = k / (len(layers) - 1)
        yk = h * (0.30 + 0.12 * k + 0.07 * np.sin(2 * np.pi * (0.7 + 0.15 * k) * ux + 1.3 * k + 0.4)
                  + 0.03 * np.sin(2 * np.pi * (1.9 + 0.2 * k) * ux + 2.1 * k)) - h * 0.06 * ux
        dd = Y - yk[None]
        a = np.clip(dd + 0.5, 0, 1)
        sh = blur(a, 22 * S)
        sh = shift(sh, -int(8 * S))
        img *= (1 - sk * sh * (1 - a) * 0.9)[..., None]
        col = mix(hx(c), hx(c) * 0.82, np.clip(dd / (0.35 * h), 0, 1)[..., None])
        col = col * ((0.95 + 0.10 * paper[..., None]) if dark else (0.985 + 0.03 * paper[..., None]))
        img = over(img, col, a)
        hi = np.exp(-((dd - 1.3 * S) / (1.2 * S)) ** 2)
        img = screen(img, mix(hx(c), white, 0.65), hi * 0.8)
        glow = np.exp(-np.clip(dd, 0, None) / (40 * S)) * a
        img = screen(img, mix(hx(c), white, 0.4), glow * 0.18)
    return img


def p_facets(cv: Canvas, dark: bool):
    """Low-poly facets: jittered triangulation, per-facet lighting, crisp lit edges."""
    w, h, S = cv.w, cv.h, cv.S
    rng = np.random.default_rng(707)
    ss = 2
    W2, H2 = w * ss, h * ss
    nx_, ny_ = 20, 12
    xs = np.linspace(-0.05, 1.05, nx_ + 1); ys = np.linspace(-0.05, 1.05, ny_ + 1)
    P = np.zeros((ny_ + 1, nx_ + 1, 3))
    for j, yy in enumerate(ys):
        for i, xx in enumerate(xs):
            jx = rng.uniform(-0.38, 0.38) / nx_ if 0 < i < nx_ else 0
            jy = rng.uniform(-0.38, 0.38) / ny_ if 0 < j < ny_ else 0
            P[j, i] = (xx + jx, yy + jy, rng.uniform(0, 1))
    if dark:
        stops = [(0, "#120A4A"), (0.3, "#3A1690"), (0.55, "#9A2C9E"), (0.75, "#E05A8C"), (1, "#FF9A5A")]
        light = np.array([-0.45, -0.55, 0.70])
        edge_rgba = (255, 255, 255, 28)
    else:
        stops = [(0, "#8FE4FF"), (0.35, "#B9B2FF"), (0.65, "#FFA6CF"), (0.85, "#FFC2A6"), (1, "#FFE6A8")]
        light = np.array([-0.45, -0.55, 0.70])
        edge_rgba = (255, 255, 255, 60)
    light = light / np.linalg.norm(light)
    im = Image.new("RGB", (W2, H2))
    dr = ImageDraw.Draw(im)
    ov = Image.new("RGBA", (W2, H2), (0, 0, 0, 0))
    do = ImageDraw.Draw(ov)
    zscale = 0.06
    for j in range(ny_):
        for i in range(nx_):
            a, b, c, d = P[j, i], P[j, i + 1], P[j + 1, i], P[j + 1, i + 1]
            tris = [(a, b, d), (a, d, c)] if rng.random() < 0.5 else [(a, b, c), (b, d, c)]
            for tri in tris:
                pts = np.array(tri)
                cxm, cym = pts[:, 0].mean(), pts[:, 1].mean()
                t = np.clip(0.55 * cxm + 0.45 * (1 - cym) + 0.08 * math.sin(cxm * 7 + cym * 5), 0, 1)
                base = ramp(np.array([t]), stops)[0]
                p3 = pts.copy(); p3[:, 0] *= w / h; p3[:, 2] *= zscale
                n = np.cross(p3[1] - p3[0], p3[2] - p3[0]); n = n / (np.linalg.norm(n) + 1e-9)
                if n[2] < 0:
                    n = -n
                lam = np.clip(np.dot(n, light), 0, 1)
                shade = 0.72 + 0.45 * lam
                col = np.clip(base * shade + (0.12 * lam ** 8), 0, 1)
                poly = [(float(p[0] * W2), float(p[1] * H2)) for p in pts]
                dr.polygon(poly, fill=tuple(int(x * 255) for x in col))
                do.line(poly + [poly[0]], fill=edge_rgba, width=max(1, int(2 * S * ss / 2)))
    im = Image.alpha_composite(im.convert("RGBA"), ov).convert("RGB").resize((w, h), Image.LANCZOS)
    img = np.asarray(im, F32) / 255
    # large glass sheen sweep
    gl = np.exp(-(((cv.u - cv.v * 0.55) - 0.25) / 0.10) ** 2)
    img = screen(img, np.ones(3, F32), gl * (0.12 if dark else 0.18))
    return img


def p_aurora(cv: Canvas, dark: bool):
    """Aurora curtains with sharp lower edges and ray structure, stars, crisp mountain ridge."""
    w, h, S, X, Y, u, v = cv.w, cv.h, cv.S, cv.X, cv.Y, cv.u, cv.v
    if dark:
        img = ramp(v, [(0, "#040619"), (0.5, "#0A1A3E"), (0.82, "#0B3A52"), (1, "#0A2A3A")])
        img += stars(cv, 81, n=2600, bright=0.9, glints=5) * smoothstep(0.85, 0.3, v)[..., None]
        cols = [(0, "#32FFB0"), (0.25, "#2FE8D8"), (0.55, "#5A7CFF"), (0.8, "#B04CFF"), (1, "#FF4FD8")]
        ridge_c, rim_c = hx("#030812"), hx("#3FFFD0")
    else:
        img = ramp(v, [(0, "#BFD3FF"), (0.5, "#E2E8FF"), (0.82, "#FFE6F0"), (1, "#FFF1E6")])
        cols = [(0, "#00C98A"), (0.3, "#00B4D0"), (0.6, "#5A6CFF"), (0.85, "#B04CE8"), (1, "#E04FB8")]
        ridge_c, rim_c = hx("#6E83B8"), hx("#FFFFFF")
    ux = np.arange(w, dtype=F32) / w
    acc = np.zeros((h, w, 3), F32)
    for k, (base, amp, ph, gain) in enumerate([(0.56, 0.07, 0.4, 1.0), (0.48, 0.09, 2.2, 0.7), (0.63, 0.05, 4.1, 0.55)]):
        warp = noise1d(w, 82 + k, base=3, octaves=3) - 0.5
        yc = h * (base + amp * np.sin(2 * np.pi * (0.8 + 0.2 * k) * ux + ph) + 0.06 * warp)
        rays = noise1d(w * 2, 90 + k, base=60, octaves=4, pers=0.6)
        idx = np.clip((ux + 0.03 * np.sin(2 * np.pi * 3 * ux + k)) * (2 * w - 1), 0, 2 * w - 1).astype(int)
        r1 = rays[idx] ** 1.6
        dy = (yc[None] - Y) / h  # >0 above the curtain base
        prof = smoothstep(-0.004, 0.006, dy) * np.exp(-np.clip(dy, 0, None) / (0.20 + 0.08 * k))
        bright = np.exp(-((dy - 0.008) / 0.012) ** 2) * 0.6
        inten = (prof * (0.35 + 0.9 * r1[None]) + bright * r1[None]) * gain
        inten *= (0.55 + 0.45 * np.sin(2 * np.pi * (1.3 + k * 0.4) * ux + k * 2.0)[None] ** 2)
        col = ramp(np.clip(dy / 0.35, 0, 1), cols)
        acc += col * inten[..., None]
    acc = acc + blur(acc, 18 * S) * 0.6
    if dark:
        img = 1 - (1 - img) * np.exp(-acc * 1.1)
    else:
        lum = acc.mean(-1, keepdims=True)
        hue = np.clip(acc / (lum + 1e-4) * 0.45, 0, 1)
        img = over(img, hue, np.clip(1 - np.exp(-lum[..., 0] * 1.8), 0, 0.85))
    yr = h * (0.86 + 0.05 * np.sin(2 * np.pi * 1.3 * ux + 0.7) + 0.035 * (noise1d(w, 99, base=10, octaves=5) - 0.5) * 2)
    dd = Y - yr[None]
    a = np.clip(dd + 0.5, 0, 1)
    img = over(img, ridge_c, a)
    img = screen(img, rim_c, np.exp(-((dd - 1.2 * S) / (1.1 * S)) ** 2) * (0.7 if dark else 0.9))
    if not dark:
        img = mix(img, np.ones(3, F32), (np.exp(-np.clip(dd, 0, None) / (30 * S)) * a * 0.5)[..., None])
    return img


PRESETS = {
    "ribbons": ("Ribbons", p_ribbons),
    "capsules": ("Capsules", p_capsules),
    "bokeh": ("Bokeh", p_bokeh),
    "nebula": ("Nebula", p_nebula),
    "aero": ("Aero", p_aero),
    "petal": ("Petal", p_petal),
    "facets": ("Facets", p_facets),
    "aurora": ("Aurora", p_aurora),
}


def finish(img: np.ndarray, seed: int) -> Image.Image:
    rng = np.random.default_rng(seed)
    h, w, _ = img.shape
    n = (rng.random((h, w), dtype=F32) - rng.random((h, w), dtype=F32))[..., None]  # TPDF dither
    out = np.clip(img, 0, 1) * 255 + n * 1.0
    return Image.fromarray(np.clip(out + 0.5, 0, 255).astype(np.uint8))


def render(name: str, dark: bool, w=2560, h=1440) -> Image.Image:
    cv = Canvas(w, h)
    img = PRESETS[name][1](cv, dark).astype(F32)
    return finish(img, hash((name, dark)) & 0xFFFF)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=str(Path(__file__).with_name("proposed")))
    ap.add_argument("--only", nargs="*")
    ap.add_argument("--scale", type=float, default=1.0)
    a = ap.parse_args()
    out = Path(a.out); out.mkdir(parents=True, exist_ok=True)
    w, h = int(2560 * a.scale), int(1440 * a.scale)
    for name in a.only or PRESETS:
        for dark in (True, False):
            t0 = time.time()
            im = render(name, dark, w, h)
            stem = f"{name}-{'dark' if dark else 'light'}"
            im.save(out / f"{stem}.jpg", "JPEG", quality=88, optimize=True, subsampling=0)
            im.resize((224, 126), Image.LANCZOS).save(out / f"{stem}-thumb.jpg", "JPEG", quality=88)
            im.resize((448, 252), Image.LANCZOS).save(out / f"{stem}-thumb@2x.jpg", "JPEG", quality=88)
            print(f"{stem}: {(out / (stem + '.jpg')).stat().st_size // 1024} KB  {time.time() - t0:.1f}s", flush=True)


if __name__ == "__main__":
    main()
