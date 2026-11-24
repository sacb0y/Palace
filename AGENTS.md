# Palace — agent notes

## Safety (HARD — read first)

- **Never** launch, register, unregister, or install over release Identity **Name** `C20A7629-…`. Never `winapp run` / `Add-AppxPackage` / `BuildAndRun.ps1` / `winapp run --no-launch` on the release manifest (layout register overwrites the install). Agents do **not** restore `C20A7629` — Sacb0y owns that.
- **UI primary host:** **FENRIR-STUDIO** (laptop). Exact test identity: `PalaceOrganizeTest-5E1F` via `.\scripts\Prepare-TestIdentity.ps1 -IdentityName PalaceOrganizeTest-5E1F`. Set `PALACE_DATA_DIR` to a **temp** folder. Register the **build output** `bin\...\AppxManifest.xml`, not `winapp run --no-launch` on source. See `.cursor/rules/local-test-identity.mdc`.
- **FenrirDesktop** is for HDR output verification / rare authorized desktop runs only. `ui-tests.ps1` **refuses** `COMPUTERNAME=FenrirDesktop` unless **both** `-AllowDesktop` and a non-empty `-OverrideReason` are passed (logged; still no DPI flips / S7). Never default to desktop.
- **Never change Windows display scale / DPI** on FENRIR-STUDIO or FenrirDesktop. S7 / Studio harness must not flip system DPI — only process DPI awareness inside the harness.
- **Forbidden paths / trees (never touch):** installed Palace; real catalog (`%LOCALAPPDATA%\Palace` / **`palace.db`**); `%TEMP%\palace-vss`; `winapp-layout\palace-main-*`; Unity / Our Apartment; **MOMOCore** / **MOMOCore-Dev**; **momoirosoft-core** consumer checkouts used as live titles; **Isiac's personal and 3D test asset files** (exact list is in the Project store — `docs/forbidden-personal-assets.md`); `Palace-Dev` as a live library. Test data = temp copy of the SFW Palace-Dev folder only. Seed the catalog directly — no GUI folder picker.
- **Worktrees (HARD):** one Palace checkout by default (switch branches there). Extra worktrees only for truly concurrent jobs; **≤8** extras; remove yours when the PR merges/closes or the job ends. Cap hygiene: `scripts/wt-list-prune-safe.ps1 -Repo palace` (default repo is momoirosoft — always pass `-Repo palace`).
- Cleanup: remove named **test** identities only. If the target PC is unreachable, skip and report — never substitute another PC. Before test input: confirm the user is idle; never maximize the test window; screenshot only the test window. Shared machines: BelowNormal `-m:1`.

Packaged WinUI 3 / Windows App SDK digital asset manager (images, GIF, video; more later). Metaphor: **Library** (catalog) and **Room** (moodboard / mindmap). Files stay on disk; SQLite + FTS5 is the index.

Human overview: `README.md`. Bug reports: Settings → About (`TxtAppVersion`).

## Keeping these notes current

This file is the always-loaded core. Topic rules live in `.cursor/rules/*.mdc` and load by glob. Change a convention → update the **matching** `.mdc` in the same change. Cross-cutting contracts only belong here. New topic → new `.mdc` with `description` + `globs`.

| Rule file | Covers |
|---|---|
| `product-tags-library.mdc` | Projects, delete, organize, Tags board, Library mosaic |
| `library-ui.mdc` | Freeze rules, busy bars, startup, mosaic, lazy thumbs, overlay |
| `gallery-hdr.mdc` | Overlay / GalleryWindow, GIF, scale modes, HDR, histogram, posters |
| `cloud-files.mdc` | On-Demand + API; never download originals; hydrate on Open |
| `catalog-sqlite.mdc` | Schema / migrations, SQL, `PalaceDb` gate, OAuth storage |
| `errors.mdc` | Log and Notify; do not crash |
| `icons-automation.mdc` | FluentIcons, AutomationIds, badges |
| `code-map.mdc` | Which file owns which area |
| `versioning-release.mdc` | `PalaceVersion`, bump rules, PR summary, MSIX, trim |
| `local-test-identity.mdc` | HARD: never touch release Identity `C20A7629`; ui-tests exact `PalaceOrganizeTest-5E1F`; FenrirDesktop refuse unless `-AllowDesktop -OverrideReason`; StudioDpiHarness |
| `tests.mdc` | Test commands and `Palace.Tests` coverage |
| `scripts-helpers.mdc` | Existing scripts table; no redundant helpers; new-script justification |
| `lifetime-hooks.mdc` | Loaded/Unloaded re-hook; OwnedDelegate; no ReferenceEquals+cast |
| `worktrees.mdc` | FENRIR-STUDIO one checkout; ≤8; prune via `wt-list-prune-safe.ps1` |

