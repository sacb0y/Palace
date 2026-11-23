# PR #200 tip 800a310 (0.3.114) — FENRIR-STUDIO harness evidence

**Host:** FENRIR-STUDIO · **DPI:** 150% (windowDpi=144) · client 1280×900 DIP  
**Identity:** PalaceOrganizeTest-5E1F only (never C20A7629)  
**Build:** Palace-200 @ 800a310 / 0.3.114 Debug x64  
**Data:** seeded LocalState from %TEMP%\palace-agent-data-pr200 (8 assets / 38 tags)

## Checklist

| Check | Verdict | Evidence |
|-------|---------|----------|
| `-SelfTestDesktopGate` | **PASS** | selftest-800a310.out — shared audit under `%TEMP%\palace-desktop-gate-…` only; `%LOCALAPPDATA%\PalaceTestAudit` absent |
| Identity gate (`Test-PalaceUiTestIdentity`) | **PASS** | ui-tests: `PalaceOrganizeTest-5E1F 0.3.114.0` |
| DPI + XamlRoot client gate (S7 prelude) | **PASS** | xamlroot-800a310.log (1200→1280 @ RasterizationScale=1.5); 01-dpi-1280x900-800a310.png |
| Stock `ui-tests.ps1` suite | **FAIL** (69 pass / 51 fail) | ui-tests-800a310.out |
| Stock S7 Tags drag (probe/status) | **FAIL** | ui-tests still targets `BtnCreateUngroupedTag`; tip TagsPage AutomationId is `BtnCreateNewTags` (post-#199 rename). Create/chip steps fail; drag probe empty. |

## Notes

- Fenrir/C20A **not** touched (release package still `C20A7629…` 0.3.58.0).
- Harness DPI/XamlRoot path works on Studio at tip 800a310.
- S7 blocked by **ui-tests selector drift** vs product Tags create button — tip fix needed (`BtnCreateUngroupedTag` → `BtnCreateNewTags`) before stock S7 can PASS; not a FenrirDesktop policy issue.
