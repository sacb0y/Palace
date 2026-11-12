param(
  [Parameter(Mandatory)][int]$AppPid,
  [Parameter(Mandatory)][string]$MediaDir,
  [string]$TipSha = '71eaf208530c0d0393aab0e6edaa842cc064f054'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
New-Item -ItemType Directory -Force -Path $MediaDir | Out-Null
$env:WINAPP_UI_WORKFLOW_ID = 'palace-196-overflow-shots'
$log = Join-Path $MediaDir 'capture-log.txt'
$clicks = Join-Path $MediaDir 'clicks.txt'
'' | Set-Content $clicks
function Log([string]$m){ $l="$(Get-Date -Format o) $m"; Add-Content $log $l; Write-Host $l }
function ClickLog([string]$m){ Add-Content $clicks $m; Log $m }

Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Cap196 {
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr a, int X, int Y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int n);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint f);
  [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern bool GetMonitorInfo(IntPtr h, ref MONITORINFO m);
  public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
  public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
}
"@

function Ui {
  param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
  $all = @('ui') + $Arguments + @('-a', "$AppPid")
  $global:LASTEXITCODE = $null
  $prev = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $output = & winapp @all 2>&1
  } finally {
    $ErrorActionPreference = $prev
  }
  $lines = @($output | ForEach-Object { "$_" })
  if ($null -eq $global:LASTEXITCODE -or $global:LASTEXITCODE -ne 0) {
    throw "winapp $($all -join ' ') failed ($global:LASTEXITCODE): $($lines -join "`n")"
  }
  return $lines
}

function Get-MainHwnd {
  $wins = (Ui list-windows --json) -join "`n" | ConvertFrom-Json
  $main = @($wins) | Where-Object { $_.hwnd -and $_.title -and $_.title -ne 'PopupHost' } | Select-Object -First 1
  if (-not $main) { throw 'No main window' }
  return [IntPtr][int64]$main.hwnd
}

function Force-Foreground([IntPtr]$Hwnd) {
  $fg = [Cap196]::GetForegroundWindow()
  $cur = [Cap196]::GetCurrentThreadId()
  [uint32]$fgPid = 0
  $fgTid = [Cap196]::GetWindowThreadProcessId($fg, [ref]$fgPid)
  [uint32]$tgPid = 0
  [void][Cap196]::GetWindowThreadProcessId($Hwnd, [ref]$tgPid)
  $attached = $false
  if ($fgTid -ne 0 -and $fgTid -ne $cur) {
    $attached = [Cap196]::AttachThreadInput($cur, $fgTid, $true)
  }
  try {
    [Cap196]::ShowWindow($Hwnd, 9) | Out-Null
    [Cap196]::BringWindowToTop($Hwnd) | Out-Null
    [Cap196]::SetForegroundWindow($Hwnd) | Out-Null
  } finally {
    if ($attached) { [Cap196]::AttachThreadInput($cur, $fgTid, $false) | Out-Null }
  }
  Start-Sleep -Milliseconds 200
}

function Resize-Dip([IntPtr]$Hwnd, [int]$DipW, [int]$DipH) {
  if ([Cap196]::IsZoomed($Hwnd)) { [Cap196]::ShowWindow($Hwnd, 9) | Out-Null; Start-Sleep -Milliseconds 250 }
  $dpi = [Cap196]::GetDpiForWindow($Hwnd); if ($dpi -le 0) { $dpi = 144 }
  $scale = $dpi / 96.0
  $physW = [int][math]::Round($DipW * $scale)
  $physH = [int][math]::Round($DipH * $scale)
  $mi = New-Object Cap196+MONITORINFO
  $mi.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($mi)
  [Cap196]::GetMonitorInfo([Cap196]::MonitorFromWindow($Hwnd, 2), [ref]$mi) | Out-Null
  $workW = $mi.rcWork.Right - $mi.rcWork.Left
  $workH = $mi.rcWork.Bottom - $mi.rcWork.Top
  $clamped = $false
  if ($physW -gt $workW - 20) { $physW = $workW - 20; $clamped = $true }
  if ($physH -gt $workH - 20) { $physH = $workH - 20; $clamped = $true }
  Force-Foreground $Hwnd
  [Cap196]::SetWindowPos($Hwnd, [IntPtr]::Zero, $mi.rcWork.Left + 10, $mi.rcWork.Top + 10, $physW, $physH, 0x0040) | Out-Null
  Start-Sleep -Milliseconds 700
  $r = New-Object Cap196+RECT
  [Cap196]::GetWindowRect($Hwnd, [ref]$r) | Out-Null
  $aw = [int][math]::Round(($r.Right - $r.Left) * 96.0 / $dpi)
  $ah = [int][math]::Round(($r.Bottom - $r.Top) * 96.0 / $dpi)
  $zoomed = [Cap196]::IsZoomed($Hwnd)
  Log ("Resize targetDIP={0}x{1} actualDIP={2}x{3} phys={4}x{5} work={6}x{7} clamped={8} dpi={9} zoomed={10}" -f $DipW,$DipH,$aw,$ah,$physW,$physH,$workW,$workH,$clamped,$dpi,$zoomed)
  return @{ DipW=$aw; DipH=$ah; Dpi=$dpi; Zoomed=$zoomed; PhysW=($r.Right-$r.Left); PhysH=($r.Bottom-$r.Top) }
}

