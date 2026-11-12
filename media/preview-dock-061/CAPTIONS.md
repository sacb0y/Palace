# Preview dock — FENRIR-STUDIO retest (tip 720095e / 0.3.62)

**Machine:** FENRIR-STUDIO (win-laptop) — not FenrirDesktop  
**Product tip:** `720095e` on `cursor/tags-sidebar-fidelity-b59f` (includes height fix `513c6b8` / 0.3.61 + Tags icon crop)  
**Identity:** `PalaceStudioShot-061` — never C20A7629  
**Data:** synthetic SFW `%TEMP%\palace-sfw-studio-061` (12 tiles)  
**DPI:** 150% (windowDpi=144); work area phys ~1600×1019 → max restored ~1053 DIP wide  

## Verdict

| Check | Result |
|---|---|
| Height-only @~1024 with Preview ON + selected | **FAIL** — prev.x 1788→406 over folder tree / mosaic |
| Height-only @~1400 with Preview ON + selected | **FAIL** — same jump to prev.x≈406 |
| Width drag corrects / stays docked | **PASS** |
| ≥5 toggles @~1024; final ON+OFF | **PASS** |
| Cycle @~900 and @~1920 (1920 clamped to work area) | **PASS** |
| Notes reachable @1024 ON + selected | **PASS** (TxtNotes offscreen=false) |
| Never over folder tree (steady docked / after width) | **PASS** when not after height-only |
| Grid reflows beside opaque dock | **PASS** when docked correctly |

**Bottom line:** #193 height-only regression **still reproduces on FENRIR-STUDIO** at tip `720095e` despite `513c6b8`. Width drag recovers. Do not merge on this evidence alone.

## Captions

| File | Caption |
|---|---|
| `01-height-1024-before.png` | BEFORE height-only ~1024; Preview ON+selected; docked right |
| `02-height-1024-after-FAIL.png` | AFTER height-only — Preview jumped over mosaic/tree (**FAIL**) |
| `03-height-1400-before.png` | BEFORE height-only ~1400 |
| `04-height-1400-after.png` | AFTER height-only ~1400 (**FAIL**) |
| `05-width-before.png` / `06-width-after-corrects.png` / `07-width-restore.png` | Width drag corrects docking |
| `08-1024-final-ON.png` / `09-1024-final-OFF.png` | After 5 toggles |
| `10-900-ON/OFF.png` | ~900 cycle |
| `10-1920-ON/OFF.png` / `11-*-OFF` | ~1920 target (clamped ~1053 DIP) |
| `12-1024-ON-selected.png` / `13-1024-ON-selected-notes.png` | Selected + Notes reachable |
