# #196 Wimbly follow-up — FENRIR-STUDIO (`276087a` / 0.3.84)

| Field | Value |
|---|---|
| **Tip SHA** | `276087a0f42e0c784432cc0d0e4906dd3b0753af` |
| **Tip version** | 0.3.84.0 |
| **Main proof** | 0.3.81.0 (`palace-organize-194-081` layout / `be103dd` lineage) |
| **Host** | FENRIR-STUDIO |
| **DPI** | 144 → **150%** PerMonitorV2 |
| **Window** | Not maximized (~1053×666 DIP / 1580×999 phys) |
| **Identity** | `PalaceOrganizeTest-5E1F` (never C20A7629) |
| **FenrirDesktop** | Not touched |

## A) Thumbnail proof — **not a 0.3.84 decode regression**

| Evidence | Path |
|---|---|
| Same library on **main/0.3.81** | `06-main-081-same-library-thumbs.png` |
| Same library on **tip/0.3.84** idle | `07-tip-084-idle-mosaic.png` |
| Source file (solid fill) | `06-proof-sfw-art-a-source.jpg` (`art-a.jpg`, 320×240, center `#9FB4C7`) |
| Cached thumb present | `06-proof-cached-thumb.jpg` under package `LocalState\thumbs\` |

**Verdict:** Blank grey tiles are **synthetic SFW solid-color JPEGs** under `palace-sfw-fenrir-gate` (distinct muted fills: blue-grey / taupe / green / pink rejected). Thumb cache exists; 0.3.81 and 0.3.84 mosaics match that look. **Not** a tip decode failure.

Note: on 0.3.81, ⋮ is **always visible** on every tile (pre-gate). On 0.3.84 idle, ⋮ is hidden — expected.

## B) Flyout — **PASS**

| File | Result |
|---|---|
| `05b-flyout-bottom-edge-aligned-right.png` | Context menu opens from ⋮, drops **down** (`MnuOpen` verified). Placement matches BottomEdgeAlignedRight intent. |

## C) Scroll-recycle — **PASS**

| File | Result |
|---|---|
| `08a-hover-before-scroll.png` | Hover shows ⋮ on `art-a.jpg` |
| `08b-after-scroll-recycle-no-stuck.png` | After hover → clear focus to `NavLibrary` → scroll recycle → mouse off tiles: **no stuck ⋮** (focus confirmed on Library nav) |

## D) Keyboard focus — **PASS** (Tab optional incomplete)

| File | Result |
|---|---|
| `09-keyboard-focus-ring.png` | `winapp ui focus art-a.jpg` — focus ring + ⋮; mouse off tile |
| `09b-tab-focus-optional.png` | Tab from nav did not reach `ItemContainer` within 15 stops (landed on Select / tree chrome). Prefer `09` as focus proof. |

## Overall

Studio proof for Wimbly items A–C on tip **0.3.84 / `276087a`**: thumbs explained, flyout down from ⋮, recycle clean. Ready for Wimbly/Doktor visual OK. **Do not merge** from this worker.
