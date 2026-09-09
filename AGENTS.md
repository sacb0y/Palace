# Palace — agent notes

Palace is a packaged WinUI 3 / Windows App SDK digital asset manager (images, GIF, video; later 3D). The metaphor is a mind palace: **Library** (catalog), **Wing** (Unity, later), **Room** (moodboard). Files stay on disk. SQLite + FTS5 is the index.

Keep this file current. When you change a convention (scan, thumbs, UI thread, cloud, packaging, tests), update the matching section here in the same change.

## Stack (locked)

- Packaged `winui-mvvm` only. CommunityToolkit.Mvvm, `{x:Bind}` with explicit `Mode`, `AutomationProperties.AutomationId` on interactive controls, `ThemeResource` (not hardcoded brushes).
- Never run the unpackaged `.exe`. Never set `WindowsPackageType=None`.
- Run: `.\BuildAndRun.ps1 . --arch x64` (or `winapp run`). Invoke attached runs asynchronously; the command stays attached while the app is open.
- Official skills: `C:\Users\iadag\.cursor\skills\winui-*` — load `winui-dev-workflow`, `winui-design`, `winui-packaging`, `winui-code-review`, `winui-ui-testing` as needed.
- Domain-reload-disabled is a Unity habit. It does not apply here.
- `Palace.Tests` is a separate `net10.0` project (`Palace.csproj` excludes `Palace.Tests\**`). Keep tests off WinUI / WinRT.

## Product rules

- Tags are **global**. Library sources and Rooms are **per project**. Default project name: **Palace**.
- The same disk folder cannot belong to two projects (`SourceFolder.Path` is UNIQUE).
- Delete = Recycle Bin + catalog row (`DeleteAssetsAsync`).
- Organize is on-disk, dry-run first, ask destination each run. Auto-organize is opt-in per source.
- Tag organize asks which parent chain. Assigning a tag applies configured implicits (transitive, cycle-safe).
- Library-first. Do not invent Wings or LLM auto-tag unless asked.

## Layout

| Area | Where |
|---|---|
| Schema / migrate | `Data/PalaceDb.cs` |
| Catalog queries | `Services/CatalogService.cs` |
| Scan / index | `Services/ScanService.cs` |
| Thumbs | `Services/ThumbnailService.cs` |
| Open hydrate | `Services/HydrationService.cs` |
| Cloud accounts + OAuth | `Services/CloudAccountService.cs`, `Services/Cloud/` |
| Library mosaic | `ViewModels/LibraryViewModel.cs`, `Pages/LibraryPage.xaml` |
| Asset → tile | `Helpers/AssetItemMapper.cs` (not ad-hoc `ToItem` probes) |
| UI thread hops | `Helpers/UiDispatch.cs` |
| On-Demand detect | `Helpers/CloudFile.cs` |
| UI tests | `ui-tests.ps1` (`winapp ui`, AutomationIds) |

`SourceFolder.AccessToken` is the **FutureAccessList** token. Do not store OAuth there. Cloud OAuth lives in `PasswordVault` via `CloudTokenStore`. App IDs (`OneDriveClientId`, `DropboxAppKey`) live in LocalSettings — no hardcoded secrets.

Schema: add cloud columns (`EnsureCloudColumns`) **before** indexes that mention them (`IX_Asset_CloudItem`). SQL fragment constants that are concatenated (`SelectSourceSql + " WHERE …"`) must end with a newline or space — `"""WHERE` became `SourceFolderWHERE` and crashed launch.

## Library UI — do not freeze

Opening a project, Library, or a large folder must stay interactive.

- Call `Library.BeginBusy("Loading…")` **before** any await. `IsBusy` is ref-counted (`BeginBusy` / `EndBusy`).
- Progress: `PrgStartup` until the main shell is shown, then `PrgLibrary` on Library and `PrgProjectLoad` under the project combo. Bind Library bars to `IsBusy`. A freeze with no bar is a bug.
- Startup: activate the window **before** `InitializeAsync` / `LoadInitialDataAsync` (`App.OnLaunched` → `Window.Activate` → init → `ShowMain` → load). Do not await a full catalog load on a blank process.
- Status: `Loading…` → `Showing 80 of 2400` → the final count line.
- Project switch (`AppServices.SetCurrentProjectAsync`): bar first, Library, then Rooms/Settings.
- Folder browse: `GetAssetsAsync(folderPrefix:)` — do not load the whole project and filter in process.
- Build `AssetItem`s off the UI thread. Chunk-append ~80 (`MosaicChunkSize`) on the dispatcher and `UiDispatch.YieldAsync`. Honor `_filterEpoch`.
- Mutate `Assets` only on the UI thread (`UiDispatch`). After chunks, `LinedFlowLayout.InvalidateItemsInfo` (`MosaicChunkAppended`).
- `ToItem` → `AssetItemMapper.FromAsset`. Never `File.Exists` or `ImageDimensions.TryRead` on the **original** while building the mosaic. Use stored Width/Height or a default aspect.
- Lazy thumbs: bind `ThumbImage`, not a static `FileToImage` during measure. Decode only realized tiles; cap in-flight decodes.
- `UpgradeThumbsAsync`: viewport only, never on first paint, skip `IsOnlineOnly`.

