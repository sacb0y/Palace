## Version

- **New version:** <!-- e.g. 0.0.5 — required when the app changes; omit only with `no-version-bump` or docs/CI/tests-only paths -->
- **User-facing summary (one line):** <!-- plain wording for Isiac / changelog handoff -->

Bump `<PalaceVersion>` in `Directory.Build.props`, then `python3 scripts/sync-appx-version.py` (Identity = MAJOR.MINOR.PATCH.**0**). See [`AGENTS.md`](../AGENTS.md) and `.cursor/rules/versioning-release.mdc`.

## Summary

<!-- What changed and why (user-visible first). -->

## Goals check

Read [`AGENTS.md`](../AGENTS.md) — especially **PR / agent goals** and **Version**.

- [ ] Stays on **0.x** until Store **1.0.0** (PATCH for library/viewer polish; MINOR only for finished milestones)
- [ ] Version bumped when the app changes (`Directory.Build.props` + synced `Package.appxmanifest`); milestone only when opening a new minor
- [ ] Does not claim Rooms or Organization are finished
- [ ] No secrets; no GitHub visibility changes

## Test plan

- [ ] `dotnet test .\Palace.Tests\Palace.Tests.csproj`
- [ ] Packaged Debug (if UI): test identity via `Prepare-TestIdentity.ps1` then `.\BuildAndRun.ps1` — never `C20A7629`
