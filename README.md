# Palace

Packaged WinUI 3 / Windows App SDK digital asset manager. Files stay on disk. SQLite + FTS5 is the catalog index.

https://github.com/sacb0y/palace

## Goals

**Now — 0.0 Library core.** Watch folders, browse a mosaic, tag and search, organize on disk when asked.

**Later** (planned slices; not current work):

- **0.1 Cloud** — On-Demand and API sources; do not download originals until Open
- **0.2 Wings** — Unity / game overlay on the same catalog
- **0.3 3D** — glTF / OBJ preview first
- **0.4 Unity packages** — preview `.unitypackage`
- **0.5 LLM tags** — Ollama / LM Studio, then Grok
- **1.0 Ship** — Release / trim, packaging / Store

## What works today

Identity on main is **0.0.3 · Library core**.

- **Library mosaic** — watched folders, FTS search, overlay / gallery for images, GIF, and video, dry-run organize, Recycle Bin delete
- **Tags board** — Eagle-style board; groups and child tags sit in two columns ([#18](https://github.com/sacb0y/palace/pull/18)); hierarchy, Any / All / None filter, implications
- **Cloud** — On-Demand detection from file attributes; no hash or decode of online-only originals; hydrate only on explicit Open
- **Rooms** — infancy: create, list, and sectioned pin grids. Not a freeform moodboard yet

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
