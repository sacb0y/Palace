<#
  Fetch a pinned LGPL (shared) ffmpeg build for codec-free video posters.
  Only ffmpeg.exe and the DLLs it imports are kept (~175 MB unpacked).
  Source: https://github.com/BtbN/FFmpeg-Builds (LGPL-2.1+, no GPL/nonfree parts).
  BtbN prunes old autobuilds: if the pinned URL 404s, update $Url/$Sha256
  to a newer "win64-lgpl-shared" asset and its published sha256.
#>
param(
    [string]$Url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-15-13-03/ffmpeg-n8.1.3-18-g46d899d7a0-win64-lgpl-shared-8.1.zip',
    [string]$Sha256 = '4acd82323c2cad9c7ffea5b8e335ebcda39beae83a3138d729545e5a7666a3fd'
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'  # PS 5.1 progress bar makes large downloads crawl
$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root 'ThirdParty\ffmpeg'
$zip = Join-Path ([IO.Path]::GetTempPath()) ('palace-ffmpeg-' + [guid]::NewGuid().ToString('N') + '.zip')
$tmp = $zip + '.d'
try {
    Invoke-WebRequest -Uri $Url -OutFile $zip -UseBasicParsing
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $Sha256.ToLowerInvariant()) { throw "SHA-256 mismatch: $hash" }
    Expand-Archive $zip $tmp
    $top = Get-ChildItem $tmp -Directory | Select-Object -First 1
    New-Item -ItemType Directory -Force (Join-Path $dest 'bin') | Out-Null
    $keep = 'ffmpeg.exe','avcodec-*.dll','avformat-*.dll','avutil-*.dll','swscale-*.dll','swresample-*.dll','avfilter-*.dll','avdevice-*.dll'
    foreach ($k in $keep) { Copy-Item (Join-Path $top.FullName "bin\$k") (Join-Path $dest 'bin') -Force }
    Copy-Item (Join-Path $top.FullName 'LICENSE.txt') $dest -Force
    Set-Content (Join-Path $dest 'SOURCE.txt') "$Url`nsha256 $Sha256"
    Write-Host "ffmpeg ready in $dest"
} finally {
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
