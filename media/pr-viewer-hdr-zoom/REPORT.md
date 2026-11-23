# FenrirDesktop HDR zoom integrity capture — PR #202

**Tip (settle upgrade):** `51044e0059409118eba2b10e19d8c93d2c7733e5` (`51044e0`) — HDR settle: pending path+want prevents decode starvation  
**Prior tips:** `fcffff9` (Actual PASS; portrait HOLD), `fabb4c5` (integrity base; Actual FAIL)  
**Branch:** `cursor/viewer-hdr-zoom-fixes-1b82`  
**Package:** PalaceOrganizeTest-5E1F `0.3.114.0`  
**Host:** FenrirDesktop 3840×2160 @ 96 DPI (scale=1.0); maximized; ScrStill client **3591×2079** (preview pane on)  
**Fit scale:** **0.935** (= 3591/3840 for landscape Fit present decode width)  
**Override:** `-AllowDesktop` + `Isiac authorized desktop testing for viewer HDR zoom bugs A/B/C (PR #202)`  
**Env:** `PALACE_HDR_PRESENT_LOG=1` → `{PALACE_DATA_DIR}/hdr-present.log`  
**Data:** LocalState `palace-data` SourceFolder → `%TEMP%\palace-pr202-hdr-51044e0\viewer-hdr` (synthetic only; no DPI; no C20A)

## Integrity

| Check | Result |
|---|---|
| All PNGs distinct sha256 | **PASS** — see `MANIFEST.json` |
| Real app present logs | **PASS** — `hdr-present-portrait-maxzoom.log` / `hdr-present-portrait-actual-51044e0.log` from `PALACE_HDR_PRESENT_LOG` |
| Clean error-log | **PASS** — `error-log-51044e0.txt` empty |
| FenrirDesktop policy | **PASS** — override audit in `palace-ui-desktop-override.log` |

## Portrait PQ settle upgrade — tip `51044e0` (required)

Hold ≥10s after max-zoom settle. Must show native decode Presented (not stuck at ~1169×2079).

| Check | Result |
|---|---|
| Portrait PQ max zoom → native decode | **PASS** — after 12.0s hold: `decode=2160x3840 panelDip=4608x8192 bufferPx=4608x8192 scale=1x1 result=Presented` |
| Not stuck at intermediate | **PASS** — intermediate `1152x2048` / Fit-sized presents superseded by native Present |
| Double-tap 1:1 (touch simulated) | **PASS** — `winapp ui touch --at x,y -g double-tap`; 1st → Fit panel (`1169×2079`); 2nd → Actual `panelDip=2160x3840` |
| Fit scale reported | **0.935** |

### Verbatim present-log (portrait maxzoom settle)

```
2026-11-23T04:57:22.214Z file=synth_pq_port_2160x3840.png decode=1152x2048 panelDip=1712.17x3043.86 bufferPx=1712x3044 scale=1x1 result=Presented
2026-11-23T04:57:23.932Z file=synth_pq_port_2160x3840.png decode=1152x2048 panelDip=4608x8192 bufferPx=4608x8192 scale=1x1 result=Presented
2026-11-23T04:57:23.933Z file=synth_pq_port_2160x3840.png decode=2160x3840 panelDip=0x0 bufferPx=0x0 scale=0x0 result=DecodeStart detail=refresh
2026-11-23T04:57:25.557Z file=synth_pq_port_2160x3840.png decode=1152x2048 panelDip=4608x8192 bufferPx=4608x8192 scale=1x1 result=Presented
2026-11-23T04:57:32.362Z file=synth_pq_port_2160x3840.png decode=2160x3840 panelDip=4608x8192 bufferPx=4608x8192 scale=1x1 result=Presented
```

### Verbatim present-log (double-tap → Actual 1:1)

```
2026-11-23T04:57:41.668Z file=synth_pq_port_2160x3840.png decode=2160x3840 panelDip=1169.44x2079 bufferPx=1169x2079 scale=1x1 result=Presented
2026-11-23T04:58:12.865Z file=synth_pq_port_2160x3840.png decode=2160x3840 panelDip=2160x3840 bufferPx=2160x3840 scale=1x1 result=Presented
```

### Shots (this tip)

| File | Role |
|---|---|
| `20-port4k-pq-fit.png` | Portrait PQ Fit open |
| `24-port4k-pq-maxzoom.png` | Max zoom after ≥10s settle (native present) |
| `42-port4k-pq-doubletap-fit-51044e0.png` | After 1st double-tap (Fit panel) |
| `43-port4k-pq-actual-1to1-51044e0.png` | After 2nd double-tap (Actual 2160×3840) |
| `CAPTURE_EVENTS_51044e0.txt` | Timed event log |
| `hdr-present-portrait-maxzoom.log` | Settle proof log |
| `hdr-present-portrait-actual-51044e0.log` | Double-tap / Actual proof log |

## Prior (still in package)

- `fabb4c5` integrity land maxzoom / rapid-notch / disc measures — kept
- `fcffff9` land Actual native `decode=3840x2160` + Ø720 — kept (`30`/`31` shots)
- Portrait PASS on `fcffff9` remains **discarded** (never upgraded); superseded by `51044e0` proof above

## Notes

- Wheel ramps toward 8×; DIP cap clamps panel to 8192 → portrait max `panelDip=4608x8192`
- Fit scale **0.935** = ScrStill width 3591 / native land width 3840
- No DPI flips; 5E1F packaged only; catalog restored after capture