param(
  [Parameter(Mandatory)][string]$MediaDir,
  [string]$TipSha = '276087a0f42e0c784432cc0d0e4906dd3b0753af',
  [string]$TipLayout = '',
  [switch]$SkipMain
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
New-Item -ItemType Directory -Force -Path $MediaDir | Out-Null
$env:WINAPP_UI_WORKFLOW_ID = 'palace-196-wimbly-proof'
$log = Join-Path $MediaDir 'wimbly-capture-log.txt'
function Log([string]$m){ $l="$(Get-Date -Format o) $m"; Add-Content $log $l; Write-Host $l }

Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Cap196w {
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

function Invoke-WinApp {
  param([string[]]$Arguments)
  $global:LASTEXITCODE = $null
  $prev = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try { $output = & winapp @Arguments 2>&1 } finally { $ErrorActionPreference = $prev }
  $lines = @($output | ForEach-Object { "$_" })
  if ($null -eq $global:LASTEXITCODE -or $global:LASTEXITCODE -ne 0) {
    throw "winapp $($Arguments -join ' ') failed ($global:LASTEXITCODE): $($lines -join "`n")"
  }
  return $lines
}

function Ui {
  param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
  Invoke-WinApp (@('ui') + $Arguments + @('-a', "$script:AppPid"))
}

function Get-MainHwnd {
  $wins = (Ui list-windows --json) -join "`n" | ConvertFrom-Json
  $main = @($wins) | Where-Object { $_.hwnd -and $_.title -like 'Palace*' } | Select-Object -First 1
  if (-not $main) { throw 'No Palace window' }
  return [IntPtr][int64]$main.hwnd
}

function Force-Foreground([IntPtr]$Hwnd) {
  $fg = [Cap196w]::GetForegroundWindow()
  $cur = [Cap196w]::GetCurrentThreadId()
  [uint32]$fgPid = 0
  $fgTid = [Cap196w]::GetWindowThreadProcessId($fg, [ref]$fgPid)
  [uint32]$tgPid = 0
  [void][Cap196w]::GetWindowThreadProcessId($Hwnd, [ref]$tgPid)
  $attached = $false
  if ($fgTid -ne 0 -and $fgTid -ne $cur) { $attached = [Cap196w]::AttachThreadInput($cur, $fgTid, $true) }
  try {
    [Cap196w]::ShowWindow($Hwnd, 9) | Out-Null
    [Cap196w]::BringWindowToTop($Hwnd) | Out-Null
    [Cap196w]::SetForegroundWindow($Hwnd) | Out-Null
  } finally {
    if ($attached) { [Cap196w]::AttachThreadInput($cur, $fgTid, $false) | Out-Null }
  }
  Start-Sleep -Milliseconds 150
}

function Resize-Dip([IntPtr]$Hwnd, [int]$DipW, [int]$DipH) {
  if ([Cap196w]::IsZoomed($Hwnd)) { [Cap196w]::ShowWindow($Hwnd, 9) | Out-Null; Start-Sleep -Milliseconds 200 }
  $dpi = [Cap196w]::GetDpiForWindow($Hwnd); if ($dpi -le 0) { $dpi = 144 }
  $scale = $dpi / 96.0
  $physW = [int][math]::Round($DipW * $scale)
  $physH = [int][math]::Round($DipH * $scale)
  $mi = New-Object Cap196w+MONITORINFO
  $mi.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($mi)
  [Cap196w]::GetMonitorInfo([Cap196w]::MonitorFromWindow($Hwnd, 2), [ref]$mi) | Out-Null
  $workW = $mi.rcWork.Right - $mi.rcWork.Left
  $workH = $mi.rcWork.Bottom - $mi.rcWork.Top
  if ($physW -gt $workW - 20) { $physW = $workW - 20 }
  if ($physH -gt $workH - 20) { $physH = $workH - 20 }
  Force-Foreground $Hwnd
  [Cap196w]::SetWindowPos($Hwnd, [IntPtr]::Zero, $mi.rcWork.Left + 10, $mi.rcWork.Top + 10, $physW, $physH, 0x0040) | Out-Null
  Start-Sleep -Milliseconds 650
  $r = New-Object Cap196w+RECT
  [Cap196w]::GetWindowRect($Hwnd, [ref]$r) | Out-Null
  $aw = [int][math]::Round(($r.Right - $r.Left) * 96.0 / $dpi)
  $ah = [int][math]::Round(($r.Bottom - $r.Top) * 96.0 / $dpi)
  Log ("Resize DIP={0}x{1} actual={2}x{3} phys={4}x{5} dpi={6} zoomed={7}" -f $DipW,$DipH,$aw,$ah,($r.Right-$r.Left),($r.Bottom-$r.Top),$dpi,([Cap196w]::IsZoomed($Hwnd)))
  return @{ Dpi=$dpi; Zoomed=[Cap196w]::IsZoomed($Hwnd); DipW=$aw; DipH=$ah; PhysW=($r.Right-$r.Left); PhysH=($r.Bottom-$r.Top) }
}

function Shot([string]$Name, [IntPtr]$Hwnd) {
  $path = Join-Path $MediaDir $Name
  if (Test-Path $path) { Remove-Item $path -Force }
  Force-Foreground $Hwnd
  # Use --window long form to avoid PowerShell -w ambiguity if wrapped later
  Invoke-WinApp @('ui','screenshot','--capture-screen','--window',"$Hwnd",'--output',$path,'-a',"$script:AppPid") | Out-Null
  if (-not (Test-Path $path) -or (Get-Item $path).Length -lt 1000) { throw "Bad shot $path" }
  Log "SHOT $Name size=$((Get-Item $path).Length)"
}

function Launch-Package {
  Get-Process Palace -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 500
  $pkg = Get-AppxPackage PalaceOrganizeTest-5E1F
  if (-not $pkg) { throw 'PalaceOrganizeTest-5E1F not installed' }
  $aumid = $pkg.PackageFamilyName + '!App'
  Log "Launch $($pkg.Version) $aumid"
  Start-Process "shell:AppsFolder\$aumid"
  for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Milliseconds 400
    $p = Get-Process Palace -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($p -and $p.MainWindowHandle -ne 0) {
      $script:AppPid = $p.Id
      Log "Launched PID=$script:AppPid title=$($p.MainWindowTitle)"
      return
    }
  }
  throw 'Palace failed to launch'
}

function Ensure-Library([IntPtr]$Hwnd) {
  Force-Foreground $Hwnd
  try { Ui wait-for GrdAssets -t 8000 | Out-Null } catch {
    Ui click NavLibrary | Out-Null
    Ui wait-for GrdAssets -t 12000 | Out-Null
  }
  try {
    $ts = (((Ui get-property TglPreviewPane --json) -join "`n") | ConvertFrom-Json).properties.ToggleState
    if ("$ts" -eq 'On') { Ui invoke TglPreviewPane | Out-Null; Start-Sleep -Milliseconds 400 }
  } catch {}
  try {
    $sm = (((Ui get-property TglSelectMode --json) -join "`n") | ConvertFrom-Json).properties.ToggleState
    if ("$sm" -eq 'On') { Ui invoke TglSelectMode | Out-Null; Start-Sleep -Milliseconds 300 }
  } catch {}
}

# ---------- A: main 0.3.81 thumb proof ----------
if (-not $SkipMain) {
  Log '=== A main 0.3.81 thumb proof ==='
  $layout081 = Join-Path $env:LOCALAPPDATA 'winapp-layout\palace-organize-194-081-debug-x64'
  $pkg = Get-AppxPackage PalaceOrganizeTest-5E1F
  if ($pkg.Version -ne '0.3.81.0') {
    Log "Switching to 0.3.81 from $($pkg.Version)"
    Get-Process Palace -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 400
    Remove-AppxPackage -Package $pkg.PackageFullName
    Start-Sleep -Milliseconds 400
    Add-AppxPackage -Register (Join-Path $layout081 'AppxManifest.xml')
    # Restore LocalState backup if present
    $bak = Join-Path $env:TEMP 'palace-organize-localstate-bak-196'
    $ls = 'C:\Users\iadag\AppData\Local\Packages\PalaceOrganizeTest-5E1F_1z32rh13vfry6\LocalState'
    if (Test-Path $bak) {
      New-Item -ItemType Directory -Force -Path $ls | Out-Null
      robocopy $bak $ls /E /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
    }
  }
  Launch-Package
  $hwnd = Get-MainHwnd
  $null = Resize-Dip $hwnd 1280 900
  Ensure-Library $hwnd
  Start-Sleep -Seconds 3
  Ui hover NavLibrary --dwell-time 500 | Out-Null
  Shot '06-main-081-same-library-thumbs.png' $hwnd
  Copy-Item 'C:\Users\iadag\AppData\Local\Temp\palace-sfw-fenrir-gate\art\art-a.jpg' (Join-Path $MediaDir '06-proof-sfw-art-a-source.jpg') -Force
}

# ---------- Register tip layout ----------
if (-not $TipLayout) { throw 'TipLayout required for tip shots' }
Log "=== Register tip layout $TipLayout ==="
Get-Process Palace -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400
$pkg = Get-AppxPackage PalaceOrganizeTest-5E1F
$manifest = Join-Path $TipLayout 'AppxManifest.xml'
if (Select-String -Path $manifest -Pattern 'Name="C20A7629' -Quiet) { throw 'tip layout release identity' }
if ($pkg) {
  # Prefer in-place upgrade when version increases
  try {
    Add-AppxPackage -Register $manifest
  } catch {
    Log "Register failed ($_), removing then re-register"
    Remove-AppxPackage -Package $pkg.PackageFullName
    Start-Sleep -Milliseconds 400
    Add-AppxPackage -Register $manifest
  }
} else {
  Add-AppxPackage -Register $manifest
}
$pkg = Get-AppxPackage PalaceOrganizeTest-5E1F
Log "Now $($pkg.Version) @ $($pkg.InstallLocation)"
if ($pkg.Version -notlike '0.3.84*') { throw "Expected 0.3.84, got $($pkg.Version)" }

Launch-Package
$hwnd = Get-MainHwnd
$size = Resize-Dip $hwnd 1280 900
Ensure-Library $hwnd
Start-Sleep -Seconds 2

# Tip idle mosaic (thumb continuity)
Ui hover NavLibrary --dwell-time 400 | Out-Null
Shot '07-tip-084-idle-mosaic.png' $hwnd

# ---------- B: flyout 05 ----------
Log '=== B flyout ==='
Force-Foreground $hwnd
Ui hover 'art-a.jpg' --dwell-time 800 | Out-Null
Start-Sleep -Milliseconds 250
Ui click BtnAssetMore | Out-Null
Start-Sleep -Milliseconds 900
# Verify menu
$open = (Ui search MnuOpen) -join "`n"
Log "MnuOpen search: $open"
Shot '05b-flyout-bottom-edge-aligned-right.png' $hwnd
try { Ui send-keys escape --via send-input | Out-Null } catch {}
Start-Sleep -Milliseconds 400

# ---------- C: scroll-recycle ----------
Log '=== C scroll-recycle ==='
Force-Foreground $hwnd
Ui hover 'art-a.jpg' --dwell-time 700 | Out-Null
Start-Sleep -Milliseconds 200
Shot '08a-hover-before-scroll.png' $hwnd
# Scroll mosaic to recycle containers
Ui scroll GrdAssets --wheel -1200 | Out-Null
Start-Sleep -Milliseconds 500
Ui scroll GrdAssets --wheel -1200 | Out-Null
Start-Sleep -Milliseconds 500
Ui scroll GrdAssets --wheel 2400 | Out-Null
Start-Sleep -Milliseconds 600
# Mouse off tiles
Ui hover NavLibrary --dwell-time 600 | Out-Null
Start-Sleep -Milliseconds 400
Shot '08b-after-scroll-recycle-no-stuck.png' $hwnd

# ---------- D: optional Tab focus ----------
Log '=== D Tab focus ==='
Force-Foreground $hwnd
Ui focus GrdAssets | Out-Null
Start-Sleep -Milliseconds 200
# Tab into first tile; may need several tabs
for ($i = 0; $i -lt 8; $i++) {
  Ui send-keys tab --via send-input | Out-Null
  Start-Sleep -Milliseconds 150
}
$focused = (Ui get-focused --json) -join "`n"
Log "focused=$focused"
Shot '09-tab-keyboard-focus.png' $hwnd

$pkg = Get-AppxPackage PalaceOrganizeTest-5E1F
$meta = @"
tip_sha=$TipSha
main_proof=06-main-081-same-library-thumbs.png (0.3.81 / be103dd lineage)
dpi=$($size.Dpi)
scale_pct=$([math]::Round(100.0*$size.Dpi/96.0))
identity=PalaceOrganizeTest-5E1F
tip_version=$($pkg.Version)
maximized=$($size.Zoomed)
window_dip=$($size.DipW)x$($size.DipH)
window_phys=$($size.PhysW)x$($size.PhysH)
host=FENRIR-STUDIO
fenrir_desktop=not_touched
thumb_verdict=synthetic SFW solid-color JPEGs (art-a #9FB4C7); cache present; not 0.3.84 decode regression
"@
Set-Content -Path (Join-Path $MediaDir 'meta-wimbly.txt') -Value $meta -Encoding UTF8
Log 'DONE'
Get-Content (Join-Path $MediaDir 'meta-wimbly.txt')
