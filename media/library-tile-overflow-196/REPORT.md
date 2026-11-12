# #196 Library mosaic ⋮ overflow — FENRIR-STUDIO retest (0.3.82)

| Field | Value |
|---|---|
| **Verdict** | **PASS** |
| **Build SHA** | `71eaf208530c0d0393aab0e6edaa842cc064f054` |
| **Version** | 0.3.82 |
| **Host** | FENRIR-STUDIO |
| **DPI** | 144 → **150%** PerMonitorV2 |
| **Window** | Not maximized (~1053×666 DIP / 1580×999 phys, clamped to work area) |
| **Identity** | `PalaceOrganizeTest-5E1F` 0.3.82.0 (never C20A7629) |
| **Input** | Real `winapp ui hover` / `focus` / `click` (SendInput) after ForceForeground |
| **FenrirDesktop** | Not touched |

## Checklist

| # | Capture | Expected | Result |
|---|---|---|---|
| 1 | `01-idle-grid-no-overflow.png` | Idle grid — no ⋮ | **PASS** |
| 2 | `02-hover-tile-overflow.png` | Hover one tile — ⋮ shown | **PASS** |
| 3 | `03-keyboard-focus-overflow.png` | Keyboard focus on tile — ⋮ shown | **PASS** (`get-focused` = `art-a.jpg`) |
| 4 | `04-selected-tile-overflow.png` | Selected tile — ⋮ shown | **PASS** (select mode + checked tile; idle tiles no ⋮) |
| 5 | `05-flyout-open-overflow.png` | Flyout open — ⋮ shown | **PASS** (ContextFlyout with Open / …; `MnuOpen` present) |

## Notes

- Tip rebuilt after CI fix (`f410da2` → `71eaf20`). Packaged Debug register to `%LOCALAPPDATA%\winapp-layout\palace-organize-196-082-debug-x64`.
- UIA still enumerates `BtnAssetMore` when Opacity=0; verdict is visual from screenshots.
- Do not merge — Isiac + Doktor review.
