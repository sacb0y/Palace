<#
.SYNOPSIS
Canonical FENRIR-STUDIO / Windows UI harness helpers for Palace.

.DESCRIPTION
Call Enable-PalaceDpiAwareness before any GetWindowRect / SetWindowPos /
screenshot / SendInput. Sizes windows so the *client* area matches the
target DIP (chrome-compensated SetWindowPos), gates on client DIP + required
XamlRoot LocalState probe, hovers to arm CanDrag, and runs absolute SendInput
drags (MOVE|ABSOLUTE|VIRTUALDESK past SM_CXDRAG + wiggle).

Drag QC (Blocker1): gate on Palace.TagDrag DragStarting / DragOver / Drop
probe lines. Do NOT fail QC solely on HWND/BitBlt float=0 near the cursor -
layered DragUI is out-of-band for single-HWND CAPTUREBLT.

C1: Test-PalaceUiTestIdentity uses OpenProcess + GetPackageFullName; exact
allowlist PalaceOrganizeTest-5E1F; refuse C20A7629; APPMODEL_ERROR_NO_PACKAGE
(15700) / unpackaged layout exe refuse.

.EXAMPLE
. .\scripts\StudioDpiHarness.ps1
Enable-PalaceDpiAwareness
$h = Get-PalaceMainHwnd -AppPid $AppPid
$xr = Get-PalaceXamlRootLogPath -PackageFamilyName $pkg.PackageFamilyName
if (Test-Path $xr) { Remove-Item -LiteralPath $xr -Force }
Resize-PalaceWindowDip -Hwnd $h -DipWidth 1280 -DipHeight 900
Assert-PalaceClientDipGate -Hwnd $h -TargetDipWidth 1280 -AppPid $AppPid -XamlRootLogPath $xr
Invoke-PalaceSendInputDrag -AppPid $AppPid -FromSelector 'BtnTagChip_Sonic' -ToSelector 'BtnTagGroup_Character'
Assert-TagDragQcFromProbes -LogPath $env:PALACE_TAG_DRAG_LOG_FILE -RequireStarting -RequireOver -RequireDrop
#>

# Dot-source only. ASCII-only (Windows PowerShell 5.1).

$script:PalaceAllowedTestIdentity = 'PalaceOrganizeTest-5E1F'
$script:PalaceReleaseIdentity = 'C20A7629-6BDE-449F-958A-5AAF8BCE88F3'

