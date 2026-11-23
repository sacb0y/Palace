# PR #200 tip f98fe11 (0.3.114) — FENRIR-STUDIO harness retest

**Host:** FENRIR-STUDIO · **DPI:** 150% (windowDpi=144) · client 1280×900 DIP  
**Identity:** PalaceOrganizeTest-5E1F only (never C20A7629)  
**Build:** Palace-200 @ f98fe11 / 0.3.114 Debug x64  
**Change:** ui-tests `BtnCreateUngroupedTag` → `BtnCreateNewTags` (+ nest path)

## Checklist

| Check | Verdict | Evidence |
|-------|---------|----------|
| `-SelfTestDesktopGate` | **PASS** | selftest-f98fe11.out — temp audit only; real `%LOCALAPPDATA%\PalaceTestAudit` absent |
| Identity gate | **PASS** | `PalaceOrganizeTest-5E1F 0.3.114.0` |
| DPI + XamlRoot client gate | **PASS** | xamlroot-f98fe11.log (1200→1280 @ 1.5); 01-dpi-1280x900-f98fe11.png |
| S7 create drop group + chip | **PASS** | (not in fail list; BtnCreateNewTags works) |
| S7 Tags drag hover/SendInput/probe | **FAIL** | `hover failed for BtnTagChip_UiD55d4e4`; probe empty |
| Stock ui-tests suite | **FAIL** 82 pass / 38 fail | ui-tests-f98fe11.out (was 69/51 on 800a310) |

## Notes

- Fenrir/C20A **not** touched.
- `BtnCreateNewTags` fix unblocked create/chip path; drag still fails at hover (chip selector/visibility), not at create-button AutomationId.
