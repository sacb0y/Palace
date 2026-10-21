# Settings PR #124 Library & Folders screenshots (media-only)

Packaged Debug tip `142f933c5eb868b2b2d216ced37b35f476ea32e0` (0.2.10), branch `cursor/settings-library-folders-f22f` / PR #124.
Hosted on this media branch — **not** on the product tip. Pin raw URLs to this media commit SHA.

Test identity: `PalaceSettingsPr4-124` (never C20A7629). Isolated package LocalState + temp SFW only.
Shot 04 proves background apply via UIA `SelectionItem.Select` (SelectionChanged), not pointer ItemClick.

| File | Caption |
|------|---------|
| `01-library-folders-overview.png` | Library & Folders overview: Watched folders expander with seeded path, Open/Remove/Add, plus collapsed Scanning / Thumbnail / Formats / Cloud. |
| `02-watched-folders-expander.png` | Watched folders expander: path row with Open and Remove, plus Add folder in the header. |
| `03-scanning-thumbnail-formats.png` | Scanning (Watch for changes + Scan now), Thumbnail cache (build while scanning), and Formats (Images/Video on; 3D/Audio placeholders). |
| `04-uia-select-applies-ribbons.png` | UIA SelectionItem.Select on Ribbons applies selection (checkmark) via SelectionChanged — same path as keyboard/Narrator Select. |

Companions `03a`/`03b`/`03c` and `04a`/`04b`/`04c` are detail crops / before-after of the same session.
