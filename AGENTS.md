# Palace — agent notes

Palace is a packaged WinUI 3 / Windows App SDK digital asset manager (images, GIF, video; later 3D). The metaphor is a mind palace: **Library** (catalog), **Wing** (Unity, later), **Room** (moodboard). Files stay on disk. SQLite + FTS5 is the index.

Keep this file current. When you change a convention (scan, thumbs, UI thread, cloud, packaging, tests), update the matching section here in the same change.

## Stack (locked)

- Packaged `winui-mvvm` only. CommunityToolkit.Mvvm, `{x:Bind}` with explicit `Mode`, `AutomationProperties.AutomationId` on interactive controls, `ThemeResource` (not hardcoded brushes).
- Never run the unpackaged `.exe`. Never set `WindowsPackageType=None`.
- Run: `.\BuildAndRun.ps1 . --arch x64` (or `winapp run`). Invoke attached runs asynchronously; the command stays attached while the app is open.
- Official skills: `C:\Users\iadag\.cursor\skills\winui-*` — load `winui-dev-workflow`, `winui-design`, `winui-packaging`, `winui-code-review`, `winui-ui-testing` as needed.
- Domain-reload-disabled is a Unity habit. It does not apply here.
- `Palace.Tests` is a separate `net10.0` project (`Palace.csproj` excludes `Palace.Tests\**`). Keep tests off WinUI / WinRT. Do not reference `FluentIcons.WinUI` from tests.
- Debug vs Release of the **same source** share one version number. Debug is not an older tree — it is untrimmed. Release is trimmed and currently crashes on `ItemsSource` COM wrappers; daily run is Debug (`.\BuildAndRun.ps1 . --arch x64`).

## Icons

Chrome icons come from `FluentIcons.WinUI` (`xmlns:ic="using:FluentIcons.WinUI"`). Use named `FluentIcon` / `FluentIconSource` values. Do not add Segoe hex `FontIcon` glyphs and do not add a second pack (Lucide, Tabler, Heroicons, Fluent Emoji).

- Command buttons stay **icon + existing text**. Do not convert toolbars to icon-only. Skip repeated “Remove” chip buttons.
- Room identity icons are a curated string id on `Collection.Icon` / `Room.Icon`, normalized by `Helpers/RoomIcons.cs` (default `BuildingBank`). Parse to the Fluent enum only in `Helpers/FluentGlyph.cs` — ViewModels stay pack-free.
- Gallery for names: https://davidxuang.github.io/FluentIcons/ — if an enum is missing, pick the closest Regular sibling.
- Keep existing `AutomationProperties.AutomationId` values; new pickers get stable ids (e.g. `GrdRoomIcons`). Tag chips use `TagChipItem.AutomationId` (`BtnTagChip_` / `BtnTagFilter_` / `BtnAssignChip_`). Board and assign groups use `TagBoardGroup.AutomationId` (`BtnTagGroup_`). Library mosaic folder headers use `TxtFolderGroup_` (`AssetItem.FolderGroupAutomationId`).

## Product rules

