# FenrirDesktop V1 tile evidence — PR #206

**Tip:** `b07ce28b6045c337f60db6b0a46b5fc77aa59caf` (`b07ce28`) — CI green; Wimbly round-2  
**Branch:** `cursor/smooth-viewer-v1-tiles`  
**Package:** PalaceOrganizeTest-5E1F `0.3.120.0` (never C20A7629)  
**Host:** FenrirDesktop; no DPI changes  
**Override:** `-AllowDesktop` — `V1 tile cache, Isiac asleep, laptop locked`  
**Native:** `palace_smooth.dll` built Ninja+vcvars VS18 Release (1,880,576 bytes); copied to `Native/smooth/win-x64` + Debug/Release bins  
**Capture:** UIA status + frame logs only (no screen grab)

## Image / session size

| Field | Value |
|---|---|
| Session | **12000×8000** procedural `SmoothTileSession` (hardcoded in `SmoothPresenterWindow`) |
| Working format | RGBA16F, Border=2 (508 content), coarse=L3, coarseTiles=6 |
| File decode | **N/A** — synthetic procedural tiles; not PNG/file decode path |
| Synthetic PNG on disk | `palace-sfw-206-b07ce28\v1-hdr-12000x8000.png` present for catalog context only |

## Timing split (decode / pyramid / upload)

`firstCoarseMs` is the `BuildPyramidCore` stopwatch (CPU tile generation + GPU mip CS marshaled to UI thread). Upload of the 6 coarse tiles is `UploadCoarseTiles` inside `FinalizeV1CoarseUploads` **after** that stopwatch stops; it is **not separately instrumented** and lands in the same UIA sample as `uploaded=6/6`.

| Run | Config | Decode | Pyramid (`firstCoarseMs`) | Wall pool→done | Upload |
|---|---|---|---|---|---|
| **OFF** | **Release**, evidence env unset | N/A (procedural) | **13749 ms** | **12310 ms** | 6/6 coarse; not timed separately (≪ pyramid) |
| **ON** | **Debug**, GpuEvidence always-on | N/A (procedural) | **45598 ms** | **45407 ms** | 6/6 coarse; not timed separately |

**Why OFF firstCoarseMs≈13.7s:** 12k×8k procedural pyramid through L3 with **126 GPU mips** on Release (no readback). Wall clock from UIA (~12.3s) matches.  
**Why ON ≈45.6s:** same pyramid plus **126 TileReadback + CPU compares** in Debug evidence path (~3.3× Release).

## Evidence metrics

| Metric | OFF (Release) | ON (Debug) |
|---|---|---|
| pool | 192 | 192 |
| uploaded | 6/6 | 6/6 |
| gpuMips | 126 | 126 |
| vramAvail | 24547459072 | 24549425152 |
| rbMax | 0 (evidence off) | **1.382** |
| rbOverUlp | 0 | **53741834** |
| rbN | 0 | **126** |
| Present status | frame ~377 · 2864×1449 · total≈0.65ms | frame ~1331 · 2864×1449 · total≈0.81ms |

`gpu-vs-cpu` frame-log notes: **126** lines on ON (see `smooth-presenter-frames-on.log`). Tail includes mip#122/124 `maxAbs≈1.382` driving `rbMax`.

### Evidence-ON launch note

First AppsFolder Release attempt with `$env:PALACE_SMOOTH_GPU_EVIDENCE=1` still reported `rbN=0` (AppX does not inherit that env). Authoritative ON pack uses **Debug** (`GpuEvidenceEnabled` compile-time always true).

## Verdict

| Check | Result |
|---|---|
| Native DLL present + loaded | **YES** |
| OFF Release timing (no evidence) | **YES** — firstCoarseMs=13749; rbN=0 |
| ON Debug GPU-vs-CPU evidence | **YES** — rbN=126, rbMax=1.382, 126 log notes |
| Device errors | none observed in status lines |

**Do not merge** — standing gate: Fenrir evidence (this pack) + Doktor + Wimbly OK.

## Files

- `metrics-off.json` / `metrics-on.json` / `metrics-summary.json`
- `status-samples-off.txt` / `status-samples-on.txt`
- `smooth-presenter-frames-off.log` / `smooth-presenter-frames-on.log`
- `CAPTURE_EVENTS.txt`
- `REPORT.md` / `MANIFEST.json`
