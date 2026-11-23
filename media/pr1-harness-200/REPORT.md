# PR #200 tip a09e8c7 (0.3.114) — FENRIR-STUDIO harness retest (S7 named SKIP)

**Host:** FENRIR-STUDIO · **DPI:** 150% · client 1280×900 DIP  
**Identity:** PalaceOrganizeTest-5E1F only (never C20A7629)  
**Build:** Palace-200 @ a09e8c7 / 0.3.114 Debug x64  
**Lib:** same seeded palace-agent-data-pr200 (8 assets)

## Checklist

| Check | Verdict | Evidence |
|-------|---------|----------|
| `-SelfTestDesktopGate` | **PASS** | selftest-a09e8c7.out — temp audit only |
| Identity 5E1F 0.3.114.0 | **PASS** | ui-tests-a09e8c7.out |
| DPI + XamlRoot gate | **PASS** | xamlroot-a09e8c7.log; 01-dpi-1280x900-a09e8c7.png |
| S7 create drop group + chip | **PASS** | (not failed) |
| S7 hover + SendInput | **SKIP** (named) | `tag chip reports zero size under UIA; tracked for ui-tests catch-up PR` |
| **#200-attributable fails** | **0** | All 38 suite fails also fail on main `f05e548` (pre-existing) |
| Stock ui-tests | 81 pass / 1 skip / 38 fail | ui-tests-a09e8c7.out |

## Attribution

Compared fail names to main `f05e548` stock suite (69/48). Intersection = 38. Tip-only fails = **none**. Former tip-only S7 hover fail is now a **named SKIP** (not a silent pass).

Fenrir/C20A not touched.
