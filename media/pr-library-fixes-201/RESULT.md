# PR #201 tip 1cdad86 (0.3.115) — FENRIR-STUDIO evidence

**Host:** FENRIR-STUDIO · **DPI:** 150% (windowDpi=144) · not maximized
**Identity:** PalaceOrganizeTest-5E1F only (never C20A7629)
**Lib:** synthetic %TEMP%\palace-sfw-four-bug (3204 assets)
**Build:** Palace-four-201 @ 1cdad86 / 0.3.115 Debug x64

## Checklist

| # | Check | Verdict | Evidence |
|---|-------|---------|----------|
| 1 | Folder pick thumbs no resize; ~6 widths no vanish | PASS | 01-thumbs-after-folder-pick.png, 01b, 01c, 01-resize-01..06 |
| 2 | Folder tree mouse-wheel scroll | PASS | 02-tree-before-wheel.png -> 02-tree-after-wheel.png |
| 3 | Arrows move tiles after folder pick | PASS | 03b-before/after-arrows.png (img-0000 -> img-0006) |
| 4 | Exit select: no idle more-menu | PASS | 04-after-drag-select.png -> 04-exit-select-no-more.png |

Synced via .cursor/stores + AgentStores files/media (same layout as pr2-bugs-polish-199).

## Arrow recheck tip 7c2cafa (blocker fix)

| Path | Verdict | Evidence |
|------|---------|----------|
| TREE click then arrows | PASS (img-0001 -> img-0008) | 03-tree-before/after-arrows.png |
| BreadcrumbBarItem then arrows | PASS (art-a -> img-0000) | 03-breadcrumb-before/after-arrows.png |

**GATE: PASS** for Fenrir C20A7629 #201 install (Studio did not touch Fenrir/C20A).
