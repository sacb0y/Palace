# Palace — agent notes

Palace is a packaged WinUI 3 / Windows App SDK digital asset manager (images, GIF, video; later more formats). The metaphor is a mind palace: **Library** (catalog), **Room** (moodboard / mindmap). Files stay on disk. SQLite + FTS5 is the index.

## Keeping these notes current

This file is the always-loaded core. Detailed topic rules live in `.cursor/rules/*.mdc` and load when you touch matching files. When you change a convention (scan, thumbs, UI thread, cloud, packaging, tests), update the **matching rule file** in the same change — not this file. Only add here if it is a cross-cutting contract rule. New topic → new `.mdc` with `description` + `globs`.

| Rule file | Covers |
|---|---|
| `product-tags-library.mdc` | Projects, delete, organize, Tags board, Library mosaic grouping / sort / select |
| `library-ui.mdc` | Do-not-freeze rules, busy bars, startup order, mosaic chunking, lazy thumbs, overlay open/close |
| `gallery-hdr.mdc` | Overlay / GalleryWindow chrome, GIF scrub, 1:1/Fit/Fill, HDR present, SDR tonemap, histogram, video posters |
| `cloud-files.mdc` | On-Demand + API sources, never download originals, hydrate on Open only |
| `catalog-sqlite.mdc` | Schema / migrations, SQL fragments, `PalaceDb` async gate, OAuth storage |
| `errors.mdc` | Log and Notify, do not crash |
| `icons-automation.mdc` | FluentIcons, AutomationIds, badges |
| `code-map.mdc` | Which file owns which area |
| `versioning-release.mdc` | Version identity, slice table, MSIX, trim |
| `tests.mdc` | Test commands and `Palace.Tests` coverage |

## PR / agent goals (read first)

Any PR agent (Cursor, Copilot, human) should treat this as the product contract:

1. **Library-first.** Current slice is **0.0.x Library + tagging**. Ship Library browsing, mosaic, overlay/gallery, tags, watch folders, FTS, and solid image/video (including HDR present). Do not open **0.1 Rooms**, **0.2 Gamedev**, or **0.3 AI** as a new minor unless Sacb0y asks.
2. **Version / slice rules.** Source of truth: `Package.appxmanifest` `Identity Version` + matching `<Version>` in `Palace.csproj` + `Helpers/AppVersion.cs` `Milestone`. Stay on **0.0.x** while Library is the slice — bump **patch** for drops inside the slice (e.g. `0.0.3.0` → `0.0.4.0`), Revision `0`. Do **not** jump to `1.0.0` or `0.1.x` because something “feels ready.” See **Version** below.
3. **Do not invent ahead of the slice.** No freeform Rooms canvas / moodboard-mindmap, no gamedev viewers (3D/audio/markdown Unity assign), no AI tagging/organization features until that minor is opened. Existing Rooms and Organization (on-disk organize / auto-organize) are **infancy** — polish only if the task names them; do not claim they are finished.
4. **Cloud is bonus.** Dropbox / OneDrive OAuth and On-Demand already live in 0.0.x. Further cloud work can land inside the current identity; it is **not** the next numbered slice and must not download originals.
5. **Maintainer preferences (hands-off).** Prefer small, reviewable PRs on a branch from current `main`. Packaged Debug only for daily runs (`.\BuildAndRun.ps1 . --arch x64`). Never unpackaged. Never change GitHub visibility or add secrets. Do not resume paused Magick / TGA / EXR spikes unless asked. Fix trivial CI/Bugbot blockers; skip speculative refactors.
6. **Tests.** `dotnet test .\Palace.Tests\Palace.Tests.csproj` on every change that touches helpers/services. On Windows Main-Desktop, packaged Debug via `BuildAndRun.ps1` when UI is in scope. Linux Cloud Agents: tests only — no WinUI build. Self-hosted Windows worker for live UI.
7. **Linux vs Windows.** Cloud Agent VMs are Linux → `Palace.Tests` only. WinUI / `winapp` / screenshots need a Windows worker (e.g. Main-Desktop). See **Cursor Cloud specific instructions**.

Human-facing overview: root `README.md`. Bug reports: Settings → About (`TxtAppVersion`).

## Stack (locked)

- Packaged `winui-mvvm` only. CommunityToolkit.Mvvm, `{x:Bind}` with explicit `Mode`, `AutomationProperties.AutomationId` on interactive controls, `ThemeResource` (not hardcoded brushes).
- Never run the unpackaged `.exe`. Never set `WindowsPackageType=None`.
- Run: `.\BuildAndRun.ps1 . --arch x64` (or `winapp run . --arch x64`; the script forwards to the winui-dev-workflow skill script when present, else runs `winapp run`). Invoke attached runs asynchronously; the command stays attached while the app is open.
- Official skills: `C:\Users\iadag\.cursor\skills\winui-*` — load `winui-dev-workflow`, `winui-design`, `winui-packaging`, `winui-code-review`, `winui-ui-testing` as needed.
- Domain-reload-disabled is a Unity habit. It does not apply here.
- `Palace.Tests` is a separate `net10.0` project (`Palace.csproj` excludes `Palace.Tests\**`). Keep tests off WinUI / WinRT. Do not reference `FluentIcons.WinUI` from tests.
- Debug vs Release of the **same source** share one version number. Daily run is Debug (`.\BuildAndRun.ps1 . --arch x64`). `PublishTrimmed` is **off** for both until Release trim stops crashing on `ItemsSource` COM wrappers; re-enable trim only with `SelfContained` + `WindowsAppSDKSelfContained` (else `NETSDK1102`). Title bar: set `AppTitleBar.Title` to `AppVersion.TitleBarTitle` (`Palace {version}`) and `Subtitle` to `AppVersion.TitleBar` — do not leave Title hardcoded `Palace` alone.

## PR descriptions

Keep them short. Template:

```
## What
One or two sentences.

## Why
One sentence or issue link.

## Tested
- [ ] dotnet test Palace.Tests
- [ ] Packaged Debug on Windows (if UI)
```

No essays, no full verification tables, no screenshots unless the change is visual.

## Cursor Cloud specific instructions

Cloud Agents run on **Linux**. On the Linux VM, only **`Palace.Tests`** (`net10.0`, off WinUI/WinRT) builds and runs. Keep those tests cross-platform. Windows-only remains `BuildAndRun.ps1` / `winapp` / `ui-tests.ps1` and live screenshots — not available on the Linux VM itself.

```bash
dotnet test ./Palace.Tests/Palace.Tests.csproj
```

**Self-hosted Windows worker.** For WinUI build/run, UI tests, screenshots, or visual design review of the live app, do **not** conclude "no Windows access" just because the Cloud Agent VM is Linux. Check connected workers with cursor-cloud `list-self-hosted-workers`. If a Windows worker is connected (e.g. `win-laptop` / My Machines), target it for that UI/visual work. If none is connected — or Developer Mode / the Palace checkout is missing — remind the user to set up or reconnect the Windows PC (Cursor CLI `agent worker start` from the Palace checkout, Developer Mode on, keep unlocked) before treating live UI verification as impossible.

The environment is repo-managed via `.cursor/environment.json`, whose `install` runs `.cursor/install.sh` to install the .NET 10 SDK into `$HOME/.dotnet` (added to `PATH`/`DOTNET_ROOT` in `~/.bashrc`) and warm a build of the test project.
