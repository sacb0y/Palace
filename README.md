# Palace

Packaged WinUI 3 / Windows App SDK digital asset manager. Files stay in place on disk. SQLite + FTS5 is the catalog index. 

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

V **0.0.3 · Library core**.

- **Library mosaic** — Watched folders per project, FTS search, overlay / gallery for images, GIF, and video.
- **Tags board** — Global Tag groups and child tags sit in two columns; hierarchy, Any / All / None filter; Support for implied tags similar to booru sites
- **Cloud** — Can handle "Cloud Only" files in dropbox and onedrive without mass redownloading (currently may not generate thumbnails), preliminary cloud storage support.
- **Rooms** — Will soon be moodboard/mindmap backed by image tagging

## Run

Windows, packaged Debug (keep the process attached while the app is open):

```powershell
.\BuildAndRun.ps1 . --arch x64
```

Linux Cloud Agents cannot build or run the WinUI app. Tests only:

```bash
dotnet test ./Palace.Tests/Palace.Tests.csproj
```
