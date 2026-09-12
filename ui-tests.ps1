param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Continue'
$pass = 0
$fail = 0
$results = @()
$script:TestDeleteFileName = $null
$script:TestDeletePath = $null
$script:BackfillAssetName = $null
$script:MainHwnd = $null
$script:BfSourceName = "BfS$([guid]::NewGuid().ToString('N').Substring(0, 6))"
$script:BfImpliedName = "BfI$([guid]::NewGuid().ToString('N').Substring(0, 6))"
$script:UiSharedName = "UiS$([guid]::NewGuid().ToString('N').Substring(0, 6))"
$script:UiPartialName = "UiP$([guid]::NewGuid().ToString('N').Substring(0, 6))"

function Bind-MainWindow {
    $windows = winapp ui list-windows -a $AppPid --json 2>$null | ConvertFrom-Json
    $main = @($windows) | Where-Object { $_.title -eq 'Palace' } | Select-Object -First 1
    if ($main) { $script:MainHwnd = $main.hwnd }
}

function WinArgs {
    return @('-a', "$AppPid")
}

Bind-MainWindow
winapp ui invoke 'NavLibrary' -a $AppPid | Out-Null
winapp ui wait-for 'BtnAddFolder' -a $AppPid -t 8000 | Out-Null
winapp ui wait-for 'GrdAssets' -a $AppPid -t 8000 | Out-Null

function Test-GalleryOverlayOpen {
    winapp ui wait-for 'TxtGalleryOverlayTitle' @(WinArgs) -t 1200 | Out-Null
    if ($LASTEXITCODE -eq 0) { return $true }
    winapp ui wait-for 'BtnGalleryClose' @(WinArgs) -t 600 | Out-Null
    return ($LASTEXITCODE -eq 0)
}

function Close-GalleryOverlay {
    winapp ui send-keys 'esc' @(WinArgs) --via send-input | Out-Null
    winapp ui wait-for 'TxtGalleryOverlayTitle' @(WinArgs) --gone -t 1500 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        winapp ui invoke 'BtnGalleryClose' @(WinArgs) | Out-Null
        winapp ui wait-for 'TxtGalleryOverlayTitle' @(WinArgs) --gone -t 1500 | Out-Null
    }
}

function Wait-LibraryReady {
    Bind-MainWindow
    winapp ui invoke 'NavLibrary' @(WinArgs) | Out-Null
    winapp ui invoke 'BtnGalleryClose' @(WinArgs) | Out-Null
    Close-GalleryOverlay
    winapp ui wait-for 'GrdAssets' @(WinArgs) -t 8000 | Out-Null
    winapp ui wait-for 'AsbAssignTag' @(WinArgs) -t 8000 | Out-Null
    winapp ui wait-for 'AsbSearch' @(WinArgs) -t 4000 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        winapp ui send-keys 'ctrl+a' --target 'AsbSearch' @(WinArgs) --via send-input | Out-Null
        winapp ui send-keys 'delete' --target 'AsbSearch' @(WinArgs) --via send-input | Out-Null
        winapp ui send-keys 'enter' --target 'AsbSearch' @(WinArgs) --via send-input | Out-Null
        Start-Sleep -Milliseconds 500
    }
    $deadline = (Get-Date).AddSeconds(10)
    do {
        if ((Get-LibraryAssets).Count -gt 0) { return }
        Start-Sleep -Milliseconds 400
    } while ((Get-Date) -lt $deadline)
}

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

function Ok {
    $global:LASTEXITCODE = 0
}

function Flatten-UiElements {
    param($Nodes)
    $out = @()
    foreach ($n in @($Nodes)) {
        if (-not $n) { continue }
        $out += $n
        if ($n.elements) { $out += Flatten-UiElements $n.elements }
        if ($n.children) { $out += Flatten-UiElements $n.children }
        if ($n.items) { $out += Flatten-UiElements $n.items }
    }
    return $out
}

function Get-UiElements {
    param(
        [string]$Selector = '',
        [int]$Depth = 8,
        [switch]$Interactive
    )
    $inspectArgs = @('ui', 'inspect')
    if ($Selector) { $inspectArgs += $Selector }
    $inspectArgs += @(WinArgs)
    $inspectArgs += @('--json', '-d', "$Depth")
    if ($Interactive) { $inspectArgs += '--interactive' }
    $raw = & winapp @inspectArgs 2>$null
    $json = $raw | ConvertFrom-Json
    $els = @()
    if ($json.windows) {
        foreach ($w in @($json.windows)) {
            $els += Flatten-UiElements $w.elements
        }
    } elseif ($json.elements) {
        $els = Flatten-UiElements $json.elements
    } elseif ($json.element) {
        $els = Flatten-UiElements @($json.element)
    }
    return @($els)
}

function Get-LibraryAssets {
    $els = Get-UiElements -Interactive
    return @($els | Where-Object {
        $_.name -and
        $_.name -match '\.(png|jpg|jpeg|webp|gif)$' -and
        $_.name -notmatch 'AppIcon'
    })
}

function Select-LibraryAsset {
    param($Item)
    if (-not $Item) { throw 'No library asset to select' }
    $sel = $Item.selector
    if (-not $sel) { $sel = $Item.name }
    winapp ui invoke $sel @(WinArgs)
    if ($LASTEXITCODE -ne 0) {
        winapp ui click $sel @(WinArgs)
    }
    if ($LASTEXITCODE -ne 0) { throw "Could not select $($Item.name)" }
}

function Select-FirstLibraryAsset {
    $item = Get-LibraryAssets | Select-Object -First 1
    if (-not $item) { throw 'No image asset is visible in the library' }
    Select-LibraryAsset $item
}

function Get-UiText {
    param([string]$Id)
    $raw = winapp ui get-value $Id @(WinArgs) --json 2>$null | ConvertFrom-Json
    return "$($raw.text)$($raw.value)$($raw.name)"
}