## Cloud files — do not download the original

Two source kinds:

1. **Local + Files On-Demand** (OneDrive/Dropbox sync folders added with the normal folder picker).
2. **API sources** (`SourceKind.OneDrive` / `Dropbox`) after Settings → Connect. Index via Graph / Dropbox `list_folder`. Store `CloudItemId` + display path.

Detect On-Demand with `CloudFile.IsOnlineOnly` (`File.GetAttributes` only): `RecallOnDataAccess` (`0x00400000`), `RecallOnOpen`, `Offline`. **Never open a stream** to test this.

On scan, if online-only:

- Do **not** `HashFileAsync`, metadata `Extract`, `BitmapDecoder`, or `ImageDimensions.TryRead` on the original.
- Identity: `HashService.CloudStubHash` (`cloud:` + path/size/mtime) until the file is local.
- Set `Asset.IsOnlineOnly`. `FileInfo.Length` / last-write are safe.
- Thumb: `StorageFile.GetThumbnailAsync` (provider stream). If that fails, leave no JPEG — mosaic shows `ShowCloudTile` (`IsOnlineOnly && !HasThumbnail`).
- Watchers skip non-`Local` sources.

Hydrate **only** on explicit Open (Library overlay / new window / Gallery). `HydrationService.HydrateAfterOpenAsync` waits until the placeholder is local, then re-hashes, extracts, replaces the stub thumb. API-only items use a provider large preview URL, not the original, unless the user chooses Download or Open in Explorer.

Clipboard copy must not recall files:

```csharp
var paths = AccessService.FilterCopyPaths(targets.Select(t => t.Path), out var skipped);
if (skipped > 0) Notify(AccessService.OnlineOnlyCopyWarning);
```

`File.Move` of a placeholder usually stays a placeholder — organize may keep working.

Settings Connect smoke IDs (no live OAuth in `ui-tests.ps1`): `BtnConnectOneDrive`, `BtnConnectDropbox`, `BtnAddOneDriveFolder`, `BtnAddDropboxFolder`, `TxtCloudRedirectUri`. Capability: `internetClient`.

## Tests

```powershell
dotnet test .\Palace.Tests\Palace.Tests.csproj
.\BuildAndRun.ps1 . --arch x64
# after launch, with the packaged PID:
.\ui-tests.ps1 -AppPid <pid>
```

`Palace.Tests` covers `CloudFile.IsOnlineOnly` attribute flags (including stamped `FILE_ATTRIBUTE_OFFLINE`). Do not add live OAuth to `ui-tests.ps1`. Recycle-delete and scan-size UI fixtures need a watched `PalaceUiTest`/`Temp` folder; without it those tests skip or fail. Magick.NET / TGA / EXR / HDR / PSD are out of scope until packaging is solved.

## Cursor Cloud specific instructions

Cloud Agents run on **Linux**, so the packaged WinUI 3 app (`Palace.csproj`) cannot build or run there — `BuildAndRun.ps1`, `winapp`, and `ui-tests.ps1` are Windows-only and must be run on a Windows host. The buildable, runnable surface on Linux is **`Palace.Tests`** (`net10.0`, off WinUI/WinRT).

The environment is repo-managed via `.cursor/environment.json`, whose `install` runs `.cursor/install.sh` to install the .NET 10 SDK into `$HOME/.dotnet` (added to `PATH`/`DOTNET_ROOT` in `~/.bashrc`) and warm a build of the test project. To verify the environment on Linux:

```bash
dotnet test ./Palace.Tests/Palace.Tests.csproj
```

Keep `Palace.Tests` cross-platform so it stays runnable here. Any Windows-only verification (the WinUI app, UI automation) must be done on Windows.