- Tags are **global**. Library sources and Rooms are **per project**. Default project name: **Palace**.
- The same disk folder cannot belong to two projects (`SourceFolder.Path` is UNIQUE).
- Delete = Recycle Bin + catalog row (`DeleteAssetsAsync`).
- Organize is on-disk, dry-run first, ask destination each run. Auto-organize is opt-in per source.
- Tag organize asks which parent chain. Assigning a tag applies configured implicits (transitive, cycle-safe).
- Tags page is an Eagle-style board. **All / Uncategorized / Starred are pinned at the top of the left group column** (in that order), then user groups (`TagPanelBuilder.BuildBoard`). Selecting a group stays in that column; child / sub-tags of the selected group are color-dot rows in the next column (`RepTagChildren`, `TxtTagChildrenHeader`). Creating or selecting a tag opens the group that contains it (`TagBoardExpand`). Uncategorized display name stays **Uncategorized**; automation id stays `BtnTagGroup_Ungrouped` (`IsUngrouped` / `AutomationIdOverride`) so the Library assign flyout does not become `BtnTagGroup_Uncategorized`. Search All chips have no `ParentId`. Tags inside a group are **A–Z with letter headers** (`TagAlphaIndex`). The Tags mosaic groups by **selected tag / next child / next child** (`TagMosaicGroups`; Priority then name). Keep `GrdTagAssets` and `TxtTagMosaicGroup_*` headers. Grouped mosaic tiles are tab-stop; Enter opens the focused or clicked tile, not the first asset. Keep `SelTagScope` on the board scroller, `SelTagScopeAll` / `SelTagScopeStarred` / `BtnTagGroup_Ungrouped` on those rows, and `RepTagBoard`. Disk organize stays in the details column. Comma/newline batch-create uses `TagNameList`. Library tag browse toggles multiple tags with Any / All / None (`TagFilter`). Preview tags are wrap chips with × (keep `LstTags` / `BtnRemoveTag_*`); `BtnBrowseTags` is the + Add tag flyout. Chip context: Filter in Library (`ApplySingleTagFilter` + `NavLibrary`), star, rename (focus `TxtRenameTag`), Add/Move to group (root groups via `TagGroups`), remove from this group, delete. Keep `MnuFilterTag` / `MnuRenameTag` / `MnuAddToGroup_*` / `MnuMoveToGroup_*`. Library `SelBrowseMode` / `SelTagMatch` follow `IsTagBrowse` / `TagFilterMode` on construct and Loaded — do not write the Folders/All defaults back. `SelectNode` stamps `SelectedName` and a rename baseline for every path (chip, group, create). `LoadSelectionAsync` applies the catalog name only when the tag changed or the box still matches that baseline — a cleared or edited box on the same tag is kept (`TagSelection.ShouldApplyLoadedName`).
- Library mosaic: viewing a **top-level** (source) folder groups tiles by the first 1–2 child folders. Header text is `Selected / Sub / Sub` (`FolderGroups`). Nested folder browse, search, tag browse, and the all-library view stay a flat mosaic. Headers are not assets — skip them for selection, gallery, thumbs, and organize.
- Library-first until the matching 0.x slice. Do not invent Wings, 3D, Unity packages, or LLM auto-tag unless that minor is the work (see Version).

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
| Thumb cache names | `Helpers/ThumbFileName.cs` |
| Overlay / mosaic media | `Helpers/GalleryMedia.cs` |
| Library folder groups | `Helpers/FolderGroups.cs` |
| Cloud source paths | `Helpers/CloudSourcePath.cs` |
| Room icon ids | `Helpers/RoomIcons.cs` |
| Tags mosaic groups | `Helpers/TagMosaicGroups.cs` (longest path under the selected root, then Priority then name) |
| Tag name lists | `Helpers/TagNameList.cs` |
| Tag filter sets | `Helpers/TagFilter.cs` |
| Tag board / assign panel | `Helpers/TagPanelBuilder.cs` (`BuildBoard` pins All / Uncategorized / Starred, then user groups; Uncategorized keeps `BtnTagGroup_Ungrouped`) |
| Tag board expand | `Helpers/TagBoardExpand.cs` (create/select opens the row that shows the tag) |
| Tag sibling order | `Helpers/TagSiblings.cs` |
| Tag group destinations | `Helpers/TagGroups.cs` |
| Tag A–Z index | `Helpers/TagAlphaIndex.cs` |
| Tag rename apply | `Helpers/TagSelection.cs` |
| Recent tags | `Helpers/RecentTags.cs` |
| Fluent enum parse | `Helpers/FluentGlyph.cs` (XAML only) |
| UI tests | `ui-tests.ps1` (`winapp ui`, AutomationIds) |
| App version | `Helpers/AppVersion.cs` (identity + Debug/Release + milestone) |

`SourceFolder.AccessToken` is the **FutureAccessList** token. Do not store OAuth there. Cloud OAuth lives in `PasswordVault` via `CloudTokenStore`. App IDs (`OneDriveClientId`, `DropboxAppKey`) live in LocalSettings — no hardcoded secrets.

Schema: add cloud columns (`EnsureCloudColumns`) **before** indexes that mention them (`IX_Asset_CloudItem`). SQL fragment constants that are concatenated (`SelectSourceSql + " WHERE …"`) must end with a newline or space — `"""WHERE` became `SourceFolderWHERE` and crashed launch.