if (-not ('PalaceStudioDpi' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class PalaceStudioDpi {
  public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (IntPtr)(-4);
  public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
  public const int APPMODEL_ERROR_NO_PACKAGE = 15700;
  public const int ERROR_INSUFFICIENT_BUFFER = 122;
  public const int ERROR_SUCCESS = 0;
  public const uint MOUSEEVENTF_MOVE = 0x0001;
  public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
  public const uint MOUSEEVENTF_LEFTUP = 0x0004;
  public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
  public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
  public const int SM_CXDRAG = 68;
  public const int SM_XVIRTUALSCREEN = 76;
  public const int SM_YVIRTUALSCREEN = 77;
  public const int SM_CXVIRTUALSCREEN = 78;
  public const int SM_CYVIRTUALSCREEN = 79;
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
  [DllImport("user32.dll")] public static extern IntPtr GetThreadDpiAwarenessContext();
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
  [DllImport("user32.dll")] public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int X, int Y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
  [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
  [DllImport("user32.dll")] public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int nIndex);
  [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);
  [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr hObject);
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  public static extern int GetPackageFullName(IntPtr hProcess, ref uint packageFullNameLength, StringBuilder packageFullName);
  public struct RECT { public int Left, Top, Right, Bottom; }
  public struct POINT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
  public struct MONITORINFO {
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
  }
  [StructLayout(LayoutKind.Sequential)]
  public struct INPUT { public uint type; public MOUSEINPUT mi; }
  [StructLayout(LayoutKind.Sequential)]
  public struct MOUSEINPUT {
    public int dx, dy;
    public uint mouseData, dwFlags, time;
    public IntPtr dwExtraInfo;
  }
}
"@
}

function Enable-PalaceDpiAwareness {
    $ctx = [PalaceStudioDpi]::DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
    $ok = [PalaceStudioDpi]::SetProcessDpiAwarenessContext($ctx)
    [void][PalaceStudioDpi]::SetThreadDpiAwarenessContext($ctx)
    $aware = [PalaceStudioDpi]::GetAwarenessFromDpiAwarenessContext(
        [PalaceStudioDpi]::GetThreadDpiAwarenessContext())
    Write-Verbose ("SetProcessDpiAwarenessContext(-4) ok={0} awareness={1}" -f $ok, $aware)
    if ($aware -lt 2) {
        throw "DPI awareness is $aware (need PER_MONITOR_AWARE / V2 = 2). Refuse to size/capture unaware."
    }
    return $aware
}

function Get-PalaceMainHwnd {
    param([Parameter(Mandatory)][int]$AppPid)
    $raw = winapp ui list-windows -a $AppPid --json 2>$null
    $windows = $raw | ConvertFrom-Json
    $main = @($windows) | Where-Object {
        $_.title -and $_.title -ne 'PopupHost' -and ($_.title -like 'Palace*')
    } | Select-Object -First 1
    if (-not $main) {
        $main = @($windows) | Where-Object { $_.title -and $_.title -ne 'PopupHost' } | Select-Object -First 1
    }
    if (-not $main) { throw "No main window for PID $AppPid" }
    return [IntPtr][int64]$main.hwnd
}

function Set-PalaceForeground {
    param([Parameter(Mandatory)][IntPtr]$Hwnd)
    $fg = [PalaceStudioDpi]::GetForegroundWindow()
    $cur = [PalaceStudioDpi]::GetCurrentThreadId()
    [uint32]$fgPid = 0
    $fgTid = [PalaceStudioDpi]::GetWindowThreadProcessId($fg, [ref]$fgPid)
    $attached = $false
    if ($fgTid -ne 0 -and $fgTid -ne $cur) {
        $attached = [PalaceStudioDpi]::AttachThreadInput($cur, $fgTid, $true)
    }
    try {
        [void][PalaceStudioDpi]::ShowWindow($Hwnd, 9)
        [void][PalaceStudioDpi]::BringWindowToTop($Hwnd)
        [void][PalaceStudioDpi]::SetForegroundWindow($Hwnd)
    } finally {
        if ($attached) {
            [void][PalaceStudioDpi]::AttachThreadInput($cur, $fgTid, $false)
        }
    }
    Start-Sleep -Milliseconds 180
}

function Get-PalaceWindowMetrics {
    param([Parameter(Mandatory)][IntPtr]$Hwnd)
    $dpi = [PalaceStudioDpi]::GetDpiForWindow($Hwnd)
    if ($dpi -le 0) { $dpi = 96 }
    $scale = $dpi / 96.0
    $wr = New-Object PalaceStudioDpi+RECT
    [void][PalaceStudioDpi]::GetWindowRect($Hwnd, [ref]$wr)
    $cr = New-Object PalaceStudioDpi+RECT
    [void][PalaceStudioDpi]::GetClientRect($Hwnd, [ref]$cr)
    $clientPxW = $cr.Right - $cr.Left
    $clientPxH = $cr.Bottom - $cr.Top
    return [pscustomobject]@{
        Dpi = [int]$dpi
        Scale = [double]$scale
        WindowPhysW = ($wr.Right - $wr.Left)
        WindowPhysH = ($wr.Bottom - $wr.Top)
        WindowLeft = $wr.Left
        WindowTop = $wr.Top
        WindowRight = $wr.Right
        WindowBottom = $wr.Bottom
        ClientPxW = [int]$clientPxW
        ClientPxH = [int]$clientPxH
        ClientDipW = [double]($clientPxW / $scale)
        ClientDipH = [double]($clientPxH / $scale)
        Zoomed = [bool][PalaceStudioDpi]::IsZoomed($Hwnd)
    }
}

function Resize-PalaceWindowDip {
    <#
    .SYNOPSIS
    Sizes the HWND so the *client* area matches the requested DIP size
    (chrome-compensated SetWindowPos).
    #>
    param(
        [Parameter(Mandatory)][IntPtr]$Hwnd,
        [Parameter(Mandatory)][int]$DipWidth,
        [Parameter(Mandatory)][int]$DipHeight,
        [int]$SettleMs = 650,
        [double]$ToleranceDip = 2,
        [int]$MaxPasses = 3
    )
    if ([PalaceStudioDpi]::IsZoomed($Hwnd)) {
        [void][PalaceStudioDpi]::ShowWindow($Hwnd, 9)
        Start-Sleep -Milliseconds 250
    }
    $mi = New-Object PalaceStudioDpi+MONITORINFO
    $mi.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($mi)
    [void][PalaceStudioDpi]::GetMonitorInfo(
        [PalaceStudioDpi]::MonitorFromWindow($Hwnd, 2),
        [ref]$mi)
    $workW = $mi.rcWork.Right - $mi.rcWork.Left
    $workH = $mi.rcWork.Bottom - $mi.rcWork.Top

    Set-PalaceForeground -Hwnd $Hwnd
    $metrics = $null
    for ($pass = 1; $pass -le $MaxPasses; $pass++) {
        $dpi = [PalaceStudioDpi]::GetDpiForWindow($Hwnd)
        if ($dpi -le 0) { $dpi = 96 }
        $scale = $dpi / 96.0
        $metrics = Get-PalaceWindowMetrics -Hwnd $Hwnd
        $chromePxW = [math]::Max(0, $metrics.WindowPhysW - $metrics.ClientPxW)
        $chromePxH = [math]::Max(0, $metrics.WindowPhysH - $metrics.ClientPxH)
        $clientPxW = [int][math]::Round($DipWidth * $scale)
        $clientPxH = [int][math]::Round($DipHeight * $scale)
        $physW = $clientPxW + $chromePxW
        $physH = $clientPxH + $chromePxH
        if ($physW -gt $workW - 20) { $physW = $workW - 20 }
        if ($physH -gt $workH - 20) { $physH = $workH - 20 }
        Write-Verbose ("Resize pass {0}: targetClientDip={1}x{2} chromePx={3}x{4} outerPhys={5}x{6}" -f `
            $pass, $DipWidth, $DipHeight, $chromePxW, $chromePxH, $physW, $physH)
        [void][PalaceStudioDpi]::SetWindowPos(
            $Hwnd, [IntPtr]::Zero,
            $mi.rcWork.Left + 10, $mi.rcWork.Top + 10,
            $physW, $physH, 0x0040)
        Start-Sleep -Milliseconds $SettleMs
        $metrics = Get-PalaceWindowMetrics -Hwnd $Hwnd
        $dw = [math]::Abs($metrics.ClientDipW - $DipWidth)
        $dh = [math]::Abs($metrics.ClientDipH - $DipHeight)
        if ($dw -le $ToleranceDip -and $dh -le $ToleranceDip) {
            return $metrics
        }
    }
    return $metrics
}

function Get-PalaceXamlRootLogPath {
    param(
        [Parameter(Mandatory)][string]$PackageFamilyName
    )
    $localState = Join-Path $env:LOCALAPPDATA ("Packages\{0}\LocalState" -f $PackageFamilyName)
    return (Join-Path $localState 'palace-xamlroot.log')
}

function Read-PalaceXamlRootLog {
    param(
        [Parameter(Mandatory)][string]$LogPath,
        [int]$ExpectedPid = 0
    )
    if (-not (Test-Path -LiteralPath $LogPath)) { return $null }
    $text = Get-Content -LiteralPath $LogPath -Raw -ErrorAction Stop
    if (-not $text) { return $null }
    $lines = $text -split "`r?`n" | Where-Object { $_ }
    for ($i = $lines.Count - 1; $i -ge 0; $i--) {
        $line = $lines[$i]
        if ($line -notmatch 'XamlRoot\.Size=([0-9.]+)x([0-9.]+)') { continue }
        $w = [double]$Matches[1]
        $h = [double]$Matches[2]
        if ($line -notmatch 'RasterizationScale=([0-9.]+)') { continue }
        $scale = [double]$Matches[1]
        if ($line -notmatch 'pid=([0-9]+)') { continue }
        $pid = [int]$Matches[1]
        if ($ExpectedPid -gt 0 -and $pid -ne $ExpectedPid) { continue }
        return [pscustomobject]@{
            Width = $w
            Height = $h
            Scale = $scale
            Pid = $pid
            Raw = $line
            Path = $LogPath
        }
    }
    return $null
}

function Assert-PalaceClientDipGate {
    <#
    .SYNOPSIS
    Waits until client DIP matches TargetDipWidth and (by default) a XamlRoot
    LocalState probe line matches client DIP and AppPid.
    #>
    param(
        [Parameter(Mandatory)][IntPtr]$Hwnd,
        [double]$TargetDipWidth = 0,
        [double]$ToleranceDip = 2,
        [int]$TimeoutMs = 8000,
        [string]$XamlRootLogPath = '',
        [int]$AppPid = 0,
        [bool]$RequireXamlRoot = $true
    )
    if ($RequireXamlRoot -and -not $XamlRootLogPath) {
        throw 'Assert-PalaceClientDipGate -RequireXamlRoot needs -XamlRootLogPath (LocalState palace-xamlroot.log).'
    }
    if ($RequireXamlRoot -and $AppPid -le 0) {
        throw 'Assert-PalaceClientDipGate -RequireXamlRoot needs -AppPid to match probe pid.'
    }

    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    $last = $null
    $xamlDelta = $null
    $xr = $null
    do {
        $last = Get-PalaceWindowMetrics -Hwnd $Hwnd
        $okTarget = $true
        if ($TargetDipWidth -gt 0) {
            $okTarget = [math]::Abs($last.ClientDipW - $TargetDipWidth) -le $ToleranceDip
        }
        $okXaml = -not $RequireXamlRoot
        if ($RequireXamlRoot -or $XamlRootLogPath) {
            $xr = Read-PalaceXamlRootLog -LogPath $XamlRootLogPath -ExpectedPid $AppPid
            if ($xr) {
                $xamlDelta = [math]::Abs($xr.Width - $last.ClientDipW)
                $okXaml = $xamlDelta -le $ToleranceDip
                Write-Verbose ("GATE: |XamlRoot.Width {0} - clientDip {1}| = {2} pid={3} (need le {4})" -f `
                    $xr.Width, $last.ClientDipW, $xamlDelta, $xr.Pid, $ToleranceDip)
            } elseif ($RequireXamlRoot) {
                $okXaml = $false
            }
        }
        if ($okTarget -and $okXaml -and -not $last.Zoomed) {
            return [pscustomobject]@{
                Metrics = $last
                XamlRoot = $xr
                XamlDelta = $xamlDelta
                Passed = $true
            }
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)

    $msg = "XamlRoot/client DIP gate FAIL: clientDipW=$($last.ClientDipW) target=$TargetDipWidth"
    if ($RequireXamlRoot -and -not $xr) {
        $msg += " (missing XamlRoot probe for pid=$AppPid at $XamlRootLogPath)"
    }
    if ($null -ne $xamlDelta) { $msg += " xamlDelta=$xamlDelta" }
    throw $msg
}