## Versioning and changelog

- Source of truth: `<PalaceVersion>` in `Directory.Build.props` (Assembly / File / Informational follow it). Manifest `Identity Version` = `MAJOR.MINOR.PATCH.**0**` — sync with `python3 scripts/sync-appx-version.py`. CI enforces props ↔ manifest match and a bump over `main` for app-changing PRs.
- Stay on **0.x** until the first public Store release (**1.0.0**). **PATCH** = fixes, polish, small features, library work. **MINOR** = finished milestones only (Settings restructure landed at **0.3.0**). Docs / CI / tests-only: no bump (`no-version-bump` label or exempt paths). Version follows merge order; rebase and re-bump stacked PRs.
- **Version at merge:** set / confirm the shipped version when merging (not when opening a long-lived draft). The **PR title must match** the version that actually ships (lesson: #196 0.3.82→0.3.84). Long-lived drafts may carry `no-version-bump` while open; **they must be re-bumped at merge** (props + synced manifest + title) before landing.
- Bumping PRs must state the **new version** and a **one-line user-facing summary** (for Isiac). Keep `Helpers/AppVersion.cs` `Milestone` in lockstep when opening a milestone. Do not jump to `1.0.0` because something “feels ready.”
- No `CHANGELOG.md` in-repo. Maintainers record version-bump merges in an external changelog.

## Process and design

- Prefer small PRs from current `main`. **Open-PR cap: ≤2–3** open **non-draft** Palace PRs (parked drafts like #192 do not count). Maintainers merge when CI is green and a manual retest passes. Do not rely on Bugbot. **Do not merge until Isiac says merge in his own words.** The Project merges with **merge commits** (not squash).
- **Worktrees (FENRIR-STUDIO):** one Palace checkout by default — switch branches there. Extra worktrees only for truly concurrent jobs; **≤8** extras; reuse one per branch; remove yours when the PR merges/closes or the job ends. Cap hygiene: `scripts/wt-list-prune-safe.ps1 -Repo palace` (see `worktrees.mdc`).
- UI / user-visible PRs need screenshot OK from **both Wimbly and Doktor** before merge.
- New UI-heavy features need Gooey mockups that Isiac approves **before** build. Other UI changes still get design input first. No release pressure.
- Library-first: mosaic, overlay/gallery, tags, watch folders, FTS, solid image/video (incl. HDR present). Do not invent Rooms canvas, gamedev viewers, or AI features until that minor is opened. Existing Rooms / Organization are infancy — polish only if the task names them.
- Cloud (Dropbox / OneDrive OAuth, On-Demand) may land inside the current identity; not a numbered slice; never download originals.
- Icons: FluentIcons.WinUI (`ic:FluentIcon`). Confirm the size variant exists (e.g. `ratio_one_to_one` / `scale_fill` start at Size20).
- Viewer overlays: same look in Light and Dark (dark translucent plates; no acrylic over HDR). High contrast uses system colors. Reusable styles: `ViewerOverlayButtonStyle`, `ViewerDetailsCard`, `VolumeControl`, `TagChip`. Card surface `#E01C1C1C` (`#1C1C1CE0` in CSS), border `#1AFFFFFF`, radius 8.
- Overlay hit-testing: keep interactive cards `IsHitTestVisible`. Navigation via `SelectionChanged` and UI Automation, not pointer-only.
- Hands-off: packaged Debug under a **test** identity only; never unpackaged; never change GitHub visibility or add secrets; skip speculative Magick / TGA spikes unless asked (EXR / Radiance / JXL / AVIF present already ship).
- Every UI test report includes captioned screenshots. Host PR screenshots on the media branch — never commit PNGs on the product branch.
- Push from temp clones only.
- Unit / helper changes: `dotnet test .\Palace.Tests\Palace.Tests.csproj`. Linux Cloud Agents: tests only (no WinUI). Live UI needs a self-hosted Windows worker.

## Stack (locked)

- Packaged `winui-mvvm` only. CommunityToolkit.Mvvm, `{x:Bind}` with explicit `Mode`, `AutomationProperties.AutomationId` on interactive controls, `ThemeResource` (not hardcoded brushes).
- Never run the unpackaged `.exe`. Never set `WindowsPackageType=None`.
- `Palace.Tests` is a separate `net10.0` project (excluded from `Palace.csproj`). Keep tests off WinUI / WinRT / `FluentIcons.WinUI`.
- Debug and Release of the same source share one version. `PublishTrimmed` is off until Release trim stops crashing on `ItemsSource` COM wrappers; re-enable only with `SelfContained` + `WindowsAppSDKSelfContained`.
- Title bar: `AppTitleBar.Title` = `AppVersion.TitleBarTitle` (`Palace {version}`); `Subtitle` = `AppVersion.TitleBar`.

## Architecture (current product)

- WinUI 3, .NET 10, Windows App SDK, MSIX (Store).
- Settings (0.2.6+): two-pane with search. Sections: Appearance, Library & Folders, Viewer, Tags & Rooms, Input, Performance, About. CommunityToolkit SettingsCard / Expander.
- Details card: single info area (Info flyout merged in); overlays the viewer; never resizes the image.
- HDR: scRGB / FP16 path; tonemap to SDR on SDR displays. PNG cICP HDR from 0.2.7.
- Exports: PNG / JPEG / JXR / AVIF / EXR with HDR→SDR tonemap. 360 panoramas get a thumbnail badge.
- Planned: palace CLI for agents (JSON, previews to PNG); no MCP yet; Chromium extension later.
- Dropbox cloud library: chosen folders per Project; read-only at first.
- Unified Undo: moves and organize runs share one history; an organize run is one step; undoing a move removes its folder if empty.
- Thumbnails: ~2–4 concurrent decodes, lazy in viewport order, short prefetch, cancel on scroll-away, full decodes off the UI thread.
- Accent: Follow Windows or Palace pink (`#ED9FBC` Dark, `#A14165` Light). Retint in `OnLaunched`; do not touch `Application.Resources` in the App constructor.
- Backgrounds: 13 presets plus Default, Mica, Mica Alt, Neutral, Custom. Recent custom images kept as copies; blur / darkness per entry.
- Roadmap: Settings **0.3.0** done; Library / tags / HDR present polish continue on **0.3.x**. **Next:** Smooth GPU viewer **V0–V4** (Diligent presenter → input/HUD), then **3D**, then audio / Rooms moodboard.

## PR descriptions

Short. Use `.github/PULL_REQUEST_TEMPLATE.md`. Bumping PRs fill **New version** + **User-facing summary** (title must match the version that ships). Docs/CI/tests-only: `no-version-bump` or stay on exempt paths. Also What / Why / Tested. No essays or screenshot dumps unless the change is visual (then captioned shots on the media branch). New scripts: justify vs the table in `scripts-helpers.mdc`.

## Cursor Cloud specific instructions

Cloud Agents run on **Linux**. Only `Palace.Tests` (`net10.0`, off WinUI/WinRT) builds here — keep those tests cross-platform.

```bash
dotnet test ./Palace.Tests/Palace.Tests.csproj
```

WinUI / `winapp` / `ui-tests.ps1` / live screenshots need a self-hosted Windows worker. Check `cursor-cloud` `list-self-hosted-workers`. If none is connected (or Developer Mode / checkout missing), report that — do not invent “no Windows access” without checking. HDR verification targets **FenrirDesktop** (with `-AllowDesktop` + `-OverrideReason` when driving UI tests).

Environment: `.cursor/environment.json` → `.cursor/install.sh` installs the .NET 10 SDK into `$HOME/.dotnet` and warms the test project.
