# Palace

Packaged WinUI 3 / Windows App SDK digital asset manager. Files stay on disk. SQLite + FTS5 is the catalog index.

https://github.com/sacb0y/palace

## Goals

**Now — 0.0 Library and tagging.** Image and video viewing must be solid (HDR overlay work is this slice, not a later one). The tagging system must be good. Watch folders, browse a mosaic, tag and search, organize on disk when asked.

**Next — 0.1 Rooms.** Moodboard (a board of images to look at) plus mindmap (lines, diagrams, notes). Support current and later formats. [Kanvaz](https://github.com/p4inz-code/kanvaz) is a UX reference only — not a port.

**Bonus — Cloud.** Not the next main slice. View and manage Dropbox and OneDrive via OAuth. Some of this already works in 0.0.

**Then — 0.2 Gamedev.** More formats and viewers:

- Markdown reading
- Audio (with loops)
- 3D asset viewing
- Assign a Unity folder to the Palace **project** (asset management, not the Library catalog)
- Parse `.unitypackage` and extract needed assets

**Later — 0.3 AI.** Expand asset management with AI (tagging, organization, and similar).

**1.0 Ship.** Release / trim, packaging / Store.

## What works today

Identity on main is **0.0.3 · Library core**.

- **Library mosaic** — watched folders, FTS search, overlay / gallery for images, GIF, and video, dry-run organize, Recycle Bin delete. Top-level folder browse groups tiles by child folders.
- **Tags board** — Eagle-style board; groups and child tags sit in two columns ([#18](https://github.com/sacb0y/palace/pull/18)); hierarchy, Any / All / None filter, implications
- **Cloud** — bonus capability already in 0.0: On-Demand detection from file attributes; no hash or decode of online-only originals; hydrate only on explicit Open
- **Rooms** — infancy: create, list, and sectioned pin grids. Not a freeform moodboard or mindmap yet

## Run

Windows, packaged Debug (keep the process attached while the app is open):

```powershell
.\BuildAndRun.ps1 . --arch x64
```

Daily run is Debug. Release of the same source shares the version number but is trimmed and currently crashes.

Linux Cloud Agents cannot build or run the WinUI app. Tests only:

```bash
dotnet test ./Palace.Tests/Palace.Tests.csproj
```