function Get-PalaceUiCenter {
    param(
        [Parameter(Mandatory)][int]$AppPid,
        [Parameter(Mandatory)][string]$Selector,
        [IntPtr]$Hwnd = [IntPtr]::Zero
    )
    $raw = winapp ui get-property $Selector -a $AppPid --json 2>$null
    $json = $raw | ConvertFrom-Json
    $e = $json.element
    if (-not $e -or [int]$e.width -lt 5) { throw "Element unsized: $Selector" }
    $x = [int]($e.x + $e.width / 2)
    $y = [int]($e.y + $e.height / 2)
    if ($Hwnd -ne [IntPtr]::Zero) {
        $m = Get-PalaceWindowMetrics -Hwnd $Hwnd
        if ($x -lt $m.WindowLeft + 2) { $x = $m.WindowLeft + 2 }
        if ($x -gt $m.WindowRight - 3) { $x = $m.WindowRight - 3 }
        if ($y -lt $m.WindowTop + 2) { $y = $m.WindowTop + 2 }
        if ($y -gt $m.WindowBottom - 3) { $y = $m.WindowBottom - 3 }
    }
    return [pscustomobject]@{
        X = $x
        Y = $y
        Element = $e
    }
}

function Invoke-PalaceHover {
    param(
        [Parameter(Mandatory)][int]$AppPid,
        [Parameter(Mandatory)][string]$Selector,
        [int]$DwellMs = 450
    )
    winapp ui hover $Selector -a $AppPid --dwell-time $DwellMs | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "hover failed for $Selector" }
}

