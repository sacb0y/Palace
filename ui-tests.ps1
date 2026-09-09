param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Continue'
$pass = 0
$fail = 0
$results = @()

function Test-UI {
    param([string]$Name, [scriptblock]$Script)
    try {
        $output = & $Script 2>&1
        if ($LASTEXITCODE -eq 0) {
            $script:pass++
            $script:results += @{ name = $Name; status = 'PASS' }
        } else {
            $script:fail++
            $script:results += @{ name = $Name; status = 'FAIL'; detail = "$output" }
        }
    } catch {
        $script:fail++
        $script:results += @{ name = $Name; status = 'FAIL'; detail = "$_" }
    }
}

Test-UI 'NavLibrary exists' { winapp ui wait-for 'NavLibrary' -a $AppPid -t 5000 }
Test-UI 'NavRooms exists' { winapp ui wait-for 'NavRooms' -a $AppPid -t 3000 }
Test-UI 'NavSettings exists' { winapp ui wait-for 'NavSettings' -a $AppPid -t 3000 }
Test-UI 'Add folder exists' { winapp ui wait-for 'BtnAddFolder' -a $AppPid -t 3000 }
Test-UI 'Scan exists' { winapp ui wait-for 'BtnScan' -a $AppPid -t 3000 }
Test-UI 'Organize exists' { winapp ui wait-for 'BtnOrganize' -a $AppPid -t 3000 }
Test-UI 'Undo organize exists' { winapp ui wait-for 'BtnUndoOrganize' -a $AppPid -t 3000 }
Test-UI 'Search exists' { winapp ui wait-for 'AsbSearch' -a $AppPid -t 3000 }
Test-UI 'Asset grid exists' { winapp ui wait-for 'GrdAssets' -a $AppPid -t 3000 }
Test-UI 'Folder tree exists' { winapp ui wait-for 'TreFolders' -a $AppPid -t 3000 }
Test-UI 'Status exists' { winapp ui wait-for 'TxtStatus' -a $AppPid -t 3000 }

Test-UI 'Navigate to Rooms' { winapp ui invoke 'NavRooms' -a $AppPid }
Test-UI 'Rooms list loaded' { winapp ui wait-for 'LstRooms' -a $AppPid -t 4000 }
Test-UI 'New room button exists' { winapp ui wait-for 'BtnNewRoom' -a $AppPid -t 3000 }
Test-UI 'Set room name' { winapp ui set-value 'TxtRoomName' 'Moodboard' -a $AppPid }
Test-UI 'Create room' { winapp ui invoke 'BtnNewRoom' -a $AppPid }
Start-Sleep -Milliseconds 500
Test-UI 'Rooms status mentions room' {
    winapp ui wait-for 'TxtRoomsStatus' -a $AppPid --value 'Moodboard' --contains -t 4000
}

Test-UI 'Navigate to Settings' { winapp ui invoke 'NavSettings' -a $AppPid }
Test-UI 'Theme combo exists' { winapp ui wait-for 'CmbTheme' -a $AppPid -t 4000 }
Test-UI 'Sources list exists' { winapp ui wait-for 'LstSources' -a $AppPid -t 3000 }
Test-UI 'Auto-organize toggle exists' { winapp ui wait-for 'TglAutoOrganize' -a $AppPid -t 3000 }

Test-UI 'Navigate back to Library' { winapp ui invoke 'NavLibrary' -a $AppPid }
Test-UI 'Library search still present' { winapp ui wait-for 'AsbSearch' -a $AppPid -t 4000 }

New-Item -ItemType Directory -Force -Path 'screenshots' | Out-Null
winapp ui screenshot -a $AppPid -o 'screenshots/01-library.png' 2>$null
winapp ui invoke 'NavRooms' -a $AppPid
Start-Sleep -Milliseconds 400
winapp ui screenshot -a $AppPid -o 'screenshots/02-rooms.png' 2>$null
winapp ui invoke 'NavSettings' -a $AppPid
Start-Sleep -Milliseconds 400
winapp ui screenshot -a $AppPid -o 'screenshots/03-settings.png' 2>$null

Write-Host ""
Write-Host "Passed: $pass | Failed: $fail"
$results | Where-Object { $_.status -eq 'FAIL' } | ForEach-Object {
    Write-Host "  FAIL: $($_.name) - $($_.detail)" -ForegroundColor Red
}
$results | ConvertTo-Json | Out-File 'test-results.json'
if ($fail -gt 0) { exit 1 } else { exit 0 }
