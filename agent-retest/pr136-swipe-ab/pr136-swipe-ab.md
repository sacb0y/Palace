# PR #136 swipe-drag A/B — FenrirDesktop / Main-Desktop

**When:** 2026-10-23  
**Machine:** FenrirDesktop (Main-Desktop worker), light load (`BelowNormal`, `dotnet -m:1`)  
**Identity:** `PalaceSwipeAb-136` only — never `C20A7629`  
**Data:** temp SFW stills under package LocalState `sfw-media` (isolated catalog)  
**Unity:** left alone; touch-probe attempt hung and was **aborted** (no further injection)

## Tips

| Label | SHA | Notes |
|---|---|---|
| **A** (known-good pre-rebase) | `cebc6f16b3a8547b175c72408df842d83d2ffc63` | packaged Debug |
| **B** (current tip) | `5854578ba7aea183e2305492373dfb4f1a720022` | LibraryPage layout + swipe hooks restack |

## Method (identical for every cell)

1. Open `tall-blue.png` overlay.
2. Fit: `BtnGalleryScaleFit`. Non-Fit: `BtnGalleryScaleActual`.
3. Window-guarded `winapp ui drag` across `ScrStill` BoundingRectangle (~78%→43% width, mid Y). Points asserted inside Palace HWND + `WindowFromPoint` PID == test app before inject.
4. Advance criterion: **center ROI SHA256** of still content (not full-window hash — chrome/Info toggles false-positive). Tip B Fit also confirmed via Info card **filename + `3 / 4`** unchanged.

## Comparison table

| | Fit still | non-Fit (Actual) |
|---|---|---|
| **A `cebc6f1`** | **FAIL** — ROI identical (tall-blue) | **FAIL** — ROI identical |
| **B `5854578`** | **FAIL** — still `tall-blue.png` **3/4** | **FAIL** — ROI identical |

## Verdict

**Test artifact — not a LibraryPage Loaded/Unloaded regression.**

Fails on **both** A and B with the same guarded `winapp ui drag` path. Full-window PNG hashes often differ while the still ROI does not (auto-hide / Info chrome). Do **not** count the prior tip-B FAIL as product breakage from the restack; do **not** change product code for this alone.

Tip B commit `5854578` still keeps `HookOverlayStillGestures()` on Loaded alongside layout Hook/Unhook — consistent with “hooks present,” while mouse drag does not drive Manipulation/InteractionTracker paging.

## Better check (proposed)

1. **Touch injection + window guard:** `winapp ui touch ScrStill --gesture swipe --direction left --distance ≥35% width` with `--at`/`--to-point` (or element-centered) after HWND/`WindowFromPoint` PID checks. Prefer FENRIR-STUDIO if FenrirDesktop Unity load makes touch hang (probe here hung; aborted).
2. **UIA oracle:** open Details/Info and assert Automation-visible filename or index (`N / M`) before/after — tip B Fit Info already showed `3 / 4` as ground truth.
3. Optional smoke: `BtnGalleryNext` / keyboard Next proves navigation works; does not prove swipe gesture.

## Product / CI

- **No product fix** (artifact). Tip remains `5854578`. Version stays **0.3.7**. Squash-merge note unchanged.
- **CI:** prior tip green run cited on PR: https://github.com/sacb0y/Palace/actions/runs/39960432686  
- **No merge** performed.

## Media

Store: `media/pr136-swipe-ab/`  
Also: `%TEMP%\palace-pr136-swipe-ab-shots\` and `cursor/agent-retest-media-tmp/pr136-swipe-ab/`

| Path | Caption gist |
|---|---|
| `A-cebc6f1/*-fit-*.png` | A Fit before/after — no advance |
| `A-cebc6f1/*-nonfit-*.png` | A Actual before/after — no advance |
| `B-5854578/*-fit-*.png` | B Fit; after still 3/4 tall-blue |
| `B-5854578/*-nonfit-*.png` | B Actual — no advance |
