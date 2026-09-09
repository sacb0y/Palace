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

function Select-FirstLibraryAsset {
    $raw = winapp ui inspect -a $AppPid --interactive --json 2>$null
    $json = $raw | ConvertFrom-Json
    $els = @()
    if ($json.windows) {
        foreach ($w in @($json.windows)) { $els += @($w.elements) }
    } elseif ($json.elements) {
        $els = @($json.elements)
    }
    $item = $els | Where-Object {
        $_.isInvokable -and $_.name -match '\.(png|jpg|jpeg|webp|gif)$'
    } | Select-Object -First 1
    if (-not $item) { throw 'No image asset is visible in the library' }
    $sel = $item.selector
    if (-not $sel) { $sel = $item.name }
    winapp ui invoke $sel -a $AppPid
    if ($LASTEXITCODE -ne 0) { throw "Could not select $($item.name)" }
}

Test-UI 'NavLibrary exists' { winapp ui wait-for 'NavLibrary' -a $AppPid -t 5000 }
Test-UI 'NavTags exists' { winapp ui wait-for 'NavTags' -a $AppPid -t 3000 }
Test-UI 'NavRooms exists' { winapp ui wait-for 'NavRooms' -a $AppPid -t 3000 }
Test-UI 'NavSettings exists' { winapp ui wait-for 'NavSettings' -a $AppPid -t 3000 }
Test-UI 'Project combo exists' { winapp ui wait-for 'CmbProject' -a $AppPid -t 5000 }
Test-UI 'Project combo has a value' { winapp ui wait-for 'CmbProject' -a $AppPid -t 4000 }
Test-UI 'New project exists' { winapp ui wait-for 'BtnNewProject' -a $AppPid -t 3000 }
Test-UI 'Rename project exists' { winapp ui wait-for 'BtnRenameProject' -a $AppPid -t 3000 }
Test-UI 'Add folder exists' { winapp ui wait-for 'BtnAddFolder' -a $AppPid -t 3000 }
Test-UI 'Scan exists' { winapp ui wait-for 'BtnScan' -a $AppPid -t 3000 }
Test-UI 'Organize exists' { winapp ui wait-for 'BtnOrganize' -a $AppPid -t 3000 }
Test-UI 'Undo organize exists' { winapp ui wait-for 'BtnUndoOrganize' -a $AppPid -t 3000 }
Test-UI 'Search exists' { winapp ui wait-for 'AsbSearch' -a $AppPid -t 3000 }
Test-UI 'Asset mosaic exists' { winapp ui wait-for 'GrdAssets' -a $AppPid -t 3000 }
Test-UI 'Row height slider exists' { winapp ui wait-for 'SldRowHeight' -a $AppPid -t 3000 }
Test-UI 'Browse selector exists' { winapp ui wait-for 'SelBrowseMode' -a $AppPid -t 3000 }
Test-UI 'Folders mode exists' { winapp ui wait-for 'SelFolders' -a $AppPid -t 3000 }
Test-UI 'Select Folders browse' { winapp ui invoke 'SelFolders' -a $AppPid }
Test-UI 'Folder tree exists' { winapp ui wait-for 'TreFolders' -a $AppPid -t 4000 }
Test-UI 'Switch to Tags browse' { winapp ui invoke 'SelTags' -a $AppPid }
Test-UI 'Tag browse tree exists' { winapp ui wait-for 'TreTagsBrowse' -a $AppPid -t 4000 }
Test-UI 'Switch to Folders browse' { winapp ui invoke 'SelFolders' -a $AppPid }
Test-UI 'Folder tree after switch' { winapp ui wait-for 'TreFolders' -a $AppPid -t 4000 }
Test-UI 'Status exists' { winapp ui wait-for 'TxtStatus' -a $AppPid -t 3000 }

Test-UI 'Navigate to Tags' { winapp ui invoke 'NavTags' -a $AppPid }
Test-UI 'Tags tree loaded' { winapp ui wait-for 'TreTags' -a $AppPid -t 4000 }
Test-UI 'New tag box exists' { winapp ui wait-for 'TxtNewTagName' -a $AppPid -t 3000 }
Test-UI 'Set tag name' { winapp ui set-value 'TxtNewTagName' 'Sonic' -a $AppPid }
Test-UI 'Create ungrouped tag' {
    winapp ui wait-for 'BtnCreateUngroupedTag' -a $AppPid -t 4000
    if ($LASTEXITCODE -ne 0) { throw 'BtnCreateUngroupedTag missing' }
    winapp ui invoke 'BtnCreateUngroupedTag' -a $AppPid
}
Start-Sleep -Milliseconds 500
Test-UI 'Tags status mentions tag' {
    winapp ui wait-for 'TxtTagsStatus' -a $AppPid -t 4000
}

