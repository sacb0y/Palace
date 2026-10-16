<#
  Fetch Palace's pinned slim LGPL ffmpeg build into ThirdParty\ffmpeg.
  The zip is produced by tools/build-ffmpeg.sh in CI (.github/workflows/ffmpeg.yml)
  and hosted as a release asset on sacb0y/Palace; the SHA-256 below pins it.
  Contents: bin\ffmpeg.exe (single static exe), LICENSE.txt (LGPL-2.1),
  LICENSE-dav1d.txt, SOURCE.txt (source offer + exact configure line).
  No-op when the pinned build is already present. Palace.csproj runs this on build.
#>
param(
    [string]$Url = 'https://github.com/sacb0y/Palace/releases/download/ffmpeg-8.1.3-palace1/palace-ffmpeg-8.1.3-win64-lgpl.zip',
    [string]$Sha256 = '8aa1e8f4520e3533cb646221a5a34ff316705712ec9f74ae53d886e9bd9b8484'
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'  # PS 5.1 progress bar makes downloads crawl
$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root 'ThirdParty\ffmpeg'
$stamp = Join-Path $dest '.sha256'
if ((Test-Path (Join-Path $dest 'bin\ffmpeg.exe')) -and (Test-Path $stamp) -and
    ((Get-Content $stamp -Raw).Trim() -eq $Sha256.ToLowerInvariant())) { return }
$zip = Join-Path ([IO.Path]::GetTempPath()) ('palace-ffmpeg-' + [guid]::NewGuid().ToString('N') + '.zip')
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $Url -OutFile $zip -UseBasicParsing
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $Sha256.ToLowerInvariant()) { throw "ffmpeg SHA-256 mismatch: got $hash, expected $Sha256" }
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }   # drop any older/other build
    New-Item -ItemType Directory -Force $dest | Out-Null
    Expand-Archive $zip $dest
    Set-Content $stamp $hash -NoNewline
    Write-Host "ffmpeg ready in $dest"
} finally {
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
}