## Library UI — do not freeze

Opening a project, Library, or a large folder must stay interactive.

- Call `Library.BeginBusy("Loading…")` **before** any await. `IsBusy` is ref-counted (`BeginBusy` / `EndBusy`).
- Progress: `PrgStartup` until the main shell is shown, then `PrgLibrary` on Library and `PrgProjectLoad` under the project combo. Bind Library bars to `IsBusy`. A freeze with no bar is a bug.
- Startup: activate the window **before** `InitializeAsync` / `LoadInitialDataAsync` (`App.OnLaunched` → `Window.Activate` → init → `ShowMain` → load). Do not await a full catalog load on a blank process.
- Status: `Loading…` → `Showing 80 of 2400` → the final count line.
- Project switch (`AppServices.SetCurrentProjectAsync`): bar first, Library, then Tags/Rooms/Settings. Tags mosaic is project-scoped.
- Folder browse: `GetAssetsAsync(folderPrefix:)` — do not load the whole project and filter in process. Top-level folder grouping runs on that prefix result (`FolderGroups`), not a whole-catalog in-process filter.
- Build `AssetItem`s off the UI thread. Chunk-append ~80 (`MosaicChunkSize`) on the dispatcher and `UiDispatch.YieldAsync`. Honor `_filterEpoch`.
- Mutate `Assets` only on the UI thread (`UiDispatch`). After chunks, `LinedFlowLayout.InvalidateItemsInfo` (`MosaicChunkAppended`).
- `ToItem` → `AssetItemMapper.FromAsset`. Never `File.Exists` or `ImageDimensions.TryRead` on the **original** while building the mosaic. Use stored Width/Height or a default aspect.
- Lazy thumbs: bind `ThumbImage`, not a static `FileToImage` during measure. Decode only realized tiles; cap in-flight decodes.
- `ItemsView` may not set `DataContext` on tiles and recycled containers do not re-fire `Loaded`. Stamp `Tag="{x:Bind Id, Mode=OneWay}"` on the `ItemContainer` **and** the tile `Image`. Track live `Image`s and re-resolve via `GalleryMedia.FindAssetId` on Tag / DataContext / chunk — do not untrack just because `DataContext` is not an `AssetItem`.
- Overlay open is **double-click / Enter / context Open only** (Library and the Tags mosaic). Do not synthesize a second click from `PointerPressed` on both the tile and `GrdAssets` — the same press arrives twice and opens on a single click.
- `LinedFlowLayout` aspect requests use `GalleryMedia.MosaicAspectCount` (requested range capped by remaining items) and `GalleryMedia.MosaicAspect` so folder headers take a full line. Wire both `MosaicReset` and `MosaicChunkAppended` to `InvalidateItemsInfo`.
- `AssetItem.ContentHash` is required for lazy generate and viewport upgrade. Do not recover the hash from the JPEG file name.
- Overlay image/video sources are applied in code-behind (`UpdateOverlayMedia`). Do not rely only on nested `x:Bind` of `OverlayGallery.CurrentPath` through a converter — that path is null until `LoadCurrentAsync` finishes and often never refreshes.
- Overlay chrome: put title/tags in a **row below** the player (`GalleryWindow` already does this). A bottom-overlay details card covers `MediaPlayer` transport controls (settings / seek).
- Video posters use `StorageFile.GetThumbnailAsync` (shell/provider stream), not `BitmapDecoder` on the original. Overlay play uses `MediaPlayer.Play()` (`BtnGalleryPlay`).
- `UpgradeThumbsAsync`: viewport only, never on first paint, skip `IsOnlineOnly`. Do not replace `ThumbImage` when the cached JPEG is already decoded (folder-switch flicker). Reuse decoded `BitmapImage`s by thumb path.

## Cloud files — do not download the original

Two source kinds:

1. **Local + Files On-Demand** (OneDrive/Dropbox sync folders added with the normal folder picker).
2. **API sources** (`SourceKind.OneDrive` / `Dropbox`) after Settings → Connect. Connect opens the cloud-folder picker for the **current project**. Index via Graph / Dropbox `list_folder`. Store `CloudItemId` (`SourceFolder.CloudRootItemId`) + display path (`CloudSourcePath.Build`). Path stays UNIQUE across projects.