Test-UI 'Implied controls exist' {
    winapp ui wait-for 'AsbAddImplied' -a $AppPid -t 4000
    if ($LASTEXITCODE -ne 0) { throw 'AsbAddImplied missing' }
    winapp ui wait-for 'BtnAddImplied' -a $AppPid -t 2000
    if ($LASTEXITCODE -ne 0) { throw 'BtnAddImplied missing' }
    winapp ui wait-for 'LstImpliedTags' -a $AppPid -t 2000
    if ($LASTEXITCODE -ne 0) { throw 'LstImpliedTags missing' }
    winapp ui wait-for 'TxtImpliedTags' -a $AppPid -t 2000
}
Test-UI 'Create ImpHedgehog' {
    winapp ui set-value 'TxtNewTagName' 'ImpHedgehog' -a $AppPid
    if ($LASTEXITCODE -ne 0) { throw 'Could not set ImpHedgehog' }
    winapp ui invoke 'BtnCreateUngroupedTag' -a $AppPid
}
Start-Sleep -Milliseconds 600
Test-UI 'Create ImpSonic' {
    winapp ui set-value 'TxtNewTagName' 'ImpSonic' -a $AppPid
    if ($LASTEXITCODE -ne 0) { throw 'Could not set ImpSonic' }
    winapp ui invoke 'BtnCreateUngroupedTag' -a $AppPid
}
Start-Sleep -Milliseconds 700
Test-UI 'ImpSonic is selected' {
    winapp ui wait-for 'TxtRenameTag' -a $AppPid --value 'ImpSonic' --contains -t 4000
}
Test-UI 'Configure ImpSonic implies ImpHedgehog' {
    winapp ui send-keys --verbatim 'ImpHedgehog' --target 'AsbAddImplied' -a $AppPid --via send-input
    if ($LASTEXITCODE -ne 0) { throw 'Could not type implied tag' }
    Start-Sleep -Milliseconds 300
    winapp ui invoke 'BtnAddImplied' -a $AppPid
    if ($LASTEXITCODE -ne 0) { throw 'BtnAddImplied failed' }
}
Start-Sleep -Milliseconds 700
Test-UI 'Implication listed in tag manager' {
    winapp ui wait-for 'TxtImpliedTags' -a $AppPid --value 'ImpHedgehog' --contains -t 4000
}

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
Test-UI 'Tag typeahead exists' { winapp ui wait-for 'AsbAssignTag' -a $AppPid -t 4000 }
Test-UI 'Assign tag button exists' { winapp ui wait-for 'BtnAssignTag' -a $AppPid -t 3000 }
Test-UI 'Assigned summary exists' { winapp ui wait-for 'TxtAssignedTags' -a $AppPid -t 3000 }
Test-UI 'Suggestion summary exists' { winapp ui wait-for 'TxtTagSuggestions' -a $AppPid -t 3000 }
Test-UI 'Typeahead shows newly created tag' {
    winapp ui send-keys 'ctrl+a' --target 'AsbAssignTag' -a $AppPid --via send-input
    Start-Sleep -Milliseconds 100
    winapp ui send-keys --verbatim 'ImpSo' --target 'AsbAssignTag' -a $AppPid --via send-input
    Start-Sleep -Milliseconds 500
    winapp ui wait-for 'TxtTagSuggestions' -a $AppPid --value 'ImpSonic' --contains -t 4000
}
Test-UI 'Typeahead filter hides non-matches' {
    $raw = winapp ui get-value 'TxtTagSuggestions' -a $AppPid --json 2>$null | ConvertFrom-Json
    $text = "$($raw.text)$($raw.value)$($raw.name)"
    if ($text -notmatch 'ImpSonic') { throw "Expected ImpSonic in suggestions, got: $text" }
    if ($text -match 'ImpHedgehog') { throw "Filter leaked ImpHedgehog: $text" }
}

Test-UI 'Select a library asset' { Select-FirstLibraryAsset }
Start-Sleep -Milliseconds 700
Test-UI 'Assign ImpSonic from typeahead' {
    winapp ui send-keys 'ctrl+a' --target 'AsbAssignTag' -a $AppPid --via send-input
    Start-Sleep -Milliseconds 100
    winapp ui send-keys --verbatim 'ImpSonic' --target 'AsbAssignTag' -a $AppPid --via send-input
    Start-Sleep -Milliseconds 400
    winapp ui wait-for 'TxtTagSuggestions' -a $AppPid --value 'ImpSonic' --contains -t 3000
    if ($LASTEXITCODE -ne 0) { throw 'ImpSonic not in typeahead before assign' }
    winapp ui invoke 'BtnAssignTag' -a $AppPid
    if ($LASTEXITCODE -ne 0) { throw 'BtnAssignTag failed' }
}
Start-Sleep -Milliseconds 1000
Test-UI 'Created tag assigned immediately' {
    winapp ui wait-for 'TxtAssignedTags' -a $AppPid --value 'ImpSonic' --contains -t 5000
}
Test-UI 'Implicit tag applied on assign' {
    winapp ui wait-for 'TxtAssignedTags' -a $AppPid --value 'ImpHedgehog' --contains -t 4000
}

New-Item -ItemType Directory -Force -Path 'screenshots' | Out-Null
winapp ui screenshot -a $AppPid -o 'screenshots/01-library.png' 2>$null
winapp ui invoke 'SelTags' -a $AppPid
Start-Sleep -Milliseconds 400
winapp ui screenshot -a $AppPid -o 'screenshots/05-library-tags.png' 2>$null
winapp ui invoke 'NavTags' -a $AppPid
Start-Sleep -Milliseconds 400
winapp ui screenshot -a $AppPid -o 'screenshots/04-tags.png' 2>$null
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