function Send-PalaceMouseMoveAbsolute {
    param([int]$X, [int]$Y)
    $vx = [PalaceStudioDpi]::GetSystemMetrics([PalaceStudioDpi]::SM_XVIRTUALSCREEN)
    $vy = [PalaceStudioDpi]::GetSystemMetrics([PalaceStudioDpi]::SM_YVIRTUALSCREEN)
    $vw = [PalaceStudioDpi]::GetSystemMetrics([PalaceStudioDpi]::SM_CXVIRTUALSCREEN)
    $vh = [PalaceStudioDpi]::GetSystemMetrics([PalaceStudioDpi]::SM_CYVIRTUALSCREEN)
    if ($vw -le 1) { $vw = 2 }
    if ($vh -le 1) { $vh = 2 }
    $normX = [int][math]::Round(($X - $vx) * 65535.0 / ($vw - 1))
    $normY = [int][math]::Round(($Y - $vy) * 65535.0 / ($vh - 1))
    if ($normX -lt 0) { $normX = 0 }
    if ($normY -lt 0) { $normY = 0 }
    if ($normX -gt 65535) { $normX = 65535 }
    if ($normY -gt 65535) { $normY = 65535 }
    $i = New-Object PalaceStudioDpi+INPUT
    $i.type = 0
    $i.mi = New-Object PalaceStudioDpi+MOUSEINPUT
    $i.mi.dx = $normX
    $i.mi.dy = $normY
    $i.mi.dwFlags = [PalaceStudioDpi]::MOUSEEVENTF_MOVE -bor [PalaceStudioDpi]::MOUSEEVENTF_ABSOLUTE -bor [PalaceStudioDpi]::MOUSEEVENTF_VIRTUALDESK
    [void][PalaceStudioDpi]::SendInput(1, @($i), [Runtime.InteropServices.Marshal]::SizeOf([type][PalaceStudioDpi+INPUT]))
}

