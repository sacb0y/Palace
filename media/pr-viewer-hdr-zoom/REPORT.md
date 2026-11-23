# FenrirDesktop HDR zoom integrity capture — PR #202

**Tip (integrity base):** `fabb4c526a7a8b0c44f305cd92d2cd0cc301fb77` (`fabb4c5`)
**Tip (Actual recheck):** `fcffff9e52786dad0d17714a83dad7c60a441494` (`fcffff9`)  
**Branch:** `cursor/viewer-hdr-zoom-fixes-1b82`  
**Discard prior media** (edde086 / 5d86906 integrity FAIL — do not count)  
**Package:** PalaceOrganizeTest-5E1F `0.3.114.0`  
**Host:** FenrirDesktop 3840×2160 @ 96 DPI (scale=1.0); maximized client 3840×2112  
**Override:** `-AllowDesktop` + `Isiac authorized desktop testing for viewer HDR zoom bugs A/B/C (PR #202)`  
**Env:** `PALACE_HDR_PRESENT_LOG=1` → `{PALACE_DATA_DIR}/hdr-present.log`  
**Data:** LocalState `palace-data` SourceFolder → `%TEMP%\palace-pr202-hdr-fabb4c5\viewer-hdr` (synthetic only; no DPI; no C20A)

## Integrity

| Check | Result |
|---|---|
| All PNGs distinct sha256 | **PASS** — see `MANIFEST.json` (8 PNGs, no duplicate hashes); sizes match disk |
| Separate rapid-notch shot | **PASS** — `16-land4k-rapid-notch-maxzoom.png` sha ≠ pre-fit and ≠ slow maxzoom |
| Cursor + disc-under-cursor | **PASS** — cursor logged; maxzoom/rapid `cursor_rgb=[255,255,255]` `white_frac_r40=1.0` |
| Honest DIP-cap zoom (not ~8×) | **PASS** — present log `panelDip=8192x4608` (cap); effective zoom ≈ 8192/3591 ≈ **2.28×** Fit panel |
| Real app present logs | **PASS** — `hdr-present-*.log` from `PALACE_HDR_PRESENT_LOG` (decode/panelDip/bufferPx/scale/result) |
| Actual disc Ø720 | **FAIL** — measured diam_radial=673; present log decode stays `3591x2020` (not native `3840x2160`). HDR forces `DisplayScaling=Fit` when presentable (`GalleryScale.DisplayScaling`); Actual sticky does not yield native 1:1 present on this tip. |
| Clean error-log | **PASS** — empty / no `GalleryStillSurface HDR present` |

## Verdicts (functional)

| Check | Result |
|---|---|
| Maximized + Fit land HDR present | **PASS** — Info + present path; fit disc diam_radial≈673 (native disc=720; Fit scale ≈0.98) |
| Max zoom under DIP cap + new present | **PASS** — `result=Presented` at `panelDip=8192x4608` `bufferPx=8192x4608` |
| Rapid-notch no collapse to 0,0 | **PASS** — `frac_tl=0.0004`; disc still under cursor white |
| Portrait PQ max zoom present | **PASS** — `HDR PNG` path; `panelDip=4608x8192` `result=Presented` |

## Key present-log lines (verbatim from app)

```
# land maxzoom
file=synth_land_3840x2160.hdr decode=3591x2020 panelDip=8192x4608 bufferPx=8192x4608 scale=1x1 result=Presented

# rapid
file=synth_land_3840x2160.hdr decode=3591x2020 panelDip=8192x4608 bufferPx=8192x4608 scale=1x1 result=Presented

# actual (FAIL native 1:1)
file=synth_land_3840x2160.hdr decode=3591x2020 panelDip=3591x2019.94 bufferPx=3591x2020 scale=1x1 result=Presented

# portrait maxzoom
file=synth_pq_port_2160x3840.png decode=1169x2079 panelDip=4608x8192 bufferPx=4608x8192 scale=1x1 result=Presented
```

## Notes

- Fixture disc diameter at native = `2*(min(w,h)/6)` = **720** px for 3840×2160.
- Wheel still ramps `_pinchZoom` toward 8, but `CapStillPanelSize` clamps panel DIPs so physical ≤8192 → **~2.28×** Fit, not 8× visual.
- No reused/hand-written logs; all `hdr-present-*.log` copied from DataDir after the action.

---

## Actual recheck — tip `fcffff9` (2026-11-23)

**Tip:** `fcffff9e52786dad0d17714a83dad7c60a441494` — HDR Actual clears Fit-on-open force for explicit Actual/Fill.

Prior tip `fabb4c5` Actual Ø720 was **FAIL** (HDR forced DisplayScaling=Fit; decode stayed 3591×2020).

| Check | Result |
|---|---|
| After Actual: present-log `decode=3840x2160` | **PASS** — `hdr-present-actual-fcffff9.log`: `decode=3840x2160 panelDip=3840x2160 bufferPx=3840x2160 result=Presented` |
| Disc Ø720 | **PASS** — measured diam_radial=**717** (fit baseline 673); see `31-actual-disc-measure.json` |
| New shots distinct sha256 | **PASS** — appended to `MANIFEST.json` |
| Clean error-log (recheck) | **PASS** — `error-log-fcffff9.txt` |

Verbatim present lines:
```
file=synth_land_3840x2160.hdr decode=3591x2020 panelDip=3591x2019.94 bufferPx=3591x2020 scale=1x1 result=Cancelled
file=synth_land_3840x2160.hdr decode=3840x2160 panelDip=3840x2160 bufferPx=3840x2160 scale=1x1 result=Presented
```

Shots: `30-land4k-fit-before-actual.png`, `31-land4k-actual-1to1.png`.

