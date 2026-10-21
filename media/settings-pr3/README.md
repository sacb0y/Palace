# Settings PR #123 composition preview screenshots (media-only)

Packaged Debug tip `c6b0145ba80b538080535b61a9e35842bc993a5c` (0.2.9), branch `cursor/settings-composition-preview-384a` / PR #123.
Hosted on this media branch — **not** on the product tip. Pin raw URLs to this media commit SHA.

Test identity: `PalaceSettingsPr3-123` (never C20A7629). Isolated package LocalState + temp SFW only.

| File | Caption |
|------|---------|
| `01-composition-preview-mock-chrome.png` | Appearance 16:9 composition preview (~280 DIP) with mock nav strip and card over the live wallpaper. |
| `02-thumbs-sharp-mid-drag.png` | Preset thumbs stay sharp while Blur/Darkness update live (no thumb flicker / re-decode). |
| `03-preview-live-blur-darkness.png` | Composition preview reflects live Blur and Darkness (GaussianBlur then dim) matching the shell. |
| `04-selected-entry-numberbox.png` | Selected Ribbons entry with Darkness/Blur sliders and NumberBoxes (Reset beside each). |

Companions `01b`/`02b`/`03b`/`04b` are wider card/window crops of the same session.