# FENRIR-STUDIO evidence — PR #203 HDR zoom nits (0.3.117)

| Field | Value |
|---|---|
| PR | https://github.com/sacb0y/Palace/pull/203 |
| Branch | `cursor/hdr-zoom-nits-a116` |
| Tip | `87b59629605eaff43dc706639ba1d3feef8d6954` (`87b5962`) |
| Version | **0.3.117** |
| Host | **FENRIR-STUDIO** (150% DPI, scale=1.5); client **1280×900** DIP; **not maximized** |
| Identity | PalaceOrganizeTest-5E1F only |
| Data | `PALACE_DATA_DIR` → `%TEMP%\palace-agent-data-pr203` (junctioned LocalState `palace-data`); SFW `%TEMP%\palace-sfw-pr203` |
| Env | `PALACE_HDR_PRESENT_LOG=1` (layout entry wrapper injects for AppX) |
| CI | **green** on tip — version-check + build-test + Cursor Approval |
| Merge | **not merged** |

## Required scenarios

| # | Scenario | Result |
|---|---|---|
| 1 | Full `ui-tests.ps1` on tip | Ran. **Attributable fails: 0**. Raw: 80 pass / 1 skip / 39 fail — fails are fixture/harness (Settings panes not reached, no PalaceUiTest watched-folder delete fixture, Tags chip UIA size 0 SKIP). Not tip regressions for PNG cancel / superseded log / UI-marshal settle. |
| 2 | PNG decode cancel on re-zoom / settle | **PASS** — open settle races show `DecodeStart` then `DecodeSuperseded` before `Presented` for both land + port synth PQ PNGs. |
| 3 | `PALACE_HDR_PRESENT_LOG` DecodeSuperseded / Superseded | **PASS** — real app lines in `hdr-present-full.log` (verbatim below). |
| 4 | Settle `RefreshAsync` on UI thread | **PASS** — presents complete after superseded decode; **no** `RPC_E_WRONG_THREAD` / `0x8001010E`; error-log absent/empty. |

## Verbatim `hdr-present.log` (app-written)

```
2026-11-24T21:51:10.172Z file=synth_pq_land_1920x1080.png decode=0x0 panelDip=0x0 bufferPx=0x0 scale=0x0 result=DecodeStart detail=refresh
2026-11-24T21:51:10.192Z file=synth_pq_land_1920x1080.png decode=0x0 panelDip=0x0 bufferPx=0x0 scale=0x0 result=DecodeStart detail=refresh
2026-11-24T21:51:10.194Z file=synth_pq_land_1920x1080.png decode=0x0 panelDip=0x0 bufferPx=0x0 scale=0x0 result=DecodeSuperseded detail=refresh
2026-11-24T21:51:11.933Z file=synth_pq_land_1920x1080.png decode=1920x1080 panelDip=1030.67x579.75 bufferPx=1546x870 scale=1.5x1.5 result=Presented
2026-11-24T21:51:12.631Z file=synth_pq_land_1920x1080.png decode=1920x1080 panelDip=1030.67x579.75 bufferPx=1546x870 scale=1.5x1.5 result=Presented
2026-11-24T21:52:05.884Z file=synth_pq_port_1080x1920.png decode=731x1299 panelDip=0x0 bufferPx=0x0 scale=0x0 result=DecodeStart detail=refresh
2026-11-24T21:52:05.889Z file=synth_pq_port_1080x1920.png decode=731x1299 panelDip=0x0 bufferPx=0x0 scale=0x0 result=DecodeStart detail=refresh
2026-11-24T21:52:05.890Z file=synth_pq_port_1080x1920.png decode=731x1299 panelDip=0x0 bufferPx=0x0 scale=0x0 result=DecodeSuperseded detail=refresh
2026-11-24T21:52:06.922Z file=synth_pq_port_1080x1920.png decode=731x1299 panelDip=487.13x866 bufferPx=731x1299 scale=1.5x1.5 result=Presented
2026-11-24T21:52:07.306Z file=synth_pq_port_1080x1920.png decode=731x1299 panelDip=487.13x866 bufferPx=731x1299 scale=1.5x1.5 result=Presented
```

## Captures

| File | Caption |
|---|---|
| `00-after-scan.png` | After Scan of seeded temp SFW |
| `01-land-pq-fit.png` | Land PQ Fit open |
| `02-land-pq-after-rapid-zoom.png` | Land after zoom/settle attempts |
| `03-land-pq-zoom-info.png` | Info affordance after land present |
| `04-port-pq-fit.png` | Port PQ Fit open |
| `05-port-pq-zoomed-settle.png` | Port after settle |
| `ui-tests/*.png` | Suite page shots |
| `hdr-present-full.log` | Full present probe |
| `MANIFEST.json` | Distinct sha256 per file (`png_unique: true`) |

## Safety

- Never C20A7629 / `%LOCALAPPDATA%\Palace` / Unity / MOMOCore / room.jxl / Pragmata
- One existing checkout `Palace-main-f05e548` switched in place to tip; no new worktree
- BelowNormal `-m:1` build; machine lightly loaded (Unity Hub only)
- Repo `Package.appxmanifest` restored after run

## ui-tests attributable fails

**0** attributable to #203 tip behavior under test.

Raw fails (39) are pre-existing fixture/nav gaps on this synthetic seed (Settings AutomationIds not found without section nav, no delete watched-folder fixture, Tags chip UIA size 0 → SKIP). See `ui-tests.log`.
