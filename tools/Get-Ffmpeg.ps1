<#
  Fetch Palace's pinned slim LGPL ffmpeg build into ThirdParty\ffmpeg.
  The zip is produced by tools/build-ffmpeg.sh in CI (.github/workflows/ffmpeg.yml)
  and hosted as a release asset on sacb0y/Palace; the SHA-256 below pins it.
  Contents: bin\ffmpeg.exe (single static exe), LICENSE.txt (LGPL-2.1),
  LICENSE-dav1d.txt, SOURCE.txt (source offer + exact configure line).
  No-op when the pinned build is already present. Palace.csproj runs this on build.
  Plain .NET only (no Utility/Archive cmdlets): MSBuild may start Windows
  PowerShell from pwsh with a PSModulePath where those modules don't load.
#>
param(
    [string]$Url = 'https://github.com/sacb0y/Palace/releases/download/ffmpeg-8.1.3-palace1/palace-ffmpeg-8.1.3-win64-lgpl.zip',
    [string]$Sha256 = '8aa1e8f4520e3533cb646221a5a34ff316705712ec9f74ae53d886e9bd9b8484'
)
$ErrorActionPreference = 'Stop'
$expected = $Sha256.ToLowerInvariant()
$root = [IO.Path]::GetDirectoryName($PSScriptRoot)
$dest = [IO.Path]::Combine($root, 'ThirdParty', 'ffmpeg')
$stamp = [IO.Path]::Combine($dest, '.sha256')
if ([IO.File]::Exists([IO.Path]::Combine($dest, 'bin', 'ffmpeg.exe')) -and [IO.File]::Exists($stamp) -and
    ([IO.File]::ReadAllText($stamp).Trim() -eq $expected)) { exit 0 }
$zip = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'palace-ffmpeg-' + [guid]::NewGuid().ToString('N') + '.zip')
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    (New-Object Net.WebClient).DownloadFile($Url, $zip)
    $sha = [Security.Cryptography.SHA256]::Create()
    $fs = [IO.File]::OpenRead($zip)
    try { $hash = -join ($sha.ComputeHash($fs) | ForEach-Object { $_.ToString('x2') }) } finally { $fs.Dispose() }
    if ($hash -ne $expected) { throw "ffmpeg SHA-256 mismatch: got $hash, expected $expected" }
    if ([IO.Directory]::Exists($dest)) { [IO.Directory]::Delete($dest, $true) }   # drop any older/other build
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $dest)
    [IO.File]::WriteAllText($stamp, $hash)
    [Console]::WriteLine("ffmpeg ready in $dest")
} finally {
    if ([IO.File]::Exists($zip)) { [IO.File]::Delete($zip) }
}
