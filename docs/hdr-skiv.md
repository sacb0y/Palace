# HDR / SKIV — viewer plan

**Recommendation: do not port SKIV.** Use it as a UX and color-science reference only. Grow Palace’s existing overlay and `GalleryWindow` in packaged WinUI 3. No ImGui host, no Direct3D viewer process, no Special K / MinHook, no gamedev slice.

HDR viewing is **part of making image viewing solid**. It lives in **Library + tagging (0.0.x)**, not a later slice. Isiac’s order: Library + tagging now (image / video viewing solid; tagging good) → Rooms moodboard / mindmap next → Cloud bonus → gamedev formats → AI last. Do not wait on Rooms or Cloud to keep overlay / gallery honest. Magick.NET / new EXR-class extensions still wait on packaging, not on Rooms. This plan does not change scan, hydrate, On-Demand, tagging, or Rooms.

Magick.NET / TGA / EXR / Radiance HDR / PSD stay **deferred until packaging is clean**. SKIV’s native decode stack (OpenEXR, libjxl, libavif, Ultra HDR, DirectXTex) has the same class of MSIX problem. Do not start a large Magick port from this evaluation.

Evaluated [SpecialKO/SKIV](https://github.com/SpecialKO/SKIV) at **0.0.9** (MIT; Aemony / Andon “Kaldaien” Coleman). Default branch reviewed: `capture_rotated`. Source reviewed: README, `version.h`, `include/SKIV.h`, `include/tabs/viewer.h`, `include/utility/image.h`, `include/utility/registry.h`, plus the `src/` / `include/` layout (`SKIV.cpp`, `tabs/viewer.cpp`, `utility/image.cpp`, `DirectXTexEXR.cpp`). Product copy also from [special-k.info](https://special-k.info/) and the SKIV installer blurb.

---

## What SKIV is

SKIV (Special K Image Viewer) is a **standalone Win32 HDR viewer, snipping tool, and encode/transcode bench**. It is an experimental companion to [SKIF](https://github.com/SpecialKO/SKIF) and a testbed for Special K’s HDR screenshot path.

It is **not** a catalog. It opens one image (or a capture), walks the parent folder with Explorer-style sort, and can save/transcode. There is no project partition, no stable asset ID, no mosaic, no tags, no Rooms.

**Stack (locked to SKIV, incompatible with Palace):**

- Native C++ Visual Studio project (`SKIV.vcxproj`), not .NET / WinUI
- Dear ImGui UI + D3D11 presentation
- DirectXTex + optional OpenEXR, libjxl, libavif, Ultra HDR
- MinHook, gamepad thread, registry under `SOFTWARE\Kaldaien\Special K\Viewer\`
- Global capture hotkeys (`Ctrl+Win+Shift+…`), tray, updater

**What it is good at (product):**

- **Real HDR present**, not “decode then squash to sRGB.” Default HDR path is scRGB 16 bpc; HDR10 (10 bpc) is an option. SDR fallback is explicit (8 / 10 / 16 bpc).
- **Tonemap you can name:** none (let the display), clip, infinite rolloff (`x/(1+x)`), normalize to content CLL, map CLL to the display. Default maps content to display. Optional 99th-percentile MaxCLL.
- **Visualization modes:** none, luminance heatmap, gamut, SDR-only (with flags for luminance / gamut / overbright). Cycle from the viewer.
- **Viewer chrome:** 1:1 / Fit / Fill, fullscreen, image details (`Ctrl+D`), folder next/prev with Explorer column sort, copy/paste that keeps HDR (AVIF/PNG).
- **Format breadth:** Ultra HDR JPEG, AVIF, JPEG XL, JPEG XR, OpenEXR, Radiance `.hdr`, HDR PNG, JPEG, WebP, PSD, GIF (no animation), BMP, TIFF, DDS.
- **Capture + encode:** desktop / region / window snip; write JPEG XR, JPEG XL, AVIF, or PNG with quality / bit-depth settings. HDR→SDR tonemap on save.

Palace already decided the opposite identity: **files stay on disk, the catalog stores IDs, thumbs are SDR JPEGs, Open hydrates.** SKIV is a specialist viewer. Palace is a DAM that must stay interactive and packaged.

---

## Reuse vs reference

SKIV does **not** fit the Palace stack. A port would mean rewriting a ~180 KB ImGui viewer plus a ~180 KB image pipeline into WinUI, or hosting SKIV as a child process. Both break the locked `winui-mvvm` rule (CommunityToolkit.Mvvm, `{x:Bind}`, ThemeResource, AutomationIds, `winapp ui`). MinHook and Special K injection are out of bounds for a packaged Store-shaped app.

MIT allows copying if we kept the notice. That is not the blocker. The blocker is architecture and packaging.

| SKIV piece | Palace use |
|---|---|
| ImGui + D3D11 viewer (`src/SKIV.cpp`, `tabs/viewer.cpp`) | **Do not copy.** Rewrite cost is a new app. |
| MinHook, SKIF/Special K load, gamepad, updater, tray | **Do not copy.** |
| Desktop / region / window capture + global hotkeys | **Do not copy.** Palace is not a snipping tool. |
| Encode / transcode (AVIF / JXL / JXR / HDR PNG writers) | **Do not copy.** Catalog + preview first. |
| OpenEXR + libjxl + Ultra HDR native DLLs | **Do not vendor.** Same MSIX risk as Magick. |
| libavif (`avif.dll` via `Starward.Codec`) | **Landed for HDR AVIF present.** Strip other Starward natives (JXL/UHDR/VP9). |
| DirectXTex HDR / TGA I/O (MIT) | **Idea only.** A later decoder spike may use this class of library *if* it packages; not a reason to start now. |
| PQ / ICtCp / Rec.709↔2020 / P3 matrices (`image.h`) | **Color-science reference** when a SwapChainPanel path exists. Reimplement; do not paste the ImGui app. |
| Tonemap types + heatmap / gamut / SDR viz | **UX reference** for overlay / gallery, after a real HDR present exists. |
| 1:1 / Fit / Fill, details pane, folder next/prev | **UX reference.** Next/prev already exists; scaling + details do not. |
| HDR clipboard copy/paste | **Do not copy yet.** Palace clipboard must not recall On-Demand files (`AccessService.FilterCopyPaths`). |
| “Open this file in SKIV” | **Later optional.** External tool, not a Palace feature foundation. |

**No foundation PR in the Palace repo from this evaluation.** Do not add unused HDR columns, do not widen `PathSafe.ImageExt`, do not reference Magick.NET.

---

## What Palace image viewing is today

The original plan said preview is **WIC** for jpg/png/webp/bmp/tif, GIF, video; Magick.NET for TGA/EXR/HDR/PSD *if packaging stays clean*. The checkout has the WIC path only.

**Already true (keep):**

- Catalog extensions: `.jpg` `.jpeg` `.png` `.webp` `.bmp` `.tif` `.tiff` `.avif`, `.gif`, `.mp4` `.mov` `.mkv` `.webm` `.avi` (`Helpers/PathSafe.cs`)
- Mosaic and overlay never `File.Exists` / `ImageDimensions.TryRead` on the original while building tiles
- Thumbs: WIC `BitmapDecoder` → JPEG, `ColorManagementMode.ColorManageToSRgb` (`ThumbnailService`)
- Overlay + `GalleryWindow`: `BitmapImage` / `MediaPlayerElement`, `Stretch="Uniform"`, title/tags **below** the player
- Open is double-click / Enter / context Open; hydrate only then
- Online-only tiles use provider thumbs or `ShowCloudTile`

**Why HDR files “are not supported”:**

- `.hdr` `.exr` `.tga` `.psd` `.jxl` `.jxr` are not catalog extensions, so scan skips them
- `.avif` is catalogued; WIC decodes it when the HEIF/AV1 codecs are present. Header size / CICP come from `AvifFile` (`ispe` / `colr` `nclx`)
- `ImageDimensions` understands PNG / JPEG / GIF / BMP / WebP / AVIF headers
- Overlay is `BitmapImage`. That path color-manages toward sRGB. It cannot present scRGB / HDR10
- Thumbs are 8-bit JPEG. That is correct for the mosaic; it is not an HDR preview
- Ultra HDR JPEGs that *are* already in the catalog (`.jpg`) show the SDR base layer, with no badge and no gain-map path

`Project` here is a **catalog partition**. It is not a Unity folder assignment.

---

## Palace approach

Stay inside **Library + tagging (0.0.x)**. HDR overlay / present is this slice (solid image viewing), not a Rooms, Cloud, or gamedev minor. Do not invent a Rooms canvas, gamedev formats, or AI to get HDR preview. Do not bump version for this plan.

### Rules that do not move

- Packaged WinUI 3 only. ThemeResource, `{x:Bind}` + explicit Mode, AutomationIds, FluentIcons only, icon + text on commands.
- Mosaic stays SDR JPEG thumbs. Do not decode float EXR/HDR into every tile.
- Opening follows Library: **double-click / Enter / context Open**. Hydrate only on that Open. Do not hash, Magick-decode, or SwapChain-present online-only originals.
- Clipboard still uses `AccessService.FilterCopyPaths`. HDR clipboard is not a reason to recall placeholders.
- Tags stay global on the asset. HDR is a preview/catalog-kind problem, not a second tag system.
- HDR is not Rooms, not Cloud, and not gamedev / 3D.

### Why not Magick.NET now

The original plan already gated TGA/EXR/HDR/PSD on **native deps packaging cleanly**. That gate has not opened.

- Magick.NET ships ImageMagick native binaries (x64 / AnyCPU). MSIX + Release trim is the known risk; daily run is packaged Debug because Release already crashes on `ItemsSource` COM wrappers.
- SKIV proves the *product* need (EXR, Radiance, Ultra HDR, JXL, AVIF) but solves it with a **different** native pile (OpenEXR, libjxl, libavif). Vendoring that into Palace is not simpler than Magick.
- WIC already covers Palace’s current catalog. Built-in WIC also has JPEG XR; AVIF/HEIF/JXL only if the user has the Store codecs. Radiance `.hdr` and OpenEXR are **not** WIC.

A packaging spike (Magick **or** DirectXTex+OpenEXR) is allowed later. The spike’s pass condition is: packaged Debug register/launch, those DLLs load, Release trim does not regress further, `Palace.Tests` stay off WinRT. Until that spike is green, do not add EXR / Radiance / TGA / PSD / JXL to `PathSafe.ImageExt`. AVIF is already in via WIC.

### Phase A — honest catalog + overlay chrome (no new native decoder)

Do this as current Library viewing work. It matches “HDR images are not supported” without pretending Magick is in.

- Keep current extensions as the scan allow-list
- Overlay / gallery: **1:1 / Fit / Fill** (SKIV `Ctrl+1/2/3`), keep AutomationIds, icon + text
- Image details row: filename, stored WxH, kind, file size — plus an **HDR / wide-gamut** line only when we can tell from metadata we already read (EXIF) or from a later decoder
- Ultra HDR `.jpg` already in the catalog: still show the SDR base; say so if we detect a gain map **without** opening a new native library (WIC/EXIF only). If detection needs libultrahdr, skip it
- Optional later: **Open with…** / Explorer (already have Explorer). Do not shell out to SKIV as a required dependency

No new tables. No Magick reference. No SwapChainPanel yet.

### Phase B — real HDR present on Open (SKIV viewer, rewritten)

Only after Phase A, and only for files we can already decode (WIC JPEG/PNG/TIFF/JXR, plus any format a *green* packaging spike adds).

- Overlay / `GalleryWindow` image surface becomes a **`SwapChainPanel` + DXGI** swapchain (`R16G16B16A16_FLOAT` / scRGB, or HDR10 when the display is HDR). `BitmapImage` stays for SDR and for thumbs
- Detect display HDR (`DXGI_OUTPUT_DESC1` / advanced color). If the panel is SDR, apply a named tonemap (start with **map CLL to display** + clip; do not ship SKIV’s full enum on day one)
- Visualization (heatmap / gamut / SDR) is a later toggle on this same surface
- Load happens on explicit Open, off the UI thread, then present on the dispatcher. Honor overlay close / `_filterEpoch`-style cancel
- Thumbs remain `ColorManageToSRgb` JPEG. Viewport upgrade may write a larger SDR JPEG; it does not write a float EXR into the thumb cache

This is the SKIV feature Palace actually wants: **see HDR as HDR** inside the DAM, not a second app.

### Phase C — more formats, only after packaging is clean

Pick **one** decoder family after a spike, not both:

1. **Magick.NET-Q16-HDRI-x64** — original plan (TGA / EXR / Radiance / PSD), if MSIX + trim is clean
2. **DirectXTex + optional OpenEXR** — SKIV’s class of solution for `.hdr` / `.tga` / `.exr` / DDS, if those natives package cleaner than Magick

Then, and only then:

- Add extensions to `PathSafe.ImageExt` / `KindFromExt`
- Header-only `ImageDimensions` where cheap (Radiance, EXR); otherwise store dims after a **local** Open/scan decode
- Scan: if online-only, same cloud rules — stub hash, no decode, provider thumb or a dedicated “HDR / cloud” tile
- PSD/TGA are catalog-nice; they are not the HDR present problem. Do not block Phase B on them
- JXL: prefer inbox WIC codecs when present; do not vendor libjxl. HDR AVIF present uses libavif (`Starward.Codec` → `avif.dll` only); SDR AVIF stays inbox WIC

### Explicitly later or never (unless asked)

- Porting or embedding SKIV / ImGui / MinHook
- In-app HDR encode, transcode, or snipping
- Global capture hotkeys, tray, auto-updater
- HDR clipboard that recalls On-Demand files
- Magick (or OpenEXR) before a packaging spike is green
- Listing `.hdr`/`.exr` in the mosaic with no decoder (silent empty tiles)
- Rooms canvas, gamedev formats, AI auto-tag

---

## Suggested order of work

Isiac’s sequence. HDR present on files we can already decode is **Library now**.

1. **Now — Library + tagging.** Image and video viewing must be solid (Phase A chrome, then Phase B HDR present on Open for the WIC path). Tagging must be good. Do not download originals.
2. **Next — Rooms.** Moodboard / mindmap. Kanvaz is UX only. Do not block Rooms on Magick or SKIV.
3. **Bonus — Cloud.** OAuth Dropbox / OneDrive. Not a gate for HDR or Rooms.
4. Packaging spike for one decoder family — abort if MSIX/trim fails. Still 0.0.x viewing work, not a new slice.
5. Phase C extra extensions, only if the spike passed.
6. **Then — Gamedev.** Markdown, audio with loops, 3D, Unity folder on the Palace **project** (not the Library catalog), `.unitypackage`.
7. **Later — AI.** Tagging, organization, and similar.

Until Magick / Phase C lands, treat EXR / Radiance / TGA / PSD / JXL as **out of the catalog on purpose**. AVIF is in the catalog; SDR opens via inbox WIC, HDR present via libavif. WIC overlay / HDR PNG + HDR AVIF present stays current Library work.

---

## Landed (first slice)

PR: [Palace #12](https://github.com/sacb0y/Palace/pull/12) on `cursor/gallery-hdr-scale-f737`.

- Phase A chrome is in overlay + `GalleryWindow` (1:1 / Fit / Fill, details, honest HDR line). Overlay info is a compact opaque bar below the player (`BrdGalleryOverlayInfo`), not a transparent full-width card.
- Phase B present is Magick-free and **HDR PNG only** (`SwapChainPanel` + WIC). Ultra HDR JPEG stays the SDR base + label
- Bugbot on #12: `CurrentProbe` before path, DXGI vtable 24/27, scRGB color space 1, `ScpHdr` inside `ScrStill` so 1:1 can pan, cached `HdrFrame` on resize/scale, `HdrPixels` Rgba8 vs Bgra8, present uses layout DIPs (not Collapsed 0), hide HDR when the still is not `item.Path`, oriented WIC size, window-monitor peak, `StillRevision` one refresh per item, `Bind(null)` cancels epoch, Actual 1:1 ignores unoriented catalog size
- Follow-up (same-scene SDR vs dark HDR PNG): PQ/HLG only when cICP says so; present **clips to a real display peak**, not MaxCLL (that crushed `KalanLiraDuo` vs `_SDR`) and not 203 paper white. 1:1 is one device pixel per image pixel. Fill covers and pans overflow. 1:1 / Fill also left-click-drag pan like touch. Prompt/negative scroll instead of growing the overlay. Optional Peak toggle + nits slider. Optional top-left Info toggle (Ctrl+D, on to match the SKIV shot) shows file / size / **file** resolution / color / CIE-Y luminance / MaxCLL scRGB / display luminance — not Save As / Export / Copy, and not catalog 512×512 or “Display peak: 203”.
- Open speed: first HDR paint is a **viewport** WIC decode (no second `BitmapImage` of the original). Native min/avg/max / MaxCLL scRGB refine after first present. DXGI probe is cached. 203 is never the auto clip.
- `.avif` is in the catalog. SDR AVIF opens through WIC/`BitmapImage`. HDR AVIF (cICP 16/18 or 10-bit) presents scRGB via **libavif** (`HdrAvifDecode` / `Starward.Codec` `avif.dll`: `avifDecoderNextImage` → `avifImageYUVToRGB` → PQ/HLG → scRGB), same class as SKIV. Identity matrix 0 is GBR. Inbox WIC is the fallback only; do **not** treat WIC `Rgba16` luma-in-R as RGB or as identity GBR — red tint (MaxCLL scRGB 207.561) or green (Y→G). Package only `avif.dll` from Starward (strip JXL/UltraHDR/VP9 natives). No Magick.
- SKIV-class stills now catalog without Magick: `.heic` `.heif` `.jxr` `.wdp` `.hdp` `.jxl` `.hdr` `.psd` `.dds`. Radiance is C# RGBE (`RadianceFile`). JXR float HDR stays BitmapImage (WinRT has no float pixel format). JXL HDR probe reads `ImageMetadata` + container `colr` CICP; `CanPresentHdr` is PQ/HLG only (10-bit sRGB JXL stays SDR). JXL P3 primaries are `11`. AVIF/HEIF `av1C` continues through `color_range` (limited is not full). YUV Info CIE Y is source-primary nits before 2020→709 (PNG path). EXR / TGA / Magick still deferred. No SKIV port.
- Peak clip is app-wide Settings (`GalleryPeak` / `HdrPeakOverrideEnabled` / `HdrPeakOverrideNits`). Do not add overlay/gallery peak sliders.
