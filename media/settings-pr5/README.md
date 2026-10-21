# Settings PR #125 Viewer + Input screenshots (media-only)

Packaged Debug tip `5fdadbf` (0.2.11), branch `cursor/settings-viewer-input-f042` / PR #125.
Hosted on this media branch — **not** on the product tip. Pin raw URLs to this media commit SHA.

Test identity: `PalaceSettingsPr5-125` (never C20A7629). Isolated `PALACE_DATA_DIR` + temp SFW only.
Build: BelowNormal `-m:1` if Unity is busy; defer if needed.

| File | Caption |
|------|---------|
| `01-viewer-overview.png` | Viewer: Default zoom (Fit), HDR and tonemap expander, Show Details by default, Slideshow placeholder, Loop video. |
| `02-hdr-expander.png` | HDR and tonemap expander open: peak override + nits slider, SDR tonemap info card. |
| `03-input-shortcuts-touch.png` | Input: shortcut key chips, Touch gestures, Swipe down to dismiss. |

## Capture notes

1. `Prepare-TestIdentity.ps1 -IdentityName PalaceSettingsPr5-125`
2. Register **build** `bin\...\AppxManifest.xml` only — never `winapp run --no-launch` on source, never C20A7629
3. Navigate Settings → Viewer / Input via UIA (`BtnSettingsNav_Viewer` / `BtnSettingsNav_Input`)
4. Expand `CardHdr` for shot 02
5. Screenshot the test window only (do not maximize)
