<#
.SYNOPSIS
Builds and runs Palace under a *test* package identity only.

HARD: refuses Identity Name C20A7629-… (Sacb0y's release install). Even
`winapp run --no-launch` registers layout and can overwrite that install.
Prepare a separate identity first:
  .\scripts\Prepare-TestIdentity.ps1 -IdentityName PalaceOrganizeTest-5E1F

Forwards to the winui-dev-workflow BuildAndRun.ps1 when that skill ships one
(older skills), otherwise falls back to project-mode `winapp run` (WinApp CLI 0.7+).

.EXAMPLE
.\scripts\Prepare-TestIdentity.ps1 -IdentityName PalaceOrganizeTest-5E1F
$env:PALACE_DATA_DIR = Join-Path $env:TEMP 'palace-agent-data-organize'
.\BuildAndRun.ps1 . --arch x64
#>
$ErrorActionPreference = 'Stop'

$ReleaseIdentity = 'C20A7629-6BDE-449F-958A-5AAF8BCE88F3'

function Get-PalaceManifestPath {
    param([string[]] $CliArgs)
    $candidates = @()
    if ($CliArgs.Count -eq 0) {
        $candidates += (Join-Path (Get-Location).Path 'Package.appxmanifest')
    }
    foreach ($a in $CliArgs) {
        if ($a -like '-*') { continue }
        $p = $a
        try { $p = (Resolve-Path -LiteralPath $a -ErrorAction Stop).Path } catch { continue }
        if (Test-Path -LiteralPath $p -PathType Leaf) {
            $dir = Split-Path -Parent $p
            $candidates += (Join-Path $dir 'Package.appxmanifest')
        } elseif (Test-Path -LiteralPath $p -PathType Container) {
            $candidates += (Join-Path $p 'Package.appxmanifest')
        }
    }
    $candidates += (Join-Path $PSScriptRoot 'Package.appxmanifest')
    foreach ($c in $candidates) {
        if ($c -and (Test-Path -LiteralPath $c)) { return $c }
    }
    return $null
}

function Assert-NotReleaseIdentity {
    param([string[]] $CliArgs)
    $manifest = Get-PalaceManifestPath -CliArgs $CliArgs
    if (-not $manifest) {
        Write-Error @"
BuildAndRun.ps1: could not find Package.appxmanifest next to the project.
HARD rule: never register/launch release identity $ReleaseIdentity.
Use .\scripts\Prepare-TestIdentity.ps1 -IdentityName <TestName> first.
"@
        exit 1
    }
    $text = Get-Content -LiteralPath $manifest -Raw -Encoding UTF8
    if ($text -match 'Name="(C20A7629-6BDE-449F-958A-5AAF8BCE88F3)"') {
        Write-Error @"
REFUSED: Package.appxmanifest Identity Name is $ReleaseIdentity (Sacb0y's release install).

Even ``winapp run --no-launch`` registers layout and can overwrite that install.
Never Add-AppxPackage / winapp unregister against C20A7629 — agents do not restore it.

Prepare a separate test identity, then retry:
  .\scripts\Prepare-TestIdentity.ps1 -IdentityName PalaceOrganizeTest-5E1F
  `$env:PALACE_DATA_DIR = Join-Path `$env:TEMP 'palace-agent-data-organize'
  # temp SFW copy only — never real catalog / Unity / %TEMP%\palace-vss
  .\BuildAndRun.ps1 . --arch x64

See AGENTS.md and .cursor/rules/local-test-identity.mdc.
"@
        exit 1
    }
}

Assert-NotReleaseIdentity -CliArgs $args

$skill = Join-Path $env:USERPROFILE '.cursor\skills\winui-dev-workflow\BuildAndRun.ps1'
if (Test-Path -LiteralPath $skill) {
    & $skill @args
    exit $LASTEXITCODE
}

if (-not (Get-Command winapp -ErrorAction SilentlyContinue)) {
    Write-Error 'winapp not found on PATH. Run /winui-setup (winget install --id Microsoft.WinAppCli).'
    exit 1
}

$runArgs = if ($args.Count -gt 0) { $args } else { @('.') }
& winapp run @runArgs
exit $LASTEXITCODE
