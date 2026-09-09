<#
.SYNOPSIS
Forwards to the winui-dev-workflow BuildAndRun.ps1 so analyzer injection stays intact.
#>
$ErrorActionPreference = 'Stop'
$skill = Join-Path $env:USERPROFILE '.cursor\skills\winui-dev-workflow\BuildAndRun.ps1'
if (-not (Test-Path -LiteralPath $skill)) {
    Write-Error "winui-dev-workflow BuildAndRun.ps1 not found at $skill. Run /winui-setup and reinstall the WinUI skills."
    exit 1
}
& $skill @args
exit $LASTEXITCODE
