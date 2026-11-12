#Requires -Version 5.1
<#
.SYNOPSIS
  Build palace_mesh3d.dll (x64) for the Diligent SwapChainPanel spike.

.PARAMETER WithDiligent
  Define PALACE_MESH3D_WITH_DILIGENT and expect Diligent clones under -DiligentRoot.

.PARAMETER DiligentRoot
  Parent folder containing DiligentCore, DiligentTools, DiligentFX.
#>
[CmdletBinding()]
param(
    [switch]$WithDiligent,
    [string]$DiligentRoot = ""
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$src = Join-Path $root 'Native\mesh3d'
$out = Join-Path $src 'win-x64'
$build = Join-Path $src 'build'

New-Item -ItemType Directory -Force -Path $out | Out-Null
New-Item -ItemType Directory -Force -Path $build | Out-Null

$cmakeArgs = @(
    '-S', $src,
    '-B', $build,
    '-A', 'x64',
    "-DCMAKE_BUILD_TYPE=Release"
)

if ($WithDiligent) {
    if (-not $DiligentRoot) {
        throw 'Pass -DiligentRoot when using -WithDiligent'
    }
    $cmakeArgs += '-DPALACE_MESH3D_WITH_DILIGENT=ON'
    $cmakeArgs += "-DDILIGENT_ROOT=$DiligentRoot"
} else {
    $cmakeArgs += '-DPALACE_MESH3D_WITH_DILIGENT=OFF'
}

Write-Host "cmake $($cmakeArgs -join ' ')"
& cmake @cmakeArgs
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed ($LASTEXITCODE)" }

& cmake --build $build --config Release
if ($LASTEXITCODE -ne 0) { throw "cmake build failed ($LASTEXITCODE)" }

$dll = Get-ChildItem -Path $build -Recurse -Filter 'palace_mesh3d.dll' | Select-Object -First 1
if (-not $dll) { throw 'palace_mesh3d.dll not found after build' }
Copy-Item -Force $dll.FullName (Join-Path $out 'palace_mesh3d.dll')
Copy-Item -Force (Join-Path $src 'SOURCE.txt') (Join-Path $out 'SOURCE.txt')
Copy-Item -Force (Join-Path $src 'THIRD_PARTY.md') (Join-Path $out 'THIRD_PARTY.md')
Write-Host "Wrote $($out)\palace_mesh3d.dll"