Detect On-Demand with `CloudFile.IsOnlineOnly` (`File.GetAttributes` only — not `File.Exists`, never a stream): `RecallOnDataAccess` (`0x00400000`) and `RecallOnOpen` mean hydrate-on-read. `FILE_ATTRIBUTE_PINNED` is local. Hydrated OneDrive/Dropbox files keep `ReparsePoint` and may still show leftover `Offline` — those are local and must be hashed/thumbnailed normally. Bare `Offline` (legacy HSM, or tests that stamp `FILE_ATTRIBUTE_OFFLINE`) is online-only. Use `CloudFile.Exists` / `TryGetAttributes` on user asset paths so placeholders are visible. `ImageDimensions.TryRead` refuses online-only paths. `HashService.HashFileAsync` is scan/local and explicit Open hydration only — never a cloud-vs-local probe.

On scan, if online-only:

- Do **not** `HashFileAsync`, metadata `Extract`, `BitmapDecoder`, or `ImageDimensions.TryRead` on the original.
- Identity: `HashService.CloudStubHash` (`cloud:` + path/size/mtime) until the file is local.
- Set `Asset.IsOnlineOnly`. `FileInfo.Length` / last-write are safe.
- Thumb: `StorageFile.GetThumbnailAsync` (provider stream). Never `Open` / decode the original. If that fails, or the shell only has a generic file icon, leave no JPEG — mosaic and preview show a kind **icon tile** (`ShowCloudTile` / `ShowPlaceholderTile`), not a blank image. Mosaic still requests that provider JPEG for online-only items (`ShouldRequestMosaicThumb`).
- Watchers skip non-`Local` sources.

Hydrate **only** on explicit Open (Library overlay / new window / Gallery). `HydrationService.HydrateAfterOpenAsync` fully reads the original first when it is still online-only **and** larger than 32MB (`HashService.RecallFullyAsync`) — prefix-hash only reads 8MB and would leave recall flags set. Then re-hash, extract, replace the stub thumb. Do not apply empty Extract over existing Prompt/Model while still online-only. Preview uses `GalleryMedia.CanShowPreview` (local hydrated file or remote URL), not a non-empty catalog Path. Catalog `IsOnlineOnly` is stale after hydration — `GalleryMedia.IsLiveOnlineOnly` lets a showable original win so the details pane uses the file (and video play) instead of the thumb / Online tile. API-only items use a provider large preview URL, not the original, unless the user chooses Download or Open in Explorer.

Clipboard copy must not recall files:

```csharp
var paths = AccessService.FilterCopyPaths(targets.Select(t => t.Path), out var skipped);
if (skipped > 0) Notify(AccessService.OnlineOnlyCopyWarning);
```

`File.Move` of a placeholder usually stays a placeholder — organize may keep working.

Settings Connect smoke IDs (no live OAuth in `ui-tests.ps1`): `BtnConnectOneDrive`, `BtnConnectDropbox`, `BtnAddOneDriveFolder`, `BtnAddDropboxFolder`, `TxtCloudRedirectUri`. About: `TxtAppVersion`. Capability: `internetClient`.

## Version

Pre-1.0. Identity is four parts (`Major.Minor.Patch.Revision`); the UI drops Revision. **Minor** is a planned product slice from the original Palace plan (`winui_asset_library_a3139e5d.plan.md`, [Palace kickoff](a7dfd0c1-d504-4c43-a957-008d68e3898f)). **Patch** is work inside the current slice. **1.0.0** is ship, not “we have a library.”

**Source of truth:** `Package.appxmanifest` `Identity Version` (today `0.0.3.0`). Keep `<Version>` in `Palace.csproj` on the same `Major.Minor.Patch`. Settings → About (`TxtAppVersion`) and the title-bar subtitle come from `AppVersion` — e.g. `Palace 0.0.3 (Debug) · Library core`. When you open a new slice, bump the minor **and** `AppVersion.Milestone` in the same change.