function Send-PalaceMouseDownAbsolute {
    param([int]$X, [int]$Y)
    Send-PalaceMouseMoveAbsolute -X $X -Y $Y
    Start-Sleep -Milliseconds 40
    $i = New-Object PalaceStudioDpi+INPUT
    $i.type = 0
    $i.mi = New-Object PalaceStudioDpi+MOUSEINPUT
    $i.mi.dwFlags = [PalaceStudioDpi]::MOUSEEVENTF_LEFTDOWN
    [void][PalaceStudioDpi]::SendInput(1, @($i), [Runtime.InteropServices.Marshal]::SizeOf([type][PalaceStudioDpi+INPUT]))
}

function Send-PalaceMouseUpAbsolute {
    $i = New-Object PalaceStudioDpi+INPUT
    $i.type = 0
    $i.mi = New-Object PalaceStudioDpi+MOUSEINPUT
    $i.mi.dwFlags = [PalaceStudioDpi]::MOUSEEVENTF_LEFTUP
    [void][PalaceStudioDpi]::SendInput(1, @($i), [Runtime.InteropServices.Marshal]::SizeOf([type][PalaceStudioDpi+INPUT]))
}

function Invoke-PalaceSendInputDrag {
    <#
    .SYNOPSIS
    Hover-arm CanDrag, then absolute SendInput drag past SM_CXDRAG with a
    target wiggle so DragOver can fire. Points clamped to the main window rect.
    #>
    param(
        [Parameter(Mandatory)][int]$AppPid,
        [Parameter(Mandatory)][string]$FromSelector,
        [Parameter(Mandatory)][string]$ToSelector,
        [int]$Steps = 22,
        [int]$HoldMs = 400,
        [int]$HoverDwellMs = 450,
        [switch]$NoRelease
    )
    $hwnd = Get-PalaceMainHwnd -AppPid $AppPid
    Set-PalaceForeground -Hwnd $hwnd
    Invoke-PalaceHover -AppPid $AppPid -Selector $FromSelector -DwellMs $HoverDwellMs
    $from = Get-PalaceUiCenter -AppPid $AppPid -Selector $FromSelector -Hwnd $hwnd
    $to = Get-PalaceUiCenter -AppPid $AppPid -Selector $ToSelector -Hwnd $hwnd
    $dragThresh = [PalaceStudioDpi]::GetSystemMetrics([PalaceStudioDpi]::SM_CXDRAG)
    if ($dragThresh -lt 4) { $dragThresh = 4 }

    Send-PalaceMouseDownAbsolute -X $from.X -Y $from.Y
    Start-Sleep -Milliseconds 80
    # Cross SM_CXDRAG before the rest of the path so WinUI starts DnD.
    $dx = $to.X - $from.X
    $dy = $to.Y - $from.Y
    $len = [math]::Sqrt(($dx * $dx) + ($dy * $dy))
    if ($len -lt 1) { $len = 1 }
    $thresh = $dragThresh + 2
    $tx = [int]($from.X + ($dx / $len) * $thresh)
    $ty = [int]($from.Y + ($dy / $len) * $thresh)
    Send-PalaceMouseMoveAbsolute -X $tx -Y $ty
    Start-Sleep -Milliseconds 40

    for ($i = 1; $i -le $Steps; $i++) {
        $t = $i / [double]$Steps
        $x = [int]($from.X + ($to.X - $from.X) * $t)
        $y = [int]($from.Y + ($to.Y - $from.Y) * $t)
        Send-PalaceMouseMoveAbsolute -X $x -Y $y
        Start-Sleep -Milliseconds 22
    }
    # Wiggle on the drop target so DragOver can sample.
    foreach ($w in @(@(4, 0), @(-4, 2), @(0, -3), @(0, 0))) {
        Send-PalaceMouseMoveAbsolute -X ($to.X + $w[0]) -Y ($to.Y + $w[1])
        Start-Sleep -Milliseconds 30
    }
    if ($HoldMs -gt 0) { Start-Sleep -Milliseconds $HoldMs }
    if (-not $NoRelease) {
        Send-PalaceMouseUpAbsolute
        Start-Sleep -Milliseconds 700
    }
    return [pscustomobject]@{ From = $from; To = $to }
}

