## Version

- **New version:** <!-- e.g. 0.3.118 — required when the app changes; omit only with `no-version-bump` or docs/CI/tests-only paths. Set/confirm at merge time; PR title must match the shipped version. -->
- **User-facing summary (one line):** <!-- plain wording for Isiac / changelog handoff; required when bumping -->

Bump `<PalaceVersion>` in `Directory.Build.props`, then `python3 scripts/sync-appx-version.py` (Identity = MAJOR.MINOR.PATCH.**0**). See [`AGENTS.md`](../AGENTS.md) and `.cursor/rules/versioning-release.mdc`. Docs/CI/tests-only: add label `no-version-bump` **or** keep the diff on exempt paths.

## Summary

<!-- What changed and why (user-visible first). New scripts: why existing paths in scripts-helpers.mdc are insufficient. -->

## Process

- Open-PR cap **≤2–3** open **non-draft** PRs (parked drafts do not count). **Do not merge until Isiac says merge in his own words.**
- The Project merges with **merge commits** (not squash).
- UI PRs need screenshot OK from **both Wimbly and Doktor**.
- Long-lived `no-version-bump` drafts must be **re-bumped at merge**.

## Goals check

Read [`AGENTS.md`](../AGENTS.md) — especially **Safety**, **Versioning and changelog**, and **Process and design** (open-PR cap ≤2–3; version at merge; never `C20A7629`).

- [ ] Stays on **0.x** until Store **1.0.0** (PATCH for library/viewer polish; MINOR only for finished milestones)
- [ ] Version bumped when the app changes (`Directory.Build.props` + synced `Package.appxmanifest`), **or** `no-version-bump` / docs-exempt; title matches shipped version
- [ ] Does not claim Rooms or Organization are finished
- [ ] No secrets; no GitHub visibility changes; never Identity `C20A7629`

## Test plan

- [ ] `dotnet test .\Palace.Tests\Palace.Tests.csproj`
- [ ] Packaged Debug (if UI): test identity via `Prepare-TestIdentity.ps1 -IdentityName PalaceOrganizeTest-5E1F` then `.\BuildAndRun.ps1` — never `C20A7629`
- [ ] FenrirDesktop UI only with `-AllowDesktop -OverrideReason '…'` when required (HDR); FENRIR-STUDIO primary for non-HDR UI
- [ ] Never change Windows display scale / DPI on Studio or Desktop