MSIX identities cannot go backwards. This repo already registered `1.0.1.0` once; after dropping to `0.0.1.0`, `winapp unregister` if the next Debug register/launch refuses the older identity.

| Version | Slice | Original plan |
|---|---|---|
| **0.0.x** *(now 0.0.3)* | **Library core** | v1 DAM: watch folders, mosaic, hierarchical tags, FTS, Rooms, organize/rename, A1111/Comfy metadata. Magick TGA/EXR/HDR/PSD still deferred. |
| **0.1.x** | **Cloud** | Added after v1 (plan said “out of scope unless you ask”). On-Demand + API sources; do not download originals. Current cloud work stays **0.0.x** until this slice is the one you ship. |
| **0.2.x** | **Wings** | Unity/game overlay on the same catalog (multi-directory project organize). Schema already has `Project`. |
| **0.3.x** | **3D** | Preview glTF/OBJ first; FBX/USD convert; `.blend` via Blender CLI. |
| **0.4.x** | **Unity packages** | Preview `.unitypackage`; import via batchmode or copy. |
| **0.5.x** | **LLM tags** | Ollama / LM Studio, then Grok; assignment `Source=AiLocal` / `AiCloud`. Not the same as prompt-token suggestions. |
| **1.0.0** | **Ship** | Release/trim fixed, Magick formats if packaging is clean, `winui-packaging` / Store. |

Leave Revision at `0` unless you need a same-patch rebuild identity. Debug and Release of one commit share the number; the suffix is which binary you launched.

## Tests

```powershell
dotnet test .\Palace.Tests\Palace.Tests.csproj
.\BuildAndRun.ps1 . --arch x64
# after launch, with the packaged PID:
.\ui-tests.ps1 -AppPid <pid>
```

`Palace.Tests` covers `CloudFile.IsOnlineOnly` attribute flags (Recall / Pinned / hydrated `ReparsePoint`+`Offline` / stamped `FILE_ATTRIBUTE_OFFLINE`), `CloudFile.Exists` / `FilterLocalPaths`, `ImageDimensions` refusing online-only paths, `HashService` local hash + cloud stub, `GalleryMedia` provider-thumb / placeholder-tile / live-online-only preview rules, `RoomIcons.Normalize`, `ThumbFileName`, `CloudSourcePath`, `FolderGroups` (top-level `Selected / Sub / Sub` titles, 2-folder cap), `TagNameList`, `TagFilter` (Any/All/None + descendants), `TagPanelBuilder` (including Ungrouped group-name search, stable `BtnTagGroup_Ungrouped`, search All chips without `ParentId`, All / Uncategorized / Starred pinned first), `TagBoardExpand` (create/select opens the row that shows the tag), `TagSiblings` (root groups are siblings), `TagGroups` (Add/Move destinations, cycle-safe), `TagSelection` (rename baseline: new tag applies, same-tag edits/clears stay), `TagAlphaIndex` (A–Z letter buckets), `TagMosaicGroups` (longest path under the selected root, then Priority then name), and `RecentTags`. Do not add live OAuth to `ui-tests.ps1`. Recycle-delete and scan-size UI fixtures need a watched `PalaceUiTest`/`Temp` folder; without it those tests skip or fail. Magick.NET / TGA / EXR / HDR / PSD are out of scope until packaging is solved.

## Cursor Cloud specific instructions

Cloud Agents run on **Linux**, so the packaged WinUI 3 app (`Palace.csproj`) cannot build or run there — `BuildAndRun.ps1`, `winapp`, and `ui-tests.ps1` are Windows-only and must be run on a Windows host. The buildable, runnable surface on Linux is **`Palace.Tests`** (`net10.0`, off WinUI/WinRT).

The environment is repo-managed via `.cursor/environment.json`, whose `install` runs `.cursor/install.sh` to install the .NET 10 SDK into `$HOME/.dotnet` (added to `PATH`/`DOTNET_ROOT` in `~/.bashrc`) and warm a build of the test project. To verify the environment on Linux:

```bash
dotnet test ./Palace.Tests/Palace.Tests.csproj
```

Keep `Palace.Tests` cross-platform so it stays runnable here. Any Windows-only verification (the WinUI app, UI automation) must be done on Windows.