function Assert-TagDragQcFromProbes {
    <#
    .SYNOPSIS
    Merge-gate QC for Tags drag: require probe lines, NEVER float=0 HWND capture.
    Missing log path / file => 'probe sink missing'.
    #>
    param(
        [string]$LogPath = '',
        [string]$LogText = '',
        [switch]$RequireStarting,
        [switch]$RequireOver,
        [switch]$RequireDrop,
        [int]$FloatNearCursor = -1
    )
    if (-not $LogText) {
        if (-not $LogPath) {
            $LogPath = $env:PALACE_TAG_DRAG_LOG_FILE
        }
        if (-not $LogPath) {
            throw 'probe sink missing'
        }
        if (-not (Test-Path -LiteralPath $LogPath)) {
            throw 'probe sink missing'
        }
        $LogText = Get-Content -LiteralPath $LogPath -Raw -ErrorAction Stop
    }
    if (-not $LogText) {
        throw 'probe sink missing'
    }

    $hasStarting = $LogText -match 'Palace\.TagDrag\s+DragStarting'
    $hasOver = $LogText -match 'Palace\.TagDrag\s+DragOver'
    $hasDrop = $LogText -match 'Palace\.TagDrag\s+Drop'

    if ($FloatNearCursor -ge 0) {
        Write-Verbose ("QC visual float={0} (ignored; probes are the gate)" -f $FloatNearCursor)
    }

    $failures = @()
    if ($RequireStarting -and -not $hasStarting) {
        $failures += 'missing Palace.TagDrag DragStarting probe'
    }
    if ($RequireOver -and -not $hasOver) {
        $failures += 'missing Palace.TagDrag DragOver probe'
    }
    if ($RequireDrop -and -not $hasDrop) {
        $failures += 'missing Palace.TagDrag Drop probe'
    }
    if (-not $RequireStarting -and -not $RequireOver -and -not $RequireDrop) {
        if (-not $hasStarting) {
            $failures += 'missing Palace.TagDrag DragStarting probe (default gate)'
        }
    }
    if ($failures.Count -gt 0) {
        throw ("Tag drag QC FAIL (probe gate): {0}" -f ($failures -join '; '))
    }
    return [pscustomobject]@{
        DragStarting = $hasStarting
        DragOver = $hasOver
        Drop = $hasDrop
        FloatIgnored = $true
        Passed = $true
    }
}

