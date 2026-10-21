# Settings PR #126 Performance + About screenshots (media-only)

Packaged Debug tip `c16278e` (0.2.12), branch `cursor/settings-perf-about-16fa` / PR #126.
Hosted on this media branch - **not** on the product tip. Pin raw URLs to this media commit SHA.

Test identity: `PalaceSettingsPr6-126` (never C20A7629). Isolated package LocalState + temp SFW only.
Build: BelowNormal `-m:1` if Unity is busy; defer if needed.

| File | Caption |
|------|---------|
| `01-performance-storage.png` | Performance: Media cache usage + Clear, Catalog location, GPU & preview (Thumbnail decode Auto, Preview quality Standard). |
| `02-about-export-import-reset.png` | About: Version/Copy, Preview channel, licenses/logs, Export/Import settings, Export catalog, Reset all settings. |
| `03-viewer-hdr-collapsed-fullwidth.png` | Viewer: HDR expander collapsed - SettingsCards stay full-width; Show Details / Loop switches stay on the right (no wrap under text). |
| `04-viewer-hdr-expanded.png` | Viewer: HDR and tonemap expander open - peak override + SDR tonemap; sibling cards remain full width. |

## Capture notes

1. `Prepare-TestIdentity.ps1 -IdentityName PalaceSettingsPr6-126`
2. Register **build** `bin\...\AppxManifest.xml` only - never `winapp run --no-launch` on source, never C20A7629
3. Navigate Settings → Performance / About / Viewer via UIA (`BtnSettingsNav_Performance` / `_About` / `_Viewer`)
4. Expand `CardGpuPreview` (click expander button) for shot 01 prefs; keep `CardHdr` collapsed for shot 03; expand for shot 04
5. Screenshot the test window only (do not maximize)