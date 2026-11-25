# FenrirDesktop UI shots — PR #207 (3D groundwork)

| Field | Value |
|---|---|
| PR | https://github.com/sacb0y/Palace/pull/207 |
| Tip | `1a8f72322051830cfe162018e8c73a2988666383` |
| Package | PalaceOrganizeTest-5E1F `0.3.121.0` |
| ProductVersion | `0.3.121+1a8f72322051830cfe162018e8c73a2988666383` |
| Host | FenrirDesktop (no DPI change) |
| Override | `-AllowDesktop -OverrideReason 'FENRIR-STUDIO locked overnight; #207 UI shots'` |
| Data | fresh `C:\Users\iadag\AppData\Local\Temp\palace-agent-data-207-fresh` / SFW `C:\Users\iadag\AppData\Local\Temp\palace-sfw-207-fresh` |
| Gate | n=4 (Image/Gif/Video/Model3D) — PASS |
| Capture | HWND PrintWindow vs 5E1F PID |

## Verdict: **PASS** (with notes)

| Shot | Result | Notes |
|---|---|---|
| 01-settings-index-3d-off.png | PASS | Formats expanded; 3D toggle Off visible |
| 02-settings-index-3d-on.png | PASS | 3D toggle On visible |
| 03-library-mosaic-badges.png | PASS | Model3D cube + video + GIF badges beside still |
| 04-library-overlay-3d-placeholder.png | PASS | model-207.glb · 3D preview placeholder |
| 05-tags-overlay.png | PASS | Tags nav (tag editor page) |
| 06-gallery-window.png | PASS | Inline Library viewer (no separate Gallery HWND on this tip) |
| 07-library-still.png | PASS | still-207.png in Library viewer |
| 08-tags-still.png | PASS* | Still + Info panel (Tags section in Info); not Tags-nav media browser |
| 09-gallery-still.png | PASS | Same inline viewer as 06/07 |
| 10-library-video.png | PASS | video-207.mp4 + transport controls |
| 11-tags-video.png | PASS* | Video + Info/Tags panel; not Tags-nav media browser |
| 12-gallery-video.png | PASS | video-207.mp4 + controls (inline viewer) |

\* Tags route is a tag editor, not an asset mosaic. Still/video “Tags” evidence is the viewer Info panel Tags section.

## Steps
1. Seed SourceFolder → SFW synthetic (still/gif/video/glb) under temp only
2. Settings → Library → expand Formats header button → Index 3D OFF/ON
3. Scan; sqlite gate n=4 + Model3D=1
4. HWND Library mosaic / 3D placeholder / still / video (+ Info Tags panel)

Do not merge.
