# FenrirDesktop Smooth V0 resize-fix recapture — PR #204

**Tip:** `0bc99df99644841d76e02ebd0dd9984d63853034` (`0bc99df`) — CI green; release backbuffer wrap before ResizeBuffers  
**Prior tip (blocker):** `07d395f` — pattern presented but ResizeBuffers failed @ 640×480  
**Branch:** `cursor/smooth-viewer-v0-diligent-990d`  
**Package:** PalaceOrganizeTest-5E1F `0.3.118.0` (never C20A7629)  
**Host:** FenrirDesktop 3840×2160; no DPI changes; BelowNormal builds while Unity busy  
**Override:** `-AllowDesktop` — see `palace-ui-desktop-override.log`

## Verdict: **PASS**

| Check | Result |
|---|---|
| HDR scRGB pattern presented | **YES** — fills client after resize (see `13-maximized-hold.png`) |
| Real client sizes in log | **YES** — sizes include 2864×1469, 1264×626 … **3840×2034** (maximized) |
| ResizeBuffers failures | **0** / 1707 lines |
| Maximize / restore / minimize / restore | **PASS** — events in `CAPTURE_EVENTS.txt` |
| Full-size frame times (3840×2034, N=807) | **p50=0.655 ms · p95=0.963 ms · p99=1.202 ms** (mean 0.67 ms) |

## Stress sequence

| Shot | Action | Client (HWND) | Status present WxH |
|---|---|---|---|
| `01-open-default.png` | Open | 2864×1524 | 2864×1469 |
| `02`…`06` | SetWindowPos resizes | 1264…1904 | matching panel |
| `10-maximized.png` | Maximize | 3840×2089 | **3840×2034** |
| `11-restore-after-max.png` | Restore | 1424×861 | 1424×806 |
| `12-restore-after-min.png` | Min→Restore | 1424×861 | 1424×806 |
| `13-maximized-hold.png` | Maximize hold | 3840×2089 | **3840×2034** |

## Verbatim log (open + max)

```
2026-11-24T23:39:08.8133278+00:00 frame=1 d3d12 2864x1469 wait_ms=0.001 render_ms=2.636 present_ms=0.619 total_ms=3.256
2026-11-24T23:39:08.8212066+00:00 frame=2 d3d12 2864x1469 wait_ms=0.001 render_ms=0.391 present_ms=0.707 total_ms=1.100
…
2026-11-24T23:39:27.9294161+00:00 frame=645 d3d12 3840x2034 wait_ms=0.001 render_ms=0.186 present_ms=0.249 total_ms=0.435
2026-11-24T23:39:27.9331083+00:00 frame=645 d3d12 3840x2034 wait_ms=0.001 render_ms=0.000 present_ms=0.000 total_ms=0.001 note=frame latency not ready — skipped
```

**Media branch SHA:** (filled after push) on `media/pr204-smooth-v0`.