function Shot([string]$Name) {
  $path = Join-Path $MediaDir $Name
  if (Test-Path $path) { Remove-Item $path -Force }
  Force-Foreground (Get-MainHwnd)
  Ui screenshot --capture-screen --output $path | Out-Null
  if (-not (Test-Path $path) -or (Get-Item $path).Length -lt 1000) { throw "Bad shot $path" }
  Log "SHOT $Name size=$((Get-Item $path).Length)"
}

function GalleryOpen {
  try {
    $g = (((Ui get-property GrdGalleryOverlay --json) -join "`n") | ConvertFrom-Json).element
    return (-not $g.isOffscreen -and [int]$g.width -gt 100)
  } catch { return $false }
}

function Close-Gallery {
  for ($i = 0; $i -lt 8; $i++) {
    if (-not (GalleryOpen)) { return }
    try { Ui send-keys escape --via send-input | Out-Null } catch {}
    Start-Sleep -Milliseconds 250
  }
  if (GalleryOpen) { throw 'gallery still open' }
}

Log "START tip=$TipSha pid=$AppPid"
$hwnd = Get-MainHwnd
$size = Resize-Dip $hwnd 1280 900
Close-Gallery

try { Ui wait-for GrdAssets -t 8000 | Out-Null } catch {
  ClickLog 'click NavLibrary'
  Ui click NavLibrary | Out-Null
  Ui wait-for GrdAssets -t 10000 | Out-Null
}

try {
  $ts = (((Ui get-property TglPreviewPane --json) -join "`n") | ConvertFrom-Json).properties.ToggleState
  if ("$ts" -eq 'On') {
    ClickLog 'invoke TglPreviewPane -> Off'
    Ui invoke TglPreviewPane | Out-Null
    Start-Sleep -Milliseconds 500
  }
} catch { Log "Preview toggle skipped: $_" }

try {
  $sm = (((Ui get-property TglSelectMode --json) -join "`n") | ConvertFrom-Json).properties.ToggleState
  if ("$sm" -eq 'On') {
    ClickLog 'invoke TglSelectMode -> Off'
    Ui invoke TglSelectMode | Out-Null
    Start-Sleep -Milliseconds 400
  }
} catch {}

# 1 IDLE
Force-Foreground $hwnd
ClickLog 'hover NavLibrary (idle - mouse off tiles)'
Ui hover NavLibrary --dwell-time 600 | Out-Null
Start-Sleep -Milliseconds 400
Shot '01-idle-grid-no-overflow.png'

# 2 HOVER
Force-Foreground $hwnd
ClickLog 'hover ListItem art-a.jpg (real pointer SendInput)'
Ui hover 'art-a.jpg' --dwell-time 1000 | Out-Null
Start-Sleep -Milliseconds 300
Shot '02-hover-tile-overflow.png'

# 3 KEYBOARD FOCUS
ClickLog 'hover NavLibrary then focus art-a.jpg (keyboard focus)'
Ui hover NavLibrary --dwell-time 400 | Out-Null
Start-Sleep -Milliseconds 200
Force-Foreground $hwnd
Ui focus 'art-a.jpg' | Out-Null
Start-Sleep -Milliseconds 500
$focused = (Ui get-focused --json) -join "`n"
Log "focused=$focused"
Shot '03-keyboard-focus-overflow.png'

# 4 SELECTED
ClickLog 'invoke TglSelectMode On'
Ui invoke TglSelectMode | Out-Null
Start-Sleep -Milliseconds 500
ClickLog 'click art-a.jpg to select'
Ui click 'art-a.jpg' | Out-Null
Start-Sleep -Milliseconds 500
Ui hover NavLibrary --dwell-time 400 | Out-Null
Start-Sleep -Milliseconds 300
Shot '04-selected-tile-overflow.png'

# 5 FLYOUT
Force-Foreground $hwnd
ClickLog 'hover art-a.jpg then click BtnAssetMore (flyout open)'
Ui hover 'art-a.jpg' --dwell-time 600 | Out-Null
Start-Sleep -Milliseconds 200
Ui click BtnAssetMore | Out-Null
Start-Sleep -Milliseconds 700
Shot '05-flyout-open-overflow.png'
try { Ui send-keys escape --via send-input | Out-Null } catch {}
Start-Sleep -Milliseconds 300

$pkg = Get-AppxPackage PalaceOrganizeTest-5E1F
$meta = @"
build_sha=$TipSha
dpi=$($size.Dpi)
scale_pct=$([math]::Round(100.0*$size.Dpi/96.0))
identity=PalaceOrganizeTest-5E1F
version=$($pkg.Version)
maximized=$($size.Zoomed)
window_dip=$($size.DipW)x$($size.DipH)
window_phys=$($size.PhysW)x$($size.PhysH)
click_method=winapp ui hover/focus/click (SendInput) after ForceForeground
host=FENRIR-STUDIO
fenrir_desktop=not_touched
"@
Set-Content -Path (Join-Path $MediaDir 'meta.txt') -Value $meta -Encoding UTF8
Log 'DONE'
Get-Content (Join-Path $MediaDir 'meta.txt')
