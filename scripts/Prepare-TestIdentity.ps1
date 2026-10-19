<#
.SYNOPSIS
Prepares a separate test package identity for local Windows agent runs.

.DESCRIPTION
HARD rule: never register/launch release Identity C20A7629-....
Copies Package.appxmanifest to Package.appxmanifest.release-backup (once), then
rewrites Identity Name, PhoneProductId, and DisplayName. Does not call winapp.
Does not unregister anything. Restoring Sacb0y's installed C20A7629 package is
out of scope - only the repo manifest file is patched (restore from backup/git
after).

PhoneProductId is a deterministic GUID from the test name (not the release id).
DisplayName becomes "Palace Test <IdentityName>" on Properties and VisualElements
so a copied manifest cannot still look like the release package.

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
    Write-Error "Refusing IdentityName='$IdentityName' - that is the release identity. Choose e.g. PalaceOrganizeTest-5E1F."
    exit 1
}

function Get-TestPhoneProductId {
    param([string] $Name)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes("Palace.TestPhoneProductId:$Name"))
    } finally {
        $sha.Dispose()
    }
    $b = New-Object byte[] 16
    [System.Array]::Copy($hash, 0, $b, 0, 16)
    # Guid string order is little-endian for the third group, so the RFC 4122
    # version nibble lives in byte 7. Byte 8 is the RFC variant.
    $b[7] = [byte](($b[7] -band 0x0F) -bor 0x40)
    $b[8] = [byte](($b[8] -band 0x3F) -bor 0x80)
    $d1 = '{0:X2}{1:X2}{2:X2}{3:X2}' -f $b[3], $b[2], $b[1], $b[0]
    $d2 = '{0:X2}{1:X2}' -f $b[5], $b[4]
    $d3 = '{0:X2}{1:X2}' -f $b[7], $b[6]
    $d4 = '{0:X2}{1:X2}' -f $b[8], $b[9]
    $d5 = '{0:X2}{1:X2}{2:X2}{3:X2}{4:X2}{5:X2}' -f $b[10], $b[11], $b[12], $b[13], $b[14], $b[15]
    return "$d1-$d2-$d3-$d4-$d5"
}

function Set-ManifestField {
    param(
        [string] $Text,
        [string] $Pattern,
        [string] $Replacement,
        [string] $Already,
        [string] $Label
    )
    if ($Text.Contains($Already)) { return $Text }
    $updated = [regex]::Replace($Text, $Pattern, $Replacement, 1)
    if ($updated -eq $Text) {
        Write-Error "Could not rewrite $Label in Package.appxmanifest"
        exit 1
    }
    return $updated
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

$utf8 = New-Object System.Text.UTF8Encoding $false
$text = [System.IO.File]::ReadAllText($manifest, $utf8)
$phoneId = Get-TestPhoneProductId -Name $IdentityName
$display = "Palace Test $IdentityName"

if ($phoneId -eq $ReleaseIdentity -or $phoneId.StartsWith('C20A7629')) {
    Write-Error "Refusing generated PhoneProductId '$phoneId' (collides with the release identity)."
    exit 1
}

$newText = Set-ManifestField -Text $text `
    -Pattern '(<Identity\b[^>]*\bName=")[^"]+(")' `
    -Replacement "`${1}$IdentityName`${2}" `
    -Already "Name=`"$IdentityName`"" `
    -Label 'Identity Name'

$newText = Set-ManifestField -Text $newText `
    -Pattern '(PhoneProductId=")[^"]+(")' `
    -Replacement "`${1}$phoneId`${2}" `
    -Already "PhoneProductId=`"$phoneId`"" `
    -Label 'PhoneProductId'

$newText = Set-ManifestField -Text $newText `
    -Pattern '(<DisplayName>)[^<]*(</DisplayName>)' `
    -Replacement "`${1}$display`${2}" `
    -Already "<DisplayName>$display</DisplayName>" `
    -Label 'DisplayName'

$newText = Set-ManifestField -Text $newText `
    -Pattern '(<uap:VisualElements\b[^>]*\bDisplayName=")[^"]+(")' `
    -Replacement "`${1}$display`${2}" `
    -Already "DisplayName=`"$display`"" `
    -Label 'VisualElements DisplayName'

if ($newText -match [regex]::Escape($ReleaseIdentity)) {
    Write-Error 'Package.appxmanifest still contains the release identity after rewrite.'
    exit 1
}
if ($newText -notmatch [regex]::Escape("Name=`"$IdentityName`"")) {
    Write-Error 'Identity Name was not set.'
    exit 1
}
if ($newText -notmatch [regex]::Escape("PhoneProductId=`"$phoneId`"")) {
    Write-Error 'PhoneProductId was not set.'
    exit 1
}
if ($newText -notmatch [regex]::Escape("<DisplayName>$display</DisplayName>")) {
    Write-Error 'Properties DisplayName was not set.'
    exit 1
}
if ($newText -notmatch [regex]::Escape("DisplayName=`"$display`"")) {
    Write-Error 'VisualElements DisplayName was not set.'
    exit 1
}

[System.IO.File]::WriteAllText($manifest, $newText, $utf8)
Write-Host "Package.appxmanifest Identity Name -> $IdentityName"
Write-Host "Package.appxmanifest PhoneProductId -> $phoneId"
Write-Host "Package.appxmanifest DisplayName -> $display"
Write-Host ""
Write-Host "Next:"
Write-Host "  `$env:PALACE_DATA_DIR = Join-Path `$env:TEMP 'palace-agent-data-$IdentityName'"
Write-Host "  # temp SFW media copy only - never real catalog / Unity / %TEMP%\palace-vss"
Write-Host "  .\BuildAndRun.ps1 . --arch x64"
Write-Host ""
Write-Host "After testing, restore the *repo file* (not Sacb0y's install):"
Write-Host "  Copy-Item -LiteralPath '$backup' -Destination '$manifest' -Force"
Write-Host "  # or: git checkout -- Package.appxmanifest"
Write-Host ""
Write-Host "Never winapp unregister C20A7629. Agents do not restore that install."
