<#
.SYNOPSIS
Builds and runs Palace. Forwards to the winui-dev-workflow BuildAndRun.ps1 when that skill ships one
(older skills), otherwise falls back to project-mode `winapp run` (WinApp CLI 0.7+).
.EXAMPLE
.\BuildAndRun.ps1 . --arch x64
#>
$ErrorActionPreference = 'Stop'
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