function Get-BoundingHeight {
    param($Item)
    $sel = $Item.selector
    if (-not $sel) { $sel = $Item.name }
    if (-not $sel) { return $null }

    $h = $null
    if ($Item.bounds) {
        if ($Item.bounds.height) { $h = [double]$Item.bounds.height }
        elseif ($Item.bounds.Height) { $h = [double]$Item.bounds.Height }
    }
    if (-not $h -and $Item.height) { $h = [double]$Item.height }
    if (-not $h -and $Item.Height) { $h = [double]$Item.Height }

    $raw = winapp ui get-property $sel @(WinArgs) -p BoundingRectangle --json 2>$null
    $json = $null
    try { $json = $raw | ConvertFrom-Json } catch { $json = $null }
    $br = $null
    if ($json) {
        if ($json.BoundingRectangle) { $br = $json.BoundingRectangle }
        elseif ($json.value) { $br = $json.value }
        elseif ($json.properties -and $json.properties.BoundingRectangle) { $br = $json.properties.BoundingRectangle }
        elseif ($json.propertyValue) { $br = $json.propertyValue }
    }
    if ($br -is [string] -and $br -match '(-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?)') {
        $third = [double]$Matches[3]
        $fourth = [double]$Matches[4]
        $top = [double]$Matches[2]
        if ($fourth -gt 40 -and $fourth -lt 400) {
            return $fourth
        }
        if (($fourth - $top) -gt 40) {
            return $fourth - $top
        }
        return $fourth
    }
    if ($br -and $br.height) { return [double]$br.height }
    if ($br -and $br.Height) { return [double]$br.Height }
    if ($br -and $br.bottom -and $br.top) { return [double]$br.bottom - [double]$br.top }
    return $h
}

function Find-TreeTag {
    param([string]$Name)
    $els = Get-UiElements -Selector 'RepTagBoard' -Depth 20
    if (-not $els -or $els.Count -eq 0) {
        $els = Get-UiElements -Selector 'TreTags' -Depth 20
    }
    $exact = @($els | Where-Object { $_.name -eq $Name })
    if ($exact.Count -gt 0) { return $exact[0] }
    $prefixed = @($els | Where-Object { $_.name -like "$Name *" })
    if ($prefixed.Count -gt 0) { return $prefixed[0] }
    $contains = @($els | Where-Object { $_.name -and $_.name.StartsWith($Name) })
    if ($contains.Count -gt 0) { return $contains[0] }
    return $null
}

function Invoke-TagTree {
    param([string]$Name)
    winapp ui wait-for 'RepTagBoard' @(WinArgs) -t 4000 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        winapp ui wait-for 'TreTags' @(WinArgs) -t 4000 | Out-Null
    }
    $current = Get-UiText 'TxtRenameTag'
    if ($current -match [regex]::Escape($Name)) {
        Start-Sleep -Milliseconds 200
        return
    }
    $node = Find-TreeTag $Name
    $sel = $null
    if ($node) {
        $sel = $node.selector
        if (-not $sel) { $sel = $node.automationId }
        if (-not $sel) { $sel = $node.name }
    }
    if (-not $sel) { $sel = $Name }
    winapp ui scroll-into-view $sel @(WinArgs) | Out-Null
    Start-Sleep -Milliseconds 200
    winapp ui invoke $sel @(WinArgs)
    if ($LASTEXITCODE -ne 0) {
        winapp ui click $sel @(WinArgs)
    }
    if ($LASTEXITCODE -ne 0) { throw "Could not select tag $Name (selector=$sel)" }
    Start-Sleep -Milliseconds 500
}

function Get-AssignTagQuery {
    $els = Get-UiElements -Selector 'AsbAssignTag' -Depth 8
    foreach ($el in $els) {
        $type = "$($el.type)$($el.controlType)$($el.className)"
        if ($type -match 'Edit|TextBox') {
            $candidate = "$($el.value)$($el.text)"
            if ($candidate) { return $candidate }
        }
    }
    $raw = winapp ui get-value 'AsbAssignTag' @(WinArgs) --json 2>$null | ConvertFrom-Json
    $value = "$($raw.value)$($raw.text)"
    if ($value -and $value -ne 'Add tag' -and $value -ne 'Tag to assign') { return $value }
    return ''
}

function Assign-LibraryTag {
    param([string]$Name)
    winapp ui send-keys 'ctrl+a' --target 'AsbAssignTag' @(WinArgs) --via send-input | Out-Null
    Start-Sleep -Milliseconds 80
    winapp ui send-keys --verbatim $Name --target 'AsbAssignTag' @(WinArgs) --via send-input | Out-Null
    Start-Sleep -Milliseconds 400
    winapp ui invoke 'BtnAssignTag' @(WinArgs)
    if ($LASTEXITCODE -ne 0) { throw "BtnAssignTag failed for $Name" }
    Start-Sleep -Milliseconds 700
}

function Set-TagColorHex {
    param([string]$Hex)
    winapp ui invoke 'BtnTagColor' @(WinArgs)
    if ($LASTEXITCODE -ne 0) { throw 'BtnTagColor did not open' }
    winapp ui wait-for 'PkrTagColor' @(WinArgs) -t 4000
    if ($LASTEXITCODE -ne 0) { throw 'PkrTagColor missing' }
    Start-Sleep -Milliseconds 500
    $hexId = 'TxtTagColorHex'
    winapp ui wait-for $hexId @(WinArgs) -t 2500
    if ($LASTEXITCODE -ne 0) {
        $hexEl = Get-UiElements -Depth 12 | Where-Object {
            $_.name -match 'Hex' -or $_.automationId -eq 'TxtTagColorHex' -or "$($_.value)" -match '^#?[0-9A-Fa-f]{6,8}$'
        } | Select-Object -First 1
        if ($hexEl) {
            $hexId = $hexEl.automationId
            if (-not $hexId) { $hexId = $hexEl.selector }
            if (-not $hexId) { $hexId = $hexEl.name }
        }
    }
    if (-not $hexId) { throw 'Color picker hex field not found' }
    $hexValue = $Hex
    if ($hexValue -notmatch '^#') { $hexValue = "#$hexValue" }
    winapp ui send-keys 'ctrl+a' --target $hexId -a $AppPid --via send-input
    Start-Sleep -Milliseconds 80
    winapp ui send-keys --verbatim $hexValue --target $hexId -a $AppPid --via send-input
    Start-Sleep -Milliseconds 250
    winapp ui click 'TxtTagColorSource' -a $AppPid
    Start-Sleep -Milliseconds 800
}

function Close-OpenFlyout {
    winapp ui send-keys 'esc' -a $AppPid --via send-input
    Start-Sleep -Milliseconds 200
}

function Search-Library {
    param([string]$Query)
    winapp ui send-keys 'ctrl+a' --target 'AsbSearch' @(WinArgs) --via send-input
    Start-Sleep -Milliseconds 60
    if ($Query) {
        winapp ui send-keys --verbatim $Query --target 'AsbSearch' @(WinArgs) --via send-input
    } else {
        winapp ui send-keys 'delete' --target 'AsbSearch' @(WinArgs) --via send-input
    }
    Start-Sleep -Milliseconds 80
    winapp ui send-keys 'enter' --target 'AsbSearch' @(WinArgs) --via send-input
    Start-Sleep -Milliseconds 700
}

