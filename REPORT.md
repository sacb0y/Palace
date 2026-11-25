# FenrirDesktop V1 tile evidence — PR #206

**Tip:** `69ff940afadda5b7bc692dac589c9b46ae86c92d` (69ff940) — Wimbly evidence fix (CPU downsample origin + GPU emu)  
**Branch:** `cursor/smooth-viewer-v1-tiles`  
**Package:** PalaceOrganizeTest-5E1F `0.3.120.0` (never C20A7629)  
**ProductVersion:** `0.3.120+69ff940afadda5b7bc692dac589c9b46ae86c92d`  
**Host:** FenrirDesktop; no DPI changes  
**Override:** `-AllowDesktop` — `V1 tile cache, Isiac asleep, laptop locked`  
**Native:** `palace_smooth.dll` Ninja+vcvars VS18 Release (1,880,576 bytes) → `Native/smooth/win-x64` + Debug/Release bins  
**Launch:** `Invoke-CommandInDesktopPackage` (5E1F package context; naked `Palace.exe` hits WASDK `REGDB_E_CLASSNOTREG`). `PALACE_DATA_DIR` via env + LocalState `palace-data` junction.  
**Capture:** UIA status + frame logs + HWND `PrintWindow` coarse screens

## Verdict: **PASS**

| Gate | Result |
|---|---|
| Native DLL present | YES |
| OFF Release `firstCoarseMs≲6000` | **PASS** — `firstCoarseMs=5812`; `uploaded=6/6`; `gpuMips=126`; `rbN=0` |
| ON Debug `rbOverUlp=0` | **PASS** — `rbOverUlp=0`; `rbMax=0.001953` (≲0.002); `rbN=126`; `gpuMips=126`; `uploaded=6/6` |
| Frame log `over1ulp=0` ×126 | **PASS** — 126 `gpu-vs-cpu` lines; 126× `over1ulp=0`; maxAbs peak 0.00195312 |
| Screen coarse tiles align | **PASS** — HWND `screen-coarse-off.png` / `screen-coarse-on.png` show grid across viewport (not top-left only) |

### Units

| Field | Unit / meaning |
|---|---|
| **rbMax** | Absolute float error (Working). Gate: ≲0.002 on ON |
| **rbOverUlp** | Count of half channels with `|c−g| > 1` half-ULP. Gate: **0** |
| **rbN** | GPU mip readbacks compared (ON expects 126) |

## Image / session size

| Field | Value |
|---|---|
| Session | **12000×8000** procedural `SmoothTileSession` |
| Working | RGBA16F, Border=2 (508 content), coarse=L3, coarseTiles=6, pool=192 |
| File decode | **N/A** — synthetic procedural tiles |

## Timing

`firstCoarseMs` = `BuildPyramidCore` (CPU tile gen + GPU mip CS; Debug includes TileReadback+compare).

| Run | Config | Pyramid (`firstCoarseMs`) | Upload |
|---|---|---|---|
| **OFF** | **Release** | **5812 ms** | 6/6 |
| **ON** | **Debug** (GpuEvidence always-on) | **33411 ms** | 6/6 |

## Metrics table

| Metric | OFF (Release) | ON (Debug) |
|---|---|---|
| pool / uploaded / gpuMips | 192 / 6/6 / 126 | 192 / 6/6 / 126 |
| vramAvail | 24547459072 | 24547459072 |
| rbMax (abs float) | 0 | **0.001953** |
| rbOverUlp | 0 | **0** |
| rbN | 0 | **126** |
| Present | ~0.64 ms @ 2864×1449 | ~0.67 ms @ 2864×1449 |

## Files

`metrics-off.json`, `metrics-on.json`, `metrics-summary.json`, `status-samples-*.txt`, `smooth-presenter-frames-*.log`, `screen-coarse-*.png` (+ captions), `CAPTURE_EVENTS.txt`, `MANIFEST.json`, this `REPORT.md`.
