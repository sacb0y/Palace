# PR1 harness/safety Studio retest — 2026-11-22

**Verdict: FAIL / PARTIAL** (tip `c766d02` / 0.3.113) — waiting on chrome-compensated Resize fix tip; machine freed for #199.

## Scope
- Branch: `cursor/harness-safety-9a77`
- Tip tested: `c766d025b1dad85b0341cde1c50d62c67047f94c`
- Machine: FENRIR-STUDIO (ac6e4da9)
- Identity: `PalaceOrganizeTest-5E1F` (never C20A7629)
- Data: `%TEMP%\palace-agent-data-pr200` + SFW copy `%TEMP%\palace-sfw-pr200` (from `palace-sfw-fenrir-gate`)
- Layout: `%LOCALAPPDATA%\winapp-layout\palace-pr200-113-debug-x64`

## Gates

| Gate | Result | Notes |
|------|--------|-------|
| Wait for #199 session | PASS | Waited out Palace-199-pr2 build/run before starting |
| Prepare-TestIdentity 5E1F | PASS | Version 0.3.113.0; C20A stayed 0.3.58.0 |
| PER_MONITOR_AWARE_V2 | PASS | `Enable-PalaceDpiAwareness` |
| Shipped `Resize-PalaceWindowDip 1280×900` + `Assert-PalaceClientDipGate` | **FAIL** | Outer-window sizing: clientDipW=**1265.33** (H≈892.67) vs target 1280±2 @150% (DPI 144). Chrome ≈22×11 physical px. |
| Chrome-compensated client 1280×900 gate | PASS | Diagnostic only — confirms gate works when client DIP is correct |
| Tags drag (ui-tests IDs) | **BLOCKED / PARTIAL** | `BtnCreateUngroupedTag` missing in live UI; XAML/AutomationId is `BtnCreateNewTags`. UIA create/chip path hung; stopped after coordinator 15m rule. Probe QC not completed on this tip. |
| Probe QC DragStarting/Over/Drop | NOT RUN | Blocked before successful SendInput drag on this tip |
| Real catalog / C20A / Unity | PASS | `%LOCALAPPDATA%\Palace` LastWrite unchanged 2026-11-12; C20A 0.3.58.0; Unity not touched |

## Confirmed finding (coordinator-aligned)
Shipped `Resize-PalaceWindowDip` sizes the **outer** HWND to `DipWidth×scale` physical px. `Assert-PalaceClientDipGate` measures **client** DIP. At 150% the non-client chrome makes ui-tests' `1280` target fail (~15 DIP short). Cloud PR1 worker will push chrome-compensated fix; Studio will re-pull new tip and finish Tags drag + probe QC.

## Secondary finding
`ui-tests.ps1` Tags drag still invokes `BtnCreateUngroupedTag`; `Pages/TagsPage.xaml` exposes `BtnCreateNewTags`. Live inspect on this build confirmed `BtnCreateNewTags` / `TxtNewTagName`.

## Artifacts
- Media: `C:\Users\iadag\wkspaces\_fenrir\media\pr1-harness-200\`
- Logs: `%TEMP%\palace-pr200-logs\tags-drag-result.json`
- Worktree: `C:\Users\iadag\source\repos\palace-wt\pr200-harness-c766d02`

## Machine state
Palace / winapp for this session **stopped**. User env `PALACE_DATA_DIR` / `PALACE_TAG_DRAG_*` cleared. Ready for #199 smoke.
