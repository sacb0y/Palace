# Preview dock fix — FenrirDesktop captions (0.3.58 → 0.3.59) — Wimbly recapture

**Machine:** FenrirDesktop  
**PR:** https://github.com/sacb0y/Palace/pull/193  
**Mockup:** no mockup, written contract (grid always reflows beside panel; panel opaque).  
**Identities:** `PalacePreviewDockBefore-059` (0.3.58) / `PalacePreviewDockAfter-059` (0.3.59) — never C20A7629.  
**Data:** synthetic SFW PNGs in `%TEMP%\palace-sfw-preview-dock-059` (12 tiles).  
**Capture:** ToggleState read/assert via UIA before every captioned shot (fixes inverted ON/OFF).

## Before (tip main 0.3.58) — unchanged from first capture

| File | Caption |
|---|---|
| `before-058-01-preview-off-1024.png` | ~1024 not maximized — baseline |
| `before-058-02-preview-on-empty-1024.png` | **BUG:** Preview ON empty — acrylic Overlay; thumbs show through; grid does not shrink |
| `before-058-03-preview-on-selected-1024.png` | **BUG:** Preview ON — Tags/Notes over mosaic; semi-transparent panel |
| `before-058-04-resize-*.png` | Resize sweep Preview ON |
| `before-058-05-maximized.png` | Maximized Preview ON |
| `before-058-06-restored-1024.png` | Restore after maximize ~1024 Preview ON |
| `before-058-07-launch-preview-open-1024.png` | Relaunch with Preview already open ~1024 |

## After (0.3.59) — Wimbly recapture (toggle-verified)

| File | Caption |
|---|---|
| `after-059-01-preview-off-1024.png` | Preview OFF (ToggleState=Off) ~1024. **Thin right-edge chevron is intentional** (`BtnPreviewPaneEdge` reopen affordance when pane hidden). |
| `after-059-02-preview-on-empty-1024.png` | Preview ON empty — opaque docked ColPreview; mosaic reflows (1-col) beside panel ~1024 |
| `after-059-03-preview-on-selected-1024.png` | Preview ON + **asset selected** — image in dock, Tags/Notes opaque, grid reflowed ~1024 |
| `after-059-03-preview-on-selected-1920.png` | Preview ON + **asset selected** — image in dock, Tags/Notes opaque, grid reflowed ~1920 |
| `after-059-04-resize-900.png` | Resize ~900 Preview ON |
| `after-059-04-resize-1200.png` | Resize ~1200 Preview ON |
| `after-059-04-resize-1600.png` | Resize ~1600 Preview ON |
| `after-059-04-resize-1920.png` | Resize ~1920 Preview ON |
| `after-059-05-maximized.png` | Maximized Preview ON |
| `after-059-06-restored-1024.png` | Restore after maximize ~1024 Preview ON |
| `after-059-07-launch-preview-open-1024.png` | Relaunch with Preview open ~1024 |

## Thin right-edge handle

Intentional. `BtnPreviewPaneEdge` stays at the window right when Preview is hidden so hover/touch can reopen the pane. Not a leftover acrylic strip.

## FENRIR-STUDIO

Not run. Standing rule: do **not** fall back to FenrirDesktop for laptop retest. Studio Palace-mount gap raised with Isiac.
