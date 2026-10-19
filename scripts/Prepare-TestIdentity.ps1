<#
.SYNOPSIS
Prepares a separate test package identity for local Windows agent runs.

.DESCRIPTION
HARD rule: never register/launch release Identity C20A7629-….
Copies Package.appxmanifest to Package.appxmanifest.release-backup (once), then
rewrites Identity Name to -IdentityName. Does not call winapp. Does not
unregister anything. Restoring Sacb0y's installed C20A7629 package is out of
scope — only the repo manifest file is patched (restore from backup/git after).

.EXAMPLE
.\scripts\Prepare-TestIdentity.ps1 -IdentityName PalaceOrganizeTest-5E1F
$env:PALACE_DATA_DIR = Join-Path $env:TEMP 'palace-agent-data-organize'
.\BuildAndRun.ps1 . --arch x64
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z][A-Za-z0-9._-]{2,49}$')]
    [string] $IdentityName,

    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
$ReleaseIdentity = 'C20A7629-6BDE-449F-958A-5AAF8BCE88F3'

if ($IdentityName -eq $ReleaseIdentity -or $IdentityName.StartsWith('C20A7629')) {
    Write-Error "Refusing IdentityName='$IdentityName' — that is the release identity. Choose e.g. PalaceOrganizeTest-5E1F."
    exit 1
}

$manifest = Join-Path $RepoRoot 'Package.appxmanifest'
if (-not (Test-Path -LiteralPath $manifest)) {
    Write-Error "No Package.appxmanifest at $manifest"
    exit 1
}

$backup = Join-Path $RepoRoot 'Package.appxmanifest.release-backup'
if (-not (Test-Path -LiteralPath $backup)) {
    Copy-Item -LiteralPath $manifest -Destination $backup -Force
    Write-Host "Backed up release manifest -> $backup"
} else {
    Write-Host "Keeping existing backup $backup"
}

$text = Get-Content -LiteralPath $manifest -Raw -Encoding UTF8
$pattern = '(<Identity\b[^>]*\bName=")[^"]+(")'
$newText = [regex]::Replace($text, $pattern, "`${1}$IdentityName`${2}", 1)
if ($newText -eq $text -and $text -notmatch [regex]::Escape("Name=`"$IdentityName`"")) {
    Write-Error 'Could not rewrite Identity Name in Package.appxmanifest'
    exit 1
}

# UTF-8 without BOM; preserve final newline if present
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($manifest, $newText, $utf8)
Write-Host "Package.appxmanifest Identity Name -> $IdentityName"
Write-Host ""
Write-Host "Next:"
Write-Host "  `$env:PALACE_DATA_DIR = Join-Path `$env:TEMP 'palace-agent-data-$IdentityName'"
Write-Host "  # temp SFW media copy only — never real catalog / Unity / %TEMP%\palace-vss"
Write-Host "  .\BuildAndRun.ps1 . --arch x64"
Write-Host ""
Write-Host "After testing, restore the *repo file* (not Sacb0y's install):"
Write-Host "  Copy-Item -LiteralPath '$backup' -Destination '$manifest' -Force"
Write-Host "  # or: git checkout -- Package.appxmanifest"
Write-Host ""
Write-Host "Never winapp unregister C20A7629. Agents do not restore that install."
