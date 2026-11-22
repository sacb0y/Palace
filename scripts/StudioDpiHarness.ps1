<#
.SYNOPSIS
Canonical FENRIR-STUDIO / Windows UI harness helpers for Palace.

.DESCRIPTION
Call Enable-PalaceDpiAwareness before any GetWindowRect / SetWindowPos /
screenshot / SendInput. Sizes windows in physical pixels for a target DIP
size, gates on client DIP (and optional XamlRoot log), hovers to arm CanDrag,
and runs SendInput drags.

Drag QC (Blocker1): gate on Palace.TagDrag DragStarting / DragOver / Drop
probe lines. Do NOT fail QC solely on HWND/BitBlt float=0 near the cursor -
layered DragUI is out-of-band for single-HWND CAPTUREBLT.

.EXAMPLE
. .\scripts\StudioDpiHarness.ps1
Enable-PalaceDpiAwareness
$h = Get-PalaceMainHwnd -AppPid $AppPid
Resize-PalaceWindowDip -Hwnd $h -DipWidth 1280 -DipHeight 900
Assert-PalaceClientDipGate -Hwnd $h -TargetDipWidth 1280 -ToleranceDip 2
Invoke-PalaceSendInputDrag -AppPid $AppPid -FromSelector 'BtnTagChip_Sonic' -ToSelector 'BtnTagGroup_Character'
Assert-TagDragQcFromProbes -LogPath $env:TEMP\palace-tagdrag.log -RequireStarting
#>

# Dot-source only. ASCII-only (Windows PowerShell 5.1).

