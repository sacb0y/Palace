# FenrirDesktop UI shots — PR #207 (3D groundwork)

| Field | Value |
|---|---|
| PR | https://github.com/sacb0y/Palace/pull/207 |
| Tip (Fenrir build) | `1a8f72322051830cfe162018e8c73a2988666383` |
| Package | PalaceOrganizeTest-5E1F `0.3.121.0` |
| ProductVersion | `0.3.121+1a8f72322051830cfe162018e8c73a2988666383` |
| Host | FenrirDesktop (no DPI change) |
| Override | `-AllowDesktop -OverrideReason 'FENRIR-STUDIO locked overnight; #207 UI shots'` |
| Data | fresh `C:\Users\iadag\AppData\Local\Temp\palace-agent-data-207-fresh` / SFW `C:\Users\iadag\AppData\Local\Temp\palace-sfw-207-fresh` |
| Gate | n=4 (Image/Gif/Video/Model3D) — PASS |
| Capture | HWND PrintWindow vs 5E1F PID |

## Verdict: **PASS**

| Shot | Result | Notes |
|---|---|---|
| 01-settings-index-3d-off.png | PASS | Formats expanded; 3D toggle Off visible |
| 02-settings-index-3d-on.png | PASS | 3D toggle On visible |
| 03-library-mosaic-badges.png | PASS | Model3D cube + video + GIF badges beside still |
| 04-library-overlay-3d-placeholder.png | PASS | model-207.glb · Library overlay 3D preview placeholder |
| 05-tags-overlay.png | PASS | Tags nav (tag editor page) |
| 06-gallery-window.png | PASS | **Separate GalleryWindow** HWND + `GrdGalleryWindow`; title `still-207.png` (not main) |
| 07-library-still.png | PASS | still-207 in Library overlay viewer |
| 08-tags-still.png | PASS* | Still + Info panel; Tags nav is editor not mosaic |
| 09-gallery-still.png | PASS | **Separate GalleryWindow** still-207.png |
| 10-library-video.png | PASS | video-207 Library overlay + transport |
| 11-tags-video.png | PASS* | Video + Info panel (not Tags-nav mosaic) |
| 12-gallery-video.png | PASS | **Separate GalleryWindow** video-207.mp4 + controls |

\* Tags route is a tag editor. Still/video “Tags” evidence uses viewer Info Tags section.

### GalleryWindow correction
Earlier pack claimed PASS for 06/09/12 on the **inline Library overlay** (same HWND as main). That fails Wimbly’s separate-GalleryWindow requirement. Recaptured via mosaic **More → Open in new window**, gated on `hwnd ≠ main` and UIA `GrdGalleryWindow`.

Captions:
- 06: Separate GalleryWindow still-207.png HWND=44383692 title=[still-207.png] tip=1a8f72322051830cfe162018e8c73a2988666383 GrdGalleryWindow=YES separate-from-main=18484308.
- 09: Separate GalleryWindow still-207.png HWND=44383692 title=[still-207.png] tip=1a8f72322051830cfe162018e8c73a2988666383 GrdGalleryWindow=YES separate-from-main=18484308.
- 12: Separate GalleryWindow video-207.mp4 + controls HWND=21902736 title=[video-207.mp4] tip=1a8f72322051830cfe162018e8c73a2988666383 GrdGalleryWindow=YES separate-from-main=18484308.

## Steps
1. Seed SourceFolder → SFW synthetic (still/gif/video/glb)
2. Settings → Library → Formats → Index 3D OFF/ON
3. Scan; sqlite gate n=4 + Model3D
4. Library mosaic / overlay still+video / 3D placeholder
5. GalleryWindow via Open in new window (still + video)

Do not merge.
