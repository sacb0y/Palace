# Preview dock — FENRIR-STUDIO retest (tip 9069682 / 0.3.64)

**Machine:** FENRIR-STUDIO (win-laptop) — not FenrirDesktop  
**Product tip:** `9069682ee89e6f8af5dd7c07d4c7b5e2b066af80` on `cursor/tags-sidebar-fidelity-b59f` (includes `b430024` / 0.3.63 ColPreview spacer + right-align)  
**Identity:** `PalaceStudioShot-063` — never C20A7629  
**Data:** synthetic SFW `%TEMP%\palace-sfw-studio-063`  
**DPI:** 150% (dpi=144); work phys ~1600×1019  

## Verdict — **FAIL** (height-only)

| Check | Result |
|---|---|
| Height-only @~1024 ON+selected | **FAIL** — prev.x 1788→406 (delta=-1382) over tree/mosaic |
| Height-only @~1400 ON+selected | **FAIL** — prev.x 1854→406 (delta=-1448) |
| Width drag | **PASS** |
| ≥5 toggles @1024 final ON+OFF | **PASS** |
| Cycles @900 / @1920 (clamped) | **PASS** |
| Notes @1024 ON | **PASS** |

Same failure mode as `720095e` / `513c6b8`. `b430024` did **not** fix height-only on this laptop under SetWindowPos height-only resize.

## Files
See `*.caption.txt` siblings and PNGs in this folder.