function Test-PalaceUiTestIdentity {
    <#
    .SYNOPSIS
    OpenProcess + GetPackageFullName for the PID. Exact allowlist
    PalaceOrganizeTest-5E1F. Refuse C20A7629. APPMODEL_ERROR_NO_PACKAGE (15700)
    and other errors refuse (unpackaged layout exe must fail).
    #>
    param([Parameter(Mandatory)][int]$AppPid)
    $h = [PalaceStudioDpi]::OpenProcess([PalaceStudioDpi]::PROCESS_QUERY_LIMITED_INFORMATION, $false, $AppPid)
    if ($h -eq [IntPtr]::Zero) {
        $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        throw "REFUSED: OpenProcess PID $AppPid failed (Win32 $err). Need a packaged PalaceOrganizeTest-5E1F process."
    }
    try {
        [uint32]$len = 0
        $sb = New-Object System.Text.StringBuilder 0
        $rc = [PalaceStudioDpi]::GetPackageFullName($h, [ref]$len, $sb)
        if ($rc -eq [PalaceStudioDpi]::APPMODEL_ERROR_NO_PACKAGE) {
            throw "REFUSED: PID $AppPid has no package (GetPackageFullName 15700). Unpackaged layout exe must fail."
        }
        if ($rc -ne [PalaceStudioDpi]::ERROR_INSUFFICIENT_BUFFER -and $rc -ne [PalaceStudioDpi]::ERROR_SUCCESS) {
            throw "REFUSED: GetPackageFullName PID $AppPid failed (Win32 $rc)."
        }
        if ($len -eq 0) {
            throw "REFUSED: GetPackageFullName PID $AppPid returned empty length."
        }
        $sb = New-Object System.Text.StringBuilder ([int]$len)
        $rc = [PalaceStudioDpi]::GetPackageFullName($h, [ref]$len, $sb)
        if ($rc -eq [PalaceStudioDpi]::APPMODEL_ERROR_NO_PACKAGE) {
            throw "REFUSED: PID $AppPid has no package (GetPackageFullName 15700). Unpackaged layout exe must fail."
        }
        if ($rc -ne [PalaceStudioDpi]::ERROR_SUCCESS) {
            throw "REFUSED: GetPackageFullName PID $AppPid failed (Win32 $rc)."
        }
        $fullName = $sb.ToString()
        if ([string]::IsNullOrWhiteSpace($fullName)) {
            throw "REFUSED: GetPackageFullName PID $AppPid returned empty name."
        }
    } finally {
        [void][PalaceStudioDpi]::CloseHandle($h)
    }

    $pkg = Get-AppxPackage | Where-Object { $_.PackageFullName -eq $fullName } | Select-Object -First 1
    if (-not $pkg) {
        throw "REFUSED: no Get-AppxPackage match for '$fullName'."
    }
    if ($pkg.Name -eq $script:PalaceReleaseIdentity -or $pkg.Name -like 'C20A7629*') {
        throw "REFUSED: PID $AppPid is release identity $($pkg.Name). Never drive ui-tests against C20A7629."
    }
    if ($pkg.Name -ne $script:PalaceAllowedTestIdentity) {
        throw "REFUSED: package Name '$($pkg.Name)' is not exact allowlist '$($script:PalaceAllowedTestIdentity)'."
    }
    return $pkg
}