if (-not ('PalaceStudioDpi' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class PalaceStudioDpi {
  public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (IntPtr)(-4);
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
    <#
    .SYNOPSIS
    Sets PER_MONITOR_AWARE_V2 on the current process (and thread) before any
    window math / SendInput. Safe to call more than once.
    #>
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
    param(
        [Parameter(Mandatory)][int]$AppPid
    )
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
    $clientDipW = $clientPxW / $scale
    $clientDipH = $clientPxH / $scale
    return [pscustomobject]@{
        Dpi = [int]$dpi
        Scale = [double]$scale
        WindowPhysW = ($wr.Right - $wr.Left)
        WindowPhysH = ($wr.Bottom - $wr.Top)
        ClientPxW = [int]$clientPxW
        ClientPxH = [int]$clientPxH
        ClientDipW = [double]$clientDipW
        ClientDipH = [double]$clientDipH
        Zoomed = [bool][PalaceStudioDpi]::IsZoomed($Hwnd)
    }
}

function Resize-PalaceWindowDip {
    <#
    .SYNOPSIS
    Sizes the HWND in physical pixels for the requested client/window DIP size.
    Requires Enable-PalaceDpiAwareness first.
    #>
    param(
        [Parameter(Mandatory)][IntPtr]$Hwnd,
        [Parameter(Mandatory)][int]$DipWidth,
        [Parameter(Mandatory)][int]$DipHeight,
        [int]$SettleMs = 650
    )
    if ([PalaceStudioDpi]::IsZoomed($Hwnd)) {
        [void][PalaceStudioDpi]::ShowWindow($Hwnd, 9)
        Start-Sleep -Milliseconds 250
    }
    $dpi = [PalaceStudioDpi]::GetDpiForWindow($Hwnd)
    if ($dpi -le 0) { $dpi = 96 }
    $scale = $dpi / 96.0
    $physW = [int][math]::Round($DipWidth * $scale)
    $physH = [int][math]::Round($DipHeight * $scale)
    $mi = New-Object PalaceStudioDpi+MONITORINFO
    $mi.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($mi)
    [void][PalaceStudioDpi]::GetMonitorInfo(
        [PalaceStudioDpi]::MonitorFromWindow($Hwnd, 2),
        [ref]$mi)
    $workW = $mi.rcWork.Right - $mi.rcWork.Left
    $workH = $mi.rcWork.Bottom - $mi.rcWork.Top
    if ($physW -gt $workW - 20) { $physW = $workW - 20 }
    if ($physH -gt $workH - 20) { $physH = $workH - 20 }
    Set-PalaceForeground -Hwnd $Hwnd
    # SWP_SHOWWINDOW
    [void][PalaceStudioDpi]::SetWindowPos(
        $Hwnd, [IntPtr]::Zero,
        $mi.rcWork.Left + 10, $mi.rcWork.Top + 10,
        $physW, $physH, 0x0040)
    Start-Sleep -Milliseconds $SettleMs
    return Get-PalaceWindowMetrics -Hwnd $Hwnd
}

function Read-PalaceXamlRootLog {
    <#
    .SYNOPSIS
    Reads the latest XamlRoot.Size / RasterizationScale line from an optional
    app probe log (PALACE_XAMLROOT_LOG or %TEMP%\palace-xamlroot.log).
    #>
    param([string]$LogPath = '')
    if (-not $LogPath) {
        if ($env:PALACE_XAMLROOT_LOG) { $LogPath = $env:PALACE_XAMLROOT_LOG }
        else { $LogPath = Join-Path $env:TEMP 'palace-xamlroot.log' }
    }
    if (-not (Test-Path -LiteralPath $LogPath)) { return $null }
    $lines = Get-Content -LiteralPath $LogPath -ErrorAction SilentlyContinue
    if (-not $lines) { return $null }
    for ($i = $lines.Count - 1; $i -ge 0; $i--) {
        $line = $lines[$i]
        if ($line -match 'XamlRoot\.Size\s*=\s*([0-9.]+)\s*x\s*([0-9.]+)') {
            $w = [double]$Matches[1]
            $h = [double]$Matches[2]
            $scale = $null
            if ($line -match 'RasterizationScale\s*=\s*([0-9.]+)') {
                $scale = [double]$Matches[1]
            }
            return [pscustomobject]@{
                Width = $w
                Height = $h
                Scale = $scale
                Raw = $line
                Path = $LogPath
            }
        }
    }
    return $null
}

function Assert-PalaceClientDipGate {
    <#
    .SYNOPSIS
    Waits until client DIP (GetClientRect px / GetDpiForWindow scale) is within
    ToleranceDip of TargetDipWidth. When a XamlRoot log is present, also require
    |XamlRoot.Width - clientDip| <= ToleranceDip (the Studio smoking-gun gate).
    #>
    param(
        [Parameter(Mandatory)][IntPtr]$Hwnd,
        [double]$TargetDipWidth = 0,
        [double]$ToleranceDip = 2,
        [int]$TimeoutMs = 8000,
        [string]$XamlRootLogPath = ''
    )
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    $last = $null
    do {
        $last = Get-PalaceWindowMetrics -Hwnd $Hwnd
        $okTarget = $true
        if ($TargetDipWidth -gt 0) {
            $okTarget = [math]::Abs($last.ClientDipW - $TargetDipWidth) -le $ToleranceDip
        }
        $xr = Read-PalaceXamlRootLog -LogPath $XamlRootLogPath
        $okXaml = $true
        $xamlDelta = $null
        if ($xr) {
            $xamlDelta = [math]::Abs($xr.Width - $last.ClientDipW)
            $okXaml = $xamlDelta -le $ToleranceDip
            Write-Verbose ("GATE: |XamlRoot.Width {0} - clientPx/{1} {2}| = {3} (need le {4})" -f `
                $xr.Width, $last.Scale, $last.ClientDipW, $xamlDelta, $ToleranceDip)
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
    if ($null -ne $xamlDelta) { $msg += " xamlDelta=$xamlDelta" }
    throw $msg
}

function Get-PalaceUiCenter {
    param(
        [Parameter(Mandatory)][int]$AppPid,
        [Parameter(Mandatory)][string]$Selector
    )
    $raw = winapp ui get-property $Selector -a $AppPid --json 2>$null
    $json = $raw | ConvertFrom-Json
    $e = $json.element
    if (-not $e -or [int]$e.width -lt 5) { throw "Element unsized: $Selector" }
    return [pscustomobject]@{
        X = [int]($e.x + $e.width / 2)
        Y = [int]($e.y + $e.height / 2)
        Element = $e
    }
}

function Invoke-PalaceHover {
    <#
    .SYNOPSIS
    Real hover dwell so Tags/Library CanDrag can arm (PointerEntered).
    #>
    param(
        [Parameter(Mandatory)][int]$AppPid,
        [Parameter(Mandatory)][string]$Selector,
        [int]$DwellMs = 450
    )
    winapp ui hover $Selector -a $AppPid --dwell-time $DwellMs | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "hover failed for $Selector" }
}

function Send-PalaceMouseMove {
    param([int]$X, [int]$Y)
    [void][PalaceStudioDpi]::SetCursorPos($X, $Y)
}

function Send-PalaceMouseDown {
    param([int]$X, [int]$Y)
    [void][PalaceStudioDpi]::SetCursorPos($X, $Y)
    Start-Sleep -Milliseconds 40
    $i = New-Object PalaceStudioDpi+INPUT
    $i.type = 0
    $i.mi = New-Object PalaceStudioDpi+MOUSEINPUT
    $i.mi.dwFlags = 2 # MOUSEEVENTF_LEFTDOWN
    [void][PalaceStudioDpi]::SendInput(1, @($i), [Runtime.InteropServices.Marshal]::SizeOf([type][PalaceStudioDpi+INPUT]))
}

function Send-PalaceMouseUp {
    $i = New-Object PalaceStudioDpi+INPUT
    $i.type = 0
    $i.mi = New-Object PalaceStudioDpi+MOUSEINPUT
    $i.mi.dwFlags = 4 # MOUSEEVENTF_LEFTUP
    [void][PalaceStudioDpi]::SendInput(1, @($i), [Runtime.InteropServices.Marshal]::SizeOf([type][PalaceStudioDpi+INPUT]))
}

function Invoke-PalaceSendInputDrag {
    <#
    .SYNOPSIS
    Hover-arm CanDrag, then SendInput press / move / optional hold / release.
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
    $from = Get-PalaceUiCenter -AppPid $AppPid -Selector $FromSelector
    $to = Get-PalaceUiCenter -AppPid $AppPid -Selector $ToSelector
    Send-PalaceMouseDown -X $from.X -Y $from.Y
    Start-Sleep -Milliseconds 120
    for ($i = 1; $i -le $Steps; $i++) {
        $t = $i / [double]$Steps
        $x = [int]($from.X + ($to.X - $from.X) * $t)
        $y = [int]($from.Y + ($to.Y - $from.Y) * $t)
        Send-PalaceMouseMove -X $x -Y $y
        Start-Sleep -Milliseconds 22
    }
    if ($HoldMs -gt 0) { Start-Sleep -Milliseconds $HoldMs }
    if (-not $NoRelease) {
        Send-PalaceMouseUp
        Start-Sleep -Milliseconds 700
    }
    return [pscustomobject]@{ From = $from; To = $to }
}

function Assert-TagDragQcFromProbes {
    <#
    .SYNOPSIS
    Merge-gate QC for Tags drag: require probe lines, NEVER float=0 HWND capture.

    Pass -LogText or -LogPath containing lines like:
      Palace.TagDrag DragStarting n=1 ...
      Palace.TagDrag DragOver n=1 ...
      Palace.TagDrag Drop n=1 ...

    Visual float=0 near cursor is ignored on purpose (layered DragUI is not in
    PrintWindow / single-HWND CAPTUREBLT). Optional -FloatNearCursor is logged
    only; it cannot fail this assert.
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
            if ($env:PALACE_TAG_DRAG_LOG_FILE) { $LogPath = $env:PALACE_TAG_DRAG_LOG_FILE }
            else { $LogPath = Join-Path $env:TEMP 'palace-tagdrag.log' }
        }
        if (Test-Path -LiteralPath $LogPath) {
            $LogText = Get-Content -LiteralPath $LogPath -Raw -ErrorAction SilentlyContinue
        }
    }
    if (-not $LogText) { $LogText = '' }

    $hasStarting = $LogText -match 'Palace\.TagDrag\s+DragStarting'
    $hasOver = $LogText -match 'Palace\.TagDrag\s+DragOver'
    $hasDrop = $LogText -match 'Palace\.TagDrag\s+Drop'

    # Informational only - never a fail gate.
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
        # Default: DragStarting is the minimum merge gate.
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
    Resolves the AppX package for a running PID. Refuses release C20A7629 and
    any package whose Name is not Palace*Test*.
    #>
    param([Parameter(Mandatory)][int]$AppPid)
    $ReleaseIdentity = 'C20A7629-6BDE-449F-958A-5AAF8BCE88F3'
    $proc = Get-Process -Id $AppPid -ErrorAction Stop
    $exe = $null
    try { $exe = $proc.Path } catch { }
    if (-not $exe) {
        throw "Could not resolve Path for PID $AppPid - refuse to run UI tests without a package identity check."
    }
    $pkg = Get-AppxPackage | Where-Object {
        $_.InstallLocation -and ($exe.StartsWith($_.InstallLocation, [StringComparison]::OrdinalIgnoreCase))
    } | Select-Object -First 1
    if (-not $pkg) {
        throw "No AppxPackage owns '$exe' - refuse UI tests (need a packaged test identity)."
    }
    if ($pkg.Name -like 'C20A7629*' -or $pkg.Name -eq $ReleaseIdentity) {
        throw "REFUSED: PID $AppPid is release identity $($pkg.Name). Never drive ui-tests against C20A7629."
    }
    if ($pkg.Name -notlike 'Palace*Test*') {
        throw "REFUSED: package Name '$($pkg.Name)' is not a Palace*Test* identity."
    }
    return $pkg
}
