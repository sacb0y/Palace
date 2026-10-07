# Palace

Packaged WinUI 3 / Windows App SDK digital asset manager. Files stay on disk. SQLite + FTS5 is the catalog index. The metaphor is a mind palace: **Library** (catalog) and **Rooms** (moodboard / mindmap).

https://github.com/sacb0y/Palace

## Status

**0.0.4 · Library core · initial public preview.**

Library browsing, mosaic, overlay/gallery (images, GIF, video, HDR present), tags, watch folders, and FTS are the focus of this slice.

**Rooms** (product “palace” rooms — moodboard / mindmap) and **Organization** (on-disk organize / auto-organize) are in their infancy. Expect stubs and early flows, not finished features. Cloud (Dropbox / OneDrive) is a bonus capability already present in 0.0.x, also very early.

Agent / PR contract (version rules, Library-first, what not to invent): see [`AGENTS.md`](./AGENTS.md).

## Goals

**Now — 0.0 Per "Project" Library and tagging** Image and video viewing must be solid (HDR support a later focus). The tagging system must be good. Watch folders, browse a mosaic, tag and search, organize on disk when asked. Optional caching for faster loading and previews.

**Next — 0.1 Rooms** Moodboard (a board of images to look at) plus mindmap (lines, diagrams, notes). Support current and later formats. [Kanvaz](https://github.com/p4inz-code/kanvaz) and PureRef as inspirations but to expand beyond images.

**Bonus — Cloud** View and manage Dropbox and OneDrive via OAuth. Some of this already works in 0.0.

**Then — 0.2 Gamedev focus** More formats and viewers:

- Markdown reading
- Audio (with loops)
- 3D asset viewing
- Assign a Unity folder to the Palace **project** (asset management, not the Library catalog)
- Parse `.unitypackage` and extract needed assets

**Later — 0.3 AI.** Expand asset management with AI (tagging, organization, and similar).

**1.0 Ship.** Release / trim, packaging / Store.

## What works today

Identity on main for this preview is **0.0.4 · Library core · initial public preview**.

- **Library mosaic** — watched folders, FTS search, overlay / gallery for images, GIF, and video, dry-run organize, Recycle Bin delete. Top-level folder browse groups tiles by child folders.
- **Tags board** — Eagle-style board; groups and child tags sit in two columns; hierarchy, Any / All / None filter, implications
- **Cloud** — Can handle "Cloud Only" files in dropbox and onedrive without mass redownloading (currently may not generate thumbnails), preliminary cloud storage support.
- **Rooms** — infancy: create, list, and sectioned pin grids. Not a freeform moodboard or mindmap yet
- **Organization** — infancy: dry-run organize and optional auto-organize exist; not a finished organization product

## Build and run

Windows only. **Packaged** Debug (never the unpackaged `.exe`):

```powershell
.\BuildAndRun.ps1 . --arch x64
```

Daily run is Debug. Packaged Release works with trim off (`winapp run . --arch x64 -c Release`); re-enable `PublishTrimmed` only after the ItemsSource COM crash is fixed.

Settings → About shows `TxtAppVersion` (e.g. `Palace 0.0.4 (Debug) · Library core · initial public preview`) and the Rooms / Organization infancy note.

Linux Cloud Agents cannot build or run the WinUI app. Tests only:

```bash
dotnet test ./Palace.Tests/Palace.Tests.csproj
```

On Windows:

```powershell
dotnet test .\Palace.Tests\Palace.Tests.csproj
```
