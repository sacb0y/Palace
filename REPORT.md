# FenrirDesktop V1 tile evidence — PR #206

**Tip:** `b07ce28b6045c337f60db6b0a46b5fc77aa59caf` (`b07ce28`) — CI green; Wimbly round-2  
**Branch:** `cursor/smooth-viewer-v1-tiles`  
**Package:** PalaceOrganizeTest-5E1F `0.3.120.0` (never C20A7629)  
**Host:** FenrirDesktop; no DPI changes  
**Override:** `-AllowDesktop` — `V1 tile cache, Isiac asleep, laptop locked`  
**Native:** `palace_smooth.dll` Ninja+vcvars VS18 Release (1,880,576 bytes) → `Native/smooth/win-x64` + Debug/Release bins  
**Capture:** UIA status + frame logs only (no screen grab)

## Verdict: **FAIL** (GPU-vs-CPU half-ULP gate)

**Gate:** pass only if **no** half-float channel differs by more than **1 half-ULP** (`rbOverUlp == 0`).

| Check | Result |
|---|---|
| Native DLL present | YES |
| OFF Release timing (evidence off) | YES — firstCoarseMs=13749; rbN=0 |
| ON Debug evidence ran (rbN=126) | YES |
| **≤1 half-ULP everywhere** | **FAIL** — `rbOverUlp=53741834` (≫ 0) |

Do **not** treat this pack as merge-ready. Doktor/Wimbly still need a green ULP gate (or an agreed change to the compare).

### Units (important)

From `SmoothDiligentPresenter.CompareReadback`:

| Field | Unit / meaning |
|---|---|
| **rbMax** | **Absolute float** error: `max \|halfBitsToFloat(cpu) − halfBitsToFloat(gpu)\|` over compared half channels. **Not** a ULP count. ON value `1.382` is ~1.38 in linear Working float space. |
| **rbOverUlp** | **Count of half channels** where raw `uint16` half bits differ by **more than 1** (`\|c−g\| > 1`). This is the **>1 half-ULP** counter. Gate uses this. |
| **rbN** | Number of GPU mip readbacks compared (ON: 126 = one per `gpuMips`). |

So `rbMax=1.382` with `rbN=126` does **not** mean “1.382 ULPs”; it means peak absolute float error ~1.38 while **tens of millions** of half channels exceeded 1 half-ULP. Frame-log `over1ulp=` is the per-mip contribution to that count.

## Image / session size

| Field | Value |
|---|---|
| Session | **12000×8000** procedural `SmoothTileSession` (hardcoded in `SmoothPresenterWindow`) |
| Working | RGBA16F, Border=2 (508 content), coarse=L3, coarseTiles=6, pool=192 |
| File decode | **N/A** — synthetic procedural tiles; not PNG/file decode |

## Timing split (decode / pyramid / upload)

`firstCoarseMs` = `BuildPyramidCore` stopwatch (CPU tile gen + GPU mip CS on UI thread; Debug also includes TileReadback+compare).  
Upload = `UploadCoarseTiles` (6 coarse) in `FinalizeV1CoarseUploads` **after** that stopwatch; **not separately instrumented**.

| Run | Config | Decode | Pyramid (`firstCoarseMs`) | Wall pool→done | Upload |
|---|---|---|---|---|---|
| **OFF** | **Release**, evidence env unset | N/A | **13749 ms** | **12310 ms** | 6/6; ≪ pyramid; not timed alone |
| **ON** | **Debug**, GpuEvidence always-on | N/A | **45598 ms** | **45407 ms** | 6/6; ≪ pyramid; not timed alone |

OFF ~13.7s is the 12k×8k / 126-mip Release pyramid without readback. ON ~45.6s is the same work plus 126 readbacks/compares (~3.3×).

## Metrics table

| Metric | OFF (Release) | ON (Debug) |
|---|---|---|
| pool / uploaded / gpuMips | 192 / 6/6 / 126 | 192 / 6/6 / 126 |
| vramAvail | 24547459072 | 24549425152 |
| rbMax (abs float) | 0 | **1.382** |
| rbOverUlp (>1 half-ULP count) | 0 | **53741834** → gate FAIL |
| rbN | 0 | **126** |
| Present | ~0.65 ms @ 2864×1449 | ~0.81 ms @ 2864×1449 |

ON frame log: 126 `gpu-vs-cpu` notes (`smooth-presenter-frames-on.log`). Examples: mip#1 `maxAbs≈0.00195 over1ulp=0`; mip#122/124 `maxAbs≈1.382 over1ulp≈520180`.

### Evidence-ON launch note

AppsFolder Release + `$env:PALACE_SMOOTH_GPU_EVIDENCE=1` still showed `rbN=0` (env not inherited by AppX). Authoritative ON = **Debug** (compile-time evidence always on).

## Files

`metrics-off.json`, `metrics-on.json`, `metrics-summary.json`, `status-samples-*.txt`, `smooth-presenter-frames-*.log`, `CAPTURE_EVENTS.txt`, `MANIFEST.json`, this `REPORT.md`.