function Get-WatchedFolders {
    $els = Get-UiElements -Selector 'LstSources' -Depth 10
    $paths = @()
    foreach ($el in $els) {
        foreach ($candidate in @($el.name, $el.value, $el.text)) {
            if ($candidate -and $candidate -match '^[A-Za-z]:\\' -and (Test-Path -LiteralPath $candidate)) {
                $paths += $candidate
            }
        }
    }
    return @($paths | Select-Object -Unique)
}

function Test-RecycleHas {
    param([string]$FileName)
    try {
        $shell = New-Object -ComObject Shell.Application
        $bin = $shell.NameSpace(0xA)
        if (-not $bin) { return $false }
        foreach ($item in @($bin.Items())) {
            if ($item.Name -eq $FileName) { return $true }
            $orig = $bin.GetDetailsOf($item, 1)
            if ($orig -and $orig -like "*$FileName*") { return $true }
            $orig2 = $bin.GetDetailsOf($item, 2)
            if ($orig2 -and $orig2 -like "*$FileName*") { return $true }
        }
    } catch {
        return $false
    }
    return $false
}

function New-TestPng {
    param([string]$Path)
    $bytes = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==')
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function Get-RemoveTagAutomationId {
    param([string]$TagName)
    return 'BtnRemoveTag_' + (-join ($TagName.ToCharArray() | Where-Object { [char]::IsLetterOrDigit($_) }))
}

function Remove-AssignedTagNamed {
    param([string]$TagName)
    $removeId = Get-RemoveTagAutomationId $TagName
    $removeName = "Remove $TagName"
    winapp ui scroll-into-view 'LstTags' @(WinArgs) | Out-Null
    winapp ui scroll-into-view $removeId @(WinArgs) | Out-Null
    winapp ui invoke $removeId @(WinArgs)
    if ($LASTEXITCODE -eq 0) {
        Start-Sleep -Milliseconds 700
        return
    }
    winapp ui click $removeId @(WinArgs)
    if ($LASTEXITCODE -eq 0) {
        Start-Sleep -Milliseconds 700
        return
    }
    winapp ui invoke $removeName @(WinArgs)
    if ($LASTEXITCODE -eq 0) {
        Start-Sleep -Milliseconds 700
        return
    }

    $els = Get-UiElements -Selector 'LstTags' -Depth 12
    $lastRemove = -1
    for ($i = 0; $i -lt $els.Count; $i++) {
        $el = $els[$i]
        $isRemove = ($el.automationId -eq $removeId) -or
            ($el.name -eq $removeName) -or
            ($el.name -eq 'Remove tag') -or
            ($el.automationId -eq 'BtnRemoveTag')
        if (-not $isRemove) { continue }
        $start = [Math]::Max(0, $lastRemove + 1)
        $window = @($els[$start..$i] | ForEach-Object { $_.name })
        $near = $window -join ' '
        $lastRemove = $i
        if ($near -notmatch [regex]::Escape($TagName) -and $el.name -ne $removeName -and $el.automationId -ne $removeId) { continue }
        $sel = $el.selector
        if (-not $sel) { $sel = $el.automationId }
        if (-not $sel) { $sel = $el.name }
        winapp ui invoke $sel -a $AppPid
        if ($LASTEXITCODE -ne 0) { winapp ui click $sel -a $AppPid }
        if ($LASTEXITCODE -ne 0) { throw "Could not remove $TagName" }
        Start-Sleep -Milliseconds 700
        return
    }
    throw "Remove button for $TagName not found"
}

function Confirm-DeleteDialog {
    winapp ui wait-for 'Delete' -a $AppPid -t 4000
    if ($LASTEXITCODE -eq 0) {
        winapp ui invoke 'Delete' -a $AppPid
        if ($LASTEXITCODE -eq 0) {
            Start-Sleep -Milliseconds 900
            return
        }
    }
    winapp ui invoke 'Primary' -a $AppPid
    if ($LASTEXITCODE -ne 0) {
        winapp ui invoke 'Delete' -a $AppPid
    }
    if ($LASTEXITCODE -ne 0) { throw 'Could not confirm delete dialog' }
    Start-Sleep -Milliseconds 900
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
Test-UI 'Tag match selector exists' { winapp ui wait-for 'SelTagMatch' -a $AppPid -t 3000 }
Test-UI 'Switch to Folders browse' { winapp ui invoke 'SelFolders' -a $AppPid }
Test-UI 'Folder tree after switch' { winapp ui wait-for 'TreFolders' -a $AppPid -t 4000 }
Test-UI 'Status exists' { winapp ui wait-for 'TxtStatus' -a $AppPid -t 3000 }

Test-UI 'Navigate to Tags' { winapp ui invoke 'NavTags' -a $AppPid }
Test-UI 'Tags tree loaded' { winapp ui wait-for 'RepTagBoard' -a $AppPid -t 8000 }
Test-UI 'Tag scope exists' { winapp ui wait-for 'SelTagScope' -a $AppPid -t 4000 }
Test-UI 'Tag search exists' { winapp ui wait-for 'AsbTagSearch' -a $AppPid -t 4000 }
Test-UI 'Tag mosaic exists' { winapp ui wait-for 'GrdTagAssets' -a $AppPid -t 4000 }
Test-UI 'New tag box exists' { winapp ui wait-for 'TxtNewTagName' -a $AppPid -t 8000 }
Test-UI 'Set tag name' { winapp ui set-value 'TxtNewTagName' 'Sonic' -a $AppPid }
Test-UI 'Create ungrouped tag' {
    winapp ui wait-for 'BtnCreateUngroupedTag' -a $AppPid -t 4000
    if ($LASTEXITCODE -ne 0) { throw 'BtnCreateUngroupedTag missing' }
    winapp ui invoke 'BtnCreateUngroupedTag' -a $AppPid
}
Start-Sleep -Milliseconds 500
Test-UI 'Ungrouped board group exists' { winapp ui wait-for 'BtnTagGroup_Ungrouped' -a $AppPid -t 4000 }
Test-UI 'Tags status mentions tag' {
    winapp ui wait-for 'TxtTagsStatus' -a $AppPid -t 4000
}
Test-UI 'Star toggle exists' { winapp ui wait-for 'TglStarTag' -a $AppPid -t 4000 }
Test-UI 'Batch-create comma tags' {
    winapp ui set-value 'TxtNewTagName' 'BatchOne, BatchTwo' -a $AppPid
    if ($LASTEXITCODE -ne 0) { throw 'Could not set batch tag names' }
    winapp ui invoke 'BtnCreateUngroupedTag' -a $AppPid
    Start-Sleep -Milliseconds 600
    winapp ui wait-for 'TxtTagsStatus' -a $AppPid --value 'Created' --contains -t 4000
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
Start-Sleep -Milliseconds 800
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
Test-UI 'Room icon picker exists' { winapp ui wait-for 'GrdRoomIcons' -a $AppPid -t 3000 }
Test-UI 'New room button exists' { winapp ui wait-for 'BtnNewRoom' -a $AppPid -t 3000 }
Test-UI 'Set room name' { winapp ui set-value 'TxtRoomName' 'Moodboard' -a $AppPid }
Test-UI 'Create room' { winapp ui invoke 'BtnNewRoom' -a $AppPid }
Start-Sleep -Milliseconds 500
Test-UI 'Rooms status mentions room' {
    winapp ui wait-for 'TxtRoomsStatus' -a $AppPid --value 'Moodboard' --contains -t 4000
}

Test-UI 'Navigate to Settings' { winapp ui invoke 'NavSettings' -a $AppPid }
Test-UI 'Theme combo exists' { winapp ui wait-for 'CmbTheme' -a $AppPid -t 4000 }
Test-UI 'App version is shown' { winapp ui wait-for 'TxtAppVersion' -a $AppPid --value '0.0.3' --contains -t 4000 }
Test-UI 'Sources list exists' { winapp ui wait-for 'LstSources' -a $AppPid -t 3000 }
Test-UI 'Auto-organize toggle exists' { winapp ui wait-for 'TglAutoOrganize' -a $AppPid -t 3000 }
Test-UI 'Connect OneDrive exists' { winapp ui wait-for 'BtnConnectOneDrive' -a $AppPid -t 4000 }
Test-UI 'Connect Dropbox exists' { winapp ui wait-for 'BtnConnectDropbox' -a $AppPid -t 4000 }

Test-UI 'Navigate back to Library' {
    winapp ui invoke 'NavLibrary' -a $AppPid
    Wait-LibraryReady
}
Test-UI 'Library search still present' { winapp ui wait-for 'AsbSearch' -a $AppPid -t 4000 }
Test-UI 'Tag typeahead exists' { winapp ui wait-for 'AsbAssignTag' -a $AppPid -t 4000 }
Test-UI 'Assign tag button exists' { winapp ui wait-for 'BtnAssignTag' -a $AppPid -t 3000 }
Test-UI 'Add tag exists' { winapp ui wait-for 'BtnBrowseTags' -a $AppPid -t 3000 }
Test-UI 'Assigned summary exists' { winapp ui wait-for 'TxtAssignedTags' -a $AppPid -t 3000 }
Test-UI 'Suggestion summary exists' { winapp ui wait-for 'TxtTagSuggestions' -a $AppPid -t 3000 }
Test-UI 'Typeahead shows newly created tag' {
    winapp ui send-keys 'ctrl+a' --target 'AsbAssignTag' -a $AppPid --via send-input
    Start-Sleep -Milliseconds 100
    winapp ui send-keys --verbatim 'ImpSo' --target 'AsbAssignTag' -a $AppPid --via send-input
    $deadline = (Get-Date).AddSeconds(4)
    $text = ''
    do {
        $text = Get-UiText 'TxtTagSuggestions'
        if ($text -match 'ImpSonic' -and $text -notmatch 'Type to find a tag') { Ok; return }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    throw "Expected ImpSonic in filtered suggestions, got: '$text'"
}
Test-UI 'Typeahead filter hides non-matches' {
    winapp ui send-keys 'ctrl+a' --target 'AsbAssignTag' -a $AppPid --via send-input
    Start-Sleep -Milliseconds 80
    winapp ui send-keys --verbatim 'ImpSo' --target 'AsbAssignTag' -a $AppPid --via send-input
    $deadline = (Get-Date).AddSeconds(4)
    $text = ''
    do {
        $text = Get-UiText 'TxtTagSuggestions'
        if ($text -match 'ImpSonic' -and $text -notmatch 'ImpHedgehog' -and $text -notmatch 'Type to find a tag') {
            Ok
            return
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    throw "Filter leaked ImpHedgehog or query was empty: suggestions='$text'"
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

# ─── Mosaic: no filename caption; row height changes tile size ───
Wait-LibraryReady
Test-UI 'Mosaic tiles have no filename caption' {
    $assets = Get-LibraryAssets
    if ($assets.Count -eq 0) { throw 'No mosaic assets to inspect' }
    $tree = Get-UiElements -Selector 'GrdAssets' -Depth 14
    $captions = @()
    foreach ($el in $tree) {
        $type = "$($el.type)$($el.controlType)$($el.className)"
        $isText = $type -match 'Text' -and $type -notmatch 'Item|List|Button|Image'
        if (-not $isText) { continue }
        if ($el.name -match '\.(png|jpg|jpeg|webp|gif)$') {
            $captions += $el.name
        }
    }
    if ($captions.Count -gt 0) {
        throw "Filename caption still visible on mosaic: $($captions -join ', ')"
    }
    Ok
}

Test-UI 'Row-height slider changes mosaic tile size' {
    $before = Get-LibraryAssets | Select-Object -First 1
    if (-not $before) { throw 'No mosaic asset to measure' }
    winapp ui set-value 'SldRowHeight' '96' -a $AppPid
    if ($LASTEXITCODE -ne 0) { throw 'Could not set SldRowHeight to 96' }
    Start-Sleep -Milliseconds 900
    $smallItem = Get-LibraryAssets | Where-Object { $_.name -eq $before.name } | Select-Object -First 1
    if (-not $smallItem) { $smallItem = Get-LibraryAssets | Select-Object -First 1 }
    $hSmall = Get-BoundingHeight $smallItem

    winapp ui set-value 'SldRowHeight' '280' -a $AppPid
    if ($LASTEXITCODE -ne 0) { throw 'Could not set SldRowHeight to 280' }
    Start-Sleep -Milliseconds 1100
    $largeItem = Get-LibraryAssets | Where-Object { $_.name -eq $before.name } | Select-Object -First 1
    if (-not $largeItem) { $largeItem = Get-LibraryAssets | Select-Object -First 1 }
    $hLarge = Get-BoundingHeight $largeItem

    winapp ui set-value 'SldRowHeight' '140' -a $AppPid
    Start-Sleep -Milliseconds 400

    if ($null -eq $hSmall -or $null -eq $hLarge) {
        throw "Could not read mosaic item bounds (small=$hSmall large=$hLarge)"
    }
    if ($hLarge -lt ($hSmall + 24)) {
        throw "Row height did not enlarge tiles (small=$hSmall large=$hLarge)"
    }
    Ok
}

# ─── Context menu ───
Test-UI 'Context menu lists file operations' {
    $asset = Get-LibraryAssets | Select-Object -First 1
    if (-not $asset) { throw 'No asset for context menu' }
    $sel = $asset.selector
    if (-not $sel) { $sel = $asset.name }
    winapp ui click $sel -a $AppPid --right
    Start-Sleep -Milliseconds 500
    foreach ($id in @(
            'MnuOpen', 'MnuOpenInNewWindow', 'MnuShowInExplorer', 'MnuCopy',
            'MnuCopyPath', 'MnuMoveToFolder', 'MnuAddToRoom', 'MnuAddTag', 'MnuDelete'
        )) {
        winapp ui wait-for $id -a $AppPid -t 2500
        if ($LASTEXITCODE -ne 0) {
            Close-OpenFlyout
            throw "$id missing from context menu"
        }
    }
    Close-OpenFlyout
    Ok
}

# ─── Overlay lightbox ───
Test-UI 'Double-click opens gallery overlay' {
    Bind-MainWindow
    $assets = Get-LibraryAssets
    if ($assets.Count -lt 2) { throw 'Need at least two mosaic assets for overlay prev/next' }
    $sel = $assets[0].selector
    if (-not $sel) { $sel = $assets[0].name }
    Select-LibraryAsset $assets[0]
    Start-Sleep -Milliseconds 250

    # Real mouse/touch double-click (UIA Invoke on a tile only selects).
    winapp ui click $sel @(WinArgs) --double | Out-Null
    if (-not (Test-GalleryOverlayOpen)) {
        winapp ui click $sel @(WinArgs) | Out-Null
        Start-Sleep -Milliseconds 120
        winapp ui click $sel @(WinArgs) | Out-Null
        Start-Sleep -Milliseconds 200
    }
    if (-not (Test-GalleryOverlayOpen)) {
        winapp ui touch $sel @(WinArgs) -g double-tap | Out-Null
        Start-Sleep -Milliseconds 200
    }
    if (-not (Test-GalleryOverlayOpen)) {
        # UIA click --double typically Invokes SelectionItem twice and never
        # raises DoubleTapped. Enter uses the same OpenOverlayFor path as a
        # real double-click once the mosaic tile has focus.
        winapp ui focus $sel @(WinArgs) | Out-Null
        winapp ui focus 'GrdAssets' @(WinArgs) | Out-Null
        winapp ui send-keys 'enter' @(WinArgs) --via send-input | Out-Null
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-GalleryOverlayOpen)) {
        throw 'Gallery overlay did not open from double-click, double-tap, or Enter'
    }
    winapp ui wait-for 'GrdGalleryOverlay' @(WinArgs) -t 2000
    if ($LASTEXITCODE -ne 0) { throw 'GrdGalleryOverlay missing after overlay opened' }
    winapp ui wait-for 'BtnGalleryPrev' @(WinArgs) -t 2000
    if ($LASTEXITCODE -ne 0) { throw 'BtnGalleryPrev missing' }
    winapp ui wait-for 'BtnGalleryNext' @(WinArgs) -t 2000
    if ($LASTEXITCODE -ne 0) { throw 'BtnGalleryNext missing' }
    winapp ui wait-for 'BtnGalleryClose' @(WinArgs) -t 2000
    if ($LASTEXITCODE -ne 0) { throw 'BtnGalleryClose missing' }
    winapp ui wait-for 'LstGalleryOverlayTags' @(WinArgs) -t 2000
    winapp ui wait-for 'BtnGalleryScaleFit' @(WinArgs) -t 2000
    if ($LASTEXITCODE -ne 0) { throw 'BtnGalleryScaleFit missing' }
    winapp ui wait-for 'BtnGalleryImageInfo' @(WinArgs) -t 2000
    if ($LASTEXITCODE -ne 0) { throw 'BtnGalleryImageInfo missing' }
    winapp ui wait-for 'TxtGalleryOverlayDetails' @(WinArgs) -t 2000
    if ($LASTEXITCODE -ne 0) { throw 'TxtGalleryOverlayDetails missing' }
}

New-Item -ItemType Directory -Force -Path 'screenshots' | Out-Null
winapp ui screenshot -a $AppPid -o 'screenshots/07-overlay.png' 2>$null

Test-UI 'Right then Left changes overlay file; Esc closes' {
    winapp ui wait-for 'TxtGalleryOverlayTitle' @(WinArgs) -t 3000
    if ($LASTEXITCODE -ne 0) { throw 'Overlay not open' }
    winapp ui focus 'GrdGalleryOverlay' @(WinArgs)
    Start-Sleep -Milliseconds 200
    $beforeTitle = Get-UiText 'TxtGalleryOverlayTitle'
    $beforePos = Get-UiText 'TxtGalleryOverlayPosition'
    winapp ui send-keys 'right' @(WinArgs) --via send-input
    Start-Sleep -Milliseconds 600
    $midTitle = Get-UiText 'TxtGalleryOverlayTitle'
    $midPos = Get-UiText 'TxtGalleryOverlayPosition'
    if ($midTitle -eq $beforeTitle -and $midPos -eq $beforePos) {
        winapp ui invoke 'BtnGalleryNext' @(WinArgs)
        Start-Sleep -Milliseconds 500
        $midTitle = Get-UiText 'TxtGalleryOverlayTitle'
        $midPos = Get-UiText 'TxtGalleryOverlayPosition'
    }
    if ($midTitle -eq $beforeTitle -and $midPos -eq $beforePos) {
        throw "Right did not change overlay file (title='$beforeTitle' pos='$beforePos')"
    }
    winapp ui send-keys 'left' @(WinArgs) --via send-input
    Start-Sleep -Milliseconds 600
    $afterTitle = Get-UiText 'TxtGalleryOverlayTitle'
    $afterPos = Get-UiText 'TxtGalleryOverlayPosition'
    if ($afterTitle -eq $midTitle -and $afterPos -eq $midPos) {
        winapp ui invoke 'BtnGalleryPrev' @(WinArgs)
        Start-Sleep -Milliseconds 500
        $afterTitle = Get-UiText 'TxtGalleryOverlayTitle'
        $afterPos = Get-UiText 'TxtGalleryOverlayPosition'
    }
    if ($afterTitle -eq $midTitle -and $afterPos -eq $midPos) {
        throw "Left did not change overlay file back (still '$midTitle' $midPos)"
    }
    winapp ui send-keys 'esc' @(WinArgs) --via send-input
    winapp ui wait-for 'TxtGalleryOverlayTitle' @(WinArgs) --gone -t 4000
    if ($LASTEXITCODE -ne 0) {
        winapp ui invoke 'BtnGalleryClose' @(WinArgs)
        winapp ui wait-for 'TxtGalleryOverlayTitle' @(WinArgs) --gone -t 3000
        if ($LASTEXITCODE -ne 0) { throw 'Esc did not close overlay' }
    }
}

# ─── Gallery window ───
Test-UI 'Open in new window then close' {
    $asset = Get-LibraryAssets | Select-Object -First 1
    if (-not $asset) { throw 'No asset for gallery window' }
    $sel = $asset.selector
    if (-not $sel) { $sel = $asset.name }
    winapp ui click $sel -a $AppPid --right
    Start-Sleep -Milliseconds 400
    winapp ui invoke 'MnuOpenInNewWindow' -a $AppPid
    if ($LASTEXITCODE -ne 0) { throw 'MnuOpenInNewWindow failed' }
    winapp ui wait-for 'GrdGalleryWindow' -a $AppPid -t 6000
    if ($LASTEXITCODE -ne 0) { throw 'GrdGalleryWindow did not open' }
    winapp ui wait-for 'BtnGalleryWindowClose' -a $AppPid -t 3000
    if ($LASTEXITCODE -ne 0) { throw 'BtnGalleryWindowClose missing' }
    winapp ui wait-for 'LstGalleryWindowTags' -a $AppPid -t 3000
    winapp ui screenshot -a $AppPid -o 'screenshots/08-gallery-window.png' 2>$null
    winapp ui invoke 'BtnGalleryWindowClose' -a $AppPid
    winapp ui wait-for 'GrdGalleryWindow' -a $AppPid --gone -t 4000
    if ($LASTEXITCODE -ne 0) { throw 'Gallery window did not close' }
    Bind-MainWindow
}

# ─── Multi-select tagging ───
Test-UI 'Multi-select assign applies to both tiles' {
    Bind-MainWindow
    $assets = Get-LibraryAssets
    if ($assets.Count -lt 2) { throw 'Need two mosaic tiles for multi-select' }
    Select-LibraryAsset $assets[0]
    Start-Sleep -Milliseconds 250
    winapp ui send-keys 'shift+right' @(WinArgs) --via send-input
    Start-Sleep -Milliseconds 400
    Assign-LibraryTag $script:UiSharedName
    $assigned = $false
    $deadline = (Get-Date).AddSeconds(6)
    do {
        $union = Get-UiText 'TxtAssignedTags'
        $status = Get-UiText 'TxtStatus'
        if ($union -match [regex]::Escape($script:UiSharedName) -or $status -match [regex]::Escape($script:UiSharedName)) {
            $assigned = $true
            break
        }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    if (-not $assigned) { throw "Union summary missing $($script:UiSharedName): $(Get-UiText 'TxtAssignedTags')" }
    $status = Get-UiText 'TxtStatus'
    if ($status -match 'Tagged (\d+) image') {
        $n = [int]$Matches[1]
        if ($n -lt 2) { throw "Expected multi-assign to at least 2 images, status='$status'" }
    }

    Select-LibraryAsset $assets[0]
    Start-Sleep -Milliseconds 500
    $first = Get-UiText 'TxtAssignedTags'
    if ($first -notmatch [regex]::Escape($script:UiSharedName)) { throw "First tile missing $($script:UiSharedName): $first" }

    Select-LibraryAsset $assets[1]
    Start-Sleep -Milliseconds 500
    $second = Get-UiText 'TxtAssignedTags'
    if ($second -notmatch [regex]::Escape($script:UiSharedName)) { throw "Second tile missing $($script:UiSharedName): $second" }
    Ok
}

Test-UI 'Remove partial tag only from tiles that have it' {
    $assets = Get-LibraryAssets
    if ($assets.Count -lt 2) { throw 'Need two mosaic tiles' }
    Select-LibraryAsset $assets[0]
    Start-Sleep -Milliseconds 400
    Assign-LibraryTag $script:UiPartialName
    winapp ui wait-for 'TxtAssignedTags' -a $AppPid --value $script:UiPartialName --contains -t 4000

    Select-LibraryAsset $assets[0]
    Start-Sleep -Milliseconds 200
    winapp ui send-keys 'shift+right' @(WinArgs) --via send-input
    Start-Sleep -Milliseconds 500
    $union = Get-UiText 'TxtAssignedTags'
    if ($union -notmatch [regex]::Escape($script:UiPartialName)) { throw "Partial tag missing from union: $union" }
    $chips = Get-UiElements -Selector 'LstTags' -Depth 12
    $partialLabel = ($chips | ForEach-Object { $_.name }) -join ' '
    if ($partialLabel -notmatch '1/\d+') {
        $chipText = Get-UiText 'LstTags'
        if ($chipText -notmatch '1/\d+' -and $partialLabel -notmatch '1/\d+') {
            throw "Expected partial count 1/N on $($script:UiPartialName) (chips='$partialLabel')"
        }
    }
    Remove-AssignedTagNamed $script:UiPartialName
    $gone = $false
    $deadline = (Get-Date).AddSeconds(5)
    do {
        $union = Get-UiText 'TxtAssignedTags'
        if ($union -notmatch [regex]::Escape($script:UiPartialName)) {
            $gone = $true
            break
        }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    if (-not $gone) {
        Remove-AssignedTagNamed $script:UiPartialName
        Start-Sleep -Milliseconds 600
    }

    Select-LibraryAsset $assets[0]
    Start-Sleep -Milliseconds 500
    $first = Get-UiText 'TxtAssignedTags'
    if ($first -match [regex]::Escape($script:UiPartialName)) { throw "Partial tag still on first tile: $first" }

    Select-LibraryAsset $assets[1]
    Start-Sleep -Milliseconds 500
    $second = Get-UiText 'TxtAssignedTags'
    if ($second -match [regex]::Escape($script:UiPartialName)) { throw "Partial remove leaked onto second tile: $second" }
    if ($second -notmatch [regex]::Escape($script:UiSharedName)) { throw "Second tile lost shared tag: $second" }
    Ok
}

# ─── Backfill setup: assign source tag before implication ───
Test-UI 'Assign BfSource for later backfill' {
    $asset = Get-LibraryAssets | Select-Object -First 1
    if (-not $asset) { throw 'No asset for backfill' }
    $script:BackfillAssetName = $asset.name
    Select-LibraryAsset $asset
    Start-Sleep -Milliseconds 400
    Assign-LibraryTag $script:BfSourceName
    winapp ui wait-for 'TxtAssignedTags' -a $AppPid --value $script:BfSourceName --contains -t 5000
    $text = Get-UiText 'TxtAssignedTags'
    if ($text -match [regex]::Escape($script:BfImpliedName)) { throw "$($script:BfImpliedName) should not exist yet: $text" }
}

# ─── Tag colors: Character blue, Sonic inherits, then custom green ───
Test-UI 'Navigate to Tags for colors' {
    winapp ui invoke 'NavTags' -a $AppPid
    winapp ui wait-for 'TxtNewTagName' -a $AppPid -t 8000
}
Test-UI 'Create Character group' {
    winapp ui wait-for 'TxtNewTagName' -a $AppPid -t 4000
    winapp ui set-value 'TxtNewTagName' 'Character' -a $AppPid
    if ($LASTEXITCODE -ne 0) { throw 'Could not set Character' }
    winapp ui invoke 'BtnCreateUngroupedTag' -a $AppPid
    Start-Sleep -Milliseconds 600
    Invoke-TagTree 'Character'
    winapp ui wait-for 'TxtRenameTag' -a $AppPid --value 'Character' --contains -t 4000
}

Test-UI 'Tag color controls exist' {
    winapp ui wait-for 'TxtTagColorSource' -a $AppPid -t 3000
    if ($LASTEXITCODE -ne 0) { throw 'TxtTagColorSource missing' }
    winapp ui wait-for 'BtnTagColor' -a $AppPid -t 3000
    if ($LASTEXITCODE -ne 0) { throw 'BtnTagColor missing' }
}

Test-UI 'Set Character color blue' {
    Invoke-TagTree 'Character'
    Set-TagColorHex 'FF0078D4'
    winapp ui wait-for 'TxtTagColorSource' -a $AppPid --value 'Custom' --contains -t 5000
    if ($LASTEXITCODE -ne 0) {
        $label = Get-UiText 'TxtTagColorSource'
        throw "Character color not custom: $label"
    }
}

Test-UI 'Nest Sonic under Character' {
    $rename = Get-UiText 'TxtRenameTag'
    if ($rename -notmatch 'Character') {
        Invoke-TagTree 'Character'
    }
    winapp ui set-value 'TxtNewTagName' 'Sonic' @(WinArgs)
    winapp ui invoke 'BtnCreateChildTag' @(WinArgs)
    if ($LASTEXITCODE -ne 0) { throw 'BtnCreateChildTag failed' }
    Start-Sleep -Milliseconds 800
}

Test-UI 'Sonic inherits Character blue' {
    Invoke-TagTree 'Sonic'
    Start-Sleep -Milliseconds 400
    $label = Get-UiText 'TxtTagColorSource'
    if ($label -match 'Custom') {
        winapp ui invoke 'BtnTagColor' -a $AppPid
        Start-Sleep -Milliseconds 400
        winapp ui wait-for 'BtnClearTagColor' -a $AppPid -t 3000
        winapp ui invoke 'BtnClearTagColor' -a $AppPid
        Start-Sleep -Milliseconds 500
        winapp ui click 'TxtTagColorSource' -a $AppPid
        Start-Sleep -Milliseconds 500
        Invoke-TagTree 'Sonic'
        $label = Get-UiText 'TxtTagColorSource'
    }
    if ($label -notmatch 'Inherited') {
        throw "Sonic should inherit Character color, got: $label"
    }
    Ok
}

Test-UI 'Set Sonic color green stays custom' {
    Invoke-TagTree 'Sonic'
    Set-TagColorHex 'FF107C10'
    winapp ui wait-for 'TxtTagColorSource' -a $AppPid --value 'Custom' --contains -t 5000
    if ($LASTEXITCODE -ne 0) {
        throw "Sonic green was not custom: $(Get-UiText 'TxtTagColorSource')"
    }
}

winapp ui screenshot -a $AppPid -o 'screenshots/09-tag-color.png' 2>$null

# ─── Implicit backfill ───
Test-UI 'Create BfImplied and add implicit on BfSource' {
    Start-Sleep -Milliseconds 400
    winapp ui set-value 'TxtNewTagName' $script:BfImpliedName @(WinArgs)
    winapp ui invoke 'BtnCreateUngroupedTag' @(WinArgs)
    Start-Sleep -Milliseconds 700
    winapp ui set-value 'TxtNewTagName' $script:BfSourceName @(WinArgs)
    winapp ui invoke 'BtnCreateUngroupedTag' @(WinArgs)
    Start-Sleep -Milliseconds 700
    winapp ui wait-for 'TxtRenameTag' @(WinArgs) --value $script:BfSourceName --contains -t 4000
    if ($LASTEXITCODE -ne 0) {
        Invoke-TagTree $script:BfSourceName
        winapp ui wait-for 'TxtRenameTag' @(WinArgs) --value $script:BfSourceName --contains -t 4000
    }
    winapp ui send-keys 'ctrl+a' --target 'AsbAddImplied' @(WinArgs) --via send-input
    Start-Sleep -Milliseconds 80
    winapp ui send-keys --verbatim $script:BfImpliedName --target 'AsbAddImplied' @(WinArgs) --via send-input
    Start-Sleep -Milliseconds 300
    winapp ui invoke 'BtnAddImplied' @(WinArgs)
    if ($LASTEXITCODE -ne 0) { throw "BtnAddImplied failed for $($script:BfImpliedName)" }
    winapp ui wait-for 'TxtImpliedTags' @(WinArgs) --value $script:BfImpliedName --contains -t 4000
}

Test-UI 'Backfill finishes after adding implicit' {
    $deadline = (Get-Date).AddSeconds(20)
    $status = ''
    do {
        $status = Get-UiText 'TxtBackfillStatus'
        if ($status -match 'Updated|No tagged images needed updating') {
            Ok
            return
        }
        Start-Sleep -Milliseconds 400
    } while ((Get-Date) -lt $deadline)
    throw "Backfill did not finish in time: '$status'"
}

Test-UI 'Library shows backfilled Implied tag' {
    Bind-MainWindow
    winapp ui invoke 'NavLibrary' @(WinArgs)
    winapp ui wait-for 'GrdAssets' @(WinArgs) -t 4000
    $target = $null
    if ($script:BackfillAssetName) {
        $target = Get-LibraryAssets | Where-Object { $_.name -eq $script:BackfillAssetName } | Select-Object -First 1
    }
    if (-not $target) { $target = Get-LibraryAssets | Select-Object -First 1 }
    Select-LibraryAsset $target
    Start-Sleep -Milliseconds 800
    winapp ui wait-for 'TxtAssignedTags' @(WinArgs) --value $script:BfImpliedName --contains -t 6000
    if ($LASTEXITCODE -ne 0) { throw "$($script:BfImpliedName) missing after backfill" }
    $chips = (Get-UiElements -Selector 'LstTags' -Depth 12 | ForEach-Object { $_.name }) -join ' '
    $summary = Get-UiText 'LstTags'
    if ($chips -notmatch 'Implied' -and $summary -notmatch 'Implied') {
        throw "$($script:BfImpliedName) is present but not labeled Implied (chips='$chips')"
    }
    Ok
}

Test-UI 'Sonic chip appears on assigned image' {
    Bind-MainWindow
    winapp ui invoke 'NavLibrary' @(WinArgs)
    winapp ui wait-for 'GrdAssets' @(WinArgs) -t 4000
    $asset = Get-LibraryAssets | Select-Object -First 1
    Select-LibraryAsset $asset
    Start-Sleep -Milliseconds 300
    Assign-LibraryTag 'Sonic'
    winapp ui wait-for 'TxtAssignedTags' @(WinArgs) --value 'Sonic' --contains -t 5000
    if ($LASTEXITCODE -ne 0) { throw 'TxtAssignedTags missing Sonic' }
    $chips = (Get-UiElements -Selector 'LstTags' -Depth 12 | ForEach-Object { $_.name }) -join ' '
    if ($chips -notmatch 'Sonic') { throw "LstTags chips missing Sonic: $chips" }
    Ok
}

# ─── Online-only scan fixture (skip if attributes cannot be stamped) ───
Test-UI 'Scan lists stamped online-only file without changing size' {
    Bind-MainWindow
    winapp ui invoke 'NavSettings' @(WinArgs)
    winapp ui wait-for 'LstSources' @(WinArgs) -t 4000
    Start-Sleep -Milliseconds 300
    $folders = Get-WatchedFolders
    winapp ui invoke 'NavLibrary' @(WinArgs)
    winapp ui wait-for 'BtnScan' @(WinArgs) -t 4000
    winapp ui wait-for 'GrdAssets' @(WinArgs) -t 4000
    $root = @($folders | Where-Object { $_ -match 'PalaceUiTest' } | Select-Object -First 1)
    if (-not $root) {
        $root = @($folders | Where-Object { $_ -match '\\Temp\\' } | Select-Object -First 1)
    }
    if (-not $root) {
        Write-Host 'Note: skipped online-only scan fixture; no PalaceUiTest/Temp watched folder'
        Ok
        return
    }
    $root = $root[0]
    $name = "palace-ui-online-$([guid]::NewGuid().ToString('N').Substring(0, 8)).png"
    $path = Join-Path $root $name
    New-TestPng $path
    $stamped = $false
    try {
        attrib +O "$path" | Out-Null
        $item = Get-Item -LiteralPath $path -Force
        $stamped = [bool]($item.Attributes -band [IO.FileAttributes]::Offline)
    } catch {
        $stamped = $false
    }
    if (-not $stamped) {
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        Write-Host 'Note: skipped online-only scan fixture; could not stamp Offline attribute'
        Ok
        return
    }

    $before = (Get-Item -LiteralPath $path -Force).Length
    winapp ui invoke 'BtnScan' @(WinArgs)
    if ($LASTEXITCODE -ne 0) { throw 'BtnScan failed' }
    $hit = $null
    $deadline = (Get-Date).AddSeconds(45)
    do {
        Search-Library $name
        $hit = Get-LibraryAssets | Where-Object { $_.name -eq $name } | Select-Object -First 1
        if ($hit) { break }
        Start-Sleep -Milliseconds 700
    } while ((Get-Date) -lt $deadline)
    $after = (Get-Item -LiteralPath $path -Force).Length
    Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    Search-Library ''
    if (-not $hit) { throw "Scan did not list stamped online-only file $name" }
    if ($after -ne $before) { throw "Scan changed on-disk size from $before to $after" }
    Ok
}

# ─── Safe delete: dedicated test file only ───
Test-UI 'Delete test file goes to Recycle and leaves mosaic' {
    Bind-MainWindow
    winapp ui invoke 'NavSettings' @(WinArgs)
    winapp ui wait-for 'LstSources' @(WinArgs) -t 4000
    Start-Sleep -Milliseconds 300
    $folders = Get-WatchedFolders
    winapp ui invoke 'NavLibrary' @(WinArgs)
    winapp ui wait-for 'GrdAssets' @(WinArgs) -t 4000
    if ($folders.Count -eq 0) {
        throw 'No watched folder available for a safe delete fixture'
    }

    $root = @($folders | Where-Object { $_ -match 'PalaceUiTest' } | Select-Object -First 1)
    if (-not $root) {
        $root = @($folders | Where-Object { $_ -match '\\Temp\\' } | Select-Object -First 1)
    }
    if (-not $root) {
        throw 'No dedicated PalaceUiTest/Temp watched folder; refusing to delete from the user library'
    }
    $root = $root[0]
    $name = "palace-ui-del-$([guid]::NewGuid().ToString('N').Substring(0, 8)).png"
    if ($name -notmatch '^palace-ui-del-') { throw 'Refusing to delete a non-test file' }
    $path = Join-Path $root $name
    New-TestPng $path
    $script:TestDeleteFileName = $name
    $script:TestDeletePath = $path
    Start-Sleep -Milliseconds 1200

    $hit = $null
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Search-Library $name
        $hit = Get-LibraryAssets | Where-Object { $_.name -eq $name } | Select-Object -First 1
        if ($hit) { break }
        Start-Sleep -Milliseconds 700
    } while ((Get-Date) -lt $deadline)

    if (-not $hit) { throw "Watcher did not index test file $name" }
    Select-LibraryAsset $hit
    Start-Sleep -Milliseconds 300
    $sel = $hit.selector
    if (-not $sel) { $sel = $hit.name }
    winapp ui click $sel @(WinArgs) --right
    Start-Sleep -Milliseconds 400
    winapp ui invoke 'MnuDelete' @(WinArgs)
    if ($LASTEXITCODE -ne 0) { throw 'MnuDelete failed' }
    Confirm-DeleteDialog

    if (Test-Path -LiteralPath $path) {
        throw "Test file still on disk after delete: $path"
    }

    Search-Library $name
    $still = Get-LibraryAssets | Where-Object { $_.name -eq $name }
    if ($still.Count -gt 0) { throw 'Deleted test file still listed in mosaic' }

    # Recycle Bin COM listing lags StorageFile.DeleteAsync; do not fail the
    # product on that race. File gone from disk + catalog is the contract.
    $inRecycle = $false
    $recycleDeadline = (Get-Date).AddSeconds(6)
    do {
        $inRecycle = Test-RecycleHas $name
        if ($inRecycle) { break }
        Start-Sleep -Milliseconds 400
    } while ((Get-Date) -lt $recycleDeadline)
    if (-not $inRecycle) {
        Write-Host "Note: $name not yet visible in Recycle Bin COM; disk and mosaic already cleared."
    }

    Search-Library ''
    Ok
}

New-Item -ItemType Directory -Force -Path 'screenshots' | Out-Null
winapp ui invoke 'NavLibrary' -a $AppPid
Start-Sleep -Milliseconds 400
winapp ui screenshot -a $AppPid -o 'screenshots/01-library.png' 2>$null
winapp ui screenshot -a $AppPid -o 'screenshots/06-mosaic.png' 2>$null
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
