## Summary

<!-- What changed and why (user-visible first). -->

## Goals check

Read [`AGENTS.md`](../AGENTS.md) — especially **PR / agent goals** and **Version**.

- [ ] Stays on current **0.0.x Library** slice (no invented Rooms canvas / gamedev / AI unless that minor is open)
- [ ] Version bump only if this is a release tip — keep `Package.appxmanifest` + `Palace.csproj` + `AppVersion.Milestone` in lockstep
- [ ] Does not claim Rooms or Organization are finished
- [ ] No secrets; no GitHub visibility changes

## Test plan

- [ ] `dotnet test .\Palace.Tests\Palace.Tests.csproj`
- [ ] Packaged Debug (if UI): `.\BuildAndRun.ps1 . --arch x64`
