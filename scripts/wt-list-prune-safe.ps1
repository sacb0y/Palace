# Canonical (hub <-> core): list/prune clean merged worktrees above cap.
# Dry-run by default; -Apply removes only safe candidates.
# Safe = clean + MERGED PR (detached: tip on remote) + git branch -r --contains HEAD non-empty.
param(
  [ValidateSet('momoirosoft', 'momoirosoft-core', 'palace')]
  [string]$Repo = 'momoirosoft',
  [int]$Max,
  [switch]$Apply
)

$parents = @{
  'momoirosoft'      = 'C:\Users\iadag\wkspaces\momoirosoft'
  'momoirosoft-core' = 'C:\Users\iadag\wkspaces\momoirosoft-core'
  'palace'           = 'C:\Users\iadag\source\repos\palace'
}
$caps = @{ 'momoirosoft' = 6; 'momoirosoft-core' = 4; 'palace' = 8 }
if (-not $PSBoundParameters.ContainsKey('Max')) { $Max = $caps[$Repo] }

$parent = $parents[$Repo]
if (-not (Test-Path -LiteralPath $parent)) {
  Write-Error "Parent repo path missing: $parent"
  exit 1
}

# Known main / protected clones (compare with / and \ normalized).
$mainClones = @(
  $parents.Values
  'E:\wkspaces\momoirosoft'
  'E:\wkspaces\momoirosoft-core'
  'E:\wkspaces\Our Apartment'
  'C:\Users\iadag\wkspaces\Our Apartment'
  'C:\Users\iadag\wkspaces\MOMOCore-Dev'
  'C:\Users\iadag\wkspaces\Palace-Dev'
  'E:\wkspaces\Palace-Dev'
) | Select-Object -Unique

function Normalize-PathKey([string]$Path) {
  return (($Path -replace '/', '\').TrimEnd('\')).ToLowerInvariant()
}

function Test-MainClone([string]$Path) {
  $key = Normalize-PathKey $Path
  foreach ($p in $mainClones) {
    if ((Normalize-PathKey $p) -eq $key) { return $true }
  }
  return $false
}

function Test-ProtectedPath([string]$Path) {
  # Porcelain often uses /; match both separators. Never touch E:, mains, or named checkouts.
  if ($Path -match '(?i)^E:[\\/]') { return $true }
  if ($Path -match '(?i)MOMOCore-Dev') { return $true }
  if ($Path -match '(?i)Palace-Dev') { return $true }
  if ($Path -match '(?i)hub-post-merge') { return $true }
  if ($Path -match '(?i)Our Apartment') { return $true }
  if (Test-MainClone $Path) { return $true }
  return $false
}

function Test-UnityHoldsPath([string]$Path) {
  $lock = Join-Path $Path 'Temp/UnityLockfile'
  if (Test-Path -LiteralPath $lock) { return $true }
  $fwd = $Path.Replace('\', '/').TrimEnd('/')
  $bwd = $Path.Replace('/', '\').TrimEnd('\')
  try {
    foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue)) {
      $cl = $p.CommandLine
      if (-not $cl) { continue }
      if ($cl.Contains($fwd) -or $cl.Contains($bwd)) { return $true }
    }
  } catch { }
  return $false
}

$wts = @(
  git -C $parent worktree list --porcelain 2>$null |
    Select-String '^worktree ' |
    ForEach-Object { $_.Line.Substring(9) }
)
if ($LASTEXITCODE -ne 0) {
  Write-Error 'git worktree list failed'
  exit 1
}

$main = $wts | Select-Object -First 1
$extras = @($wts | Select-Object -Skip 1)
Write-Host "$Repo worktrees: $($extras.Count) extras (cap $Max); main=$main"

$candidates = @()
foreach ($wt in $extras) {
  if (-not (Test-Path -LiteralPath $wt)) { continue }
  if ((Normalize-PathKey $wt) -eq (Normalize-PathKey $main)) { continue }
  if (Test-ProtectedPath $wt) { continue }
  if (Test-UnityHoldsPath $wt) { continue }

  $dirty = @(git -C $wt status --porcelain 2>$null)
  if ($LASTEXITCODE -ne 0) { continue }
  if ($dirty.Count -gt 0) { continue }

  $branch = (git -C $wt rev-parse --abbrev-ref HEAD 2>$null)
  if ($LASTEXITCODE -ne 0) { continue }

  # Always require tip on a remote (merged PR alone is not enough for unpushed tips).
  $onRemote = @(git -C $wt branch -r --contains HEAD 2>$null)
  if ($LASTEXITCODE -ne 0) { continue }
  if ($onRemote.Count -eq 0) { continue }

  $detached = ($branch -eq 'HEAD')
  if ($detached) {
    # Detached: only if HEAD already on a remote (onRemote checked above).
    $candidates += [pscustomobject]@{
      Path   = $wt
      Branch = $branch
      Reason = 'clean+detached-on-remote'
    }
    continue
  }

  # Named branch: MERGED only. No PR / OPEN / CLOSED never pruned.
  $prJson = gh pr view $branch --repo "sacb0y/$Repo" --json state 2>$null
  if ($LASTEXITCODE -ne 0 -or -not $prJson) { continue }
  $pr = $prJson | ConvertFrom-Json
  if ($pr.state -ne 'MERGED') { continue }

  $candidates += [pscustomobject]@{
    Path   = $wt
    Branch = $branch
    Reason = 'clean+merged-pr+on-remote'
  }
}

Write-Host "Prune candidates: $($candidates.Count)"
$candidates | Format-Table -AutoSize

if ($extras.Count -le $Max) {
  Write-Host 'Under cap.'
  exit 0
}

$need = $extras.Count - $Max
Write-Host "Over cap by $need"
if (-not $Apply) {
  Write-Host 'Dry-run only. Re-run with -Apply after review.'
  exit 0
}

$toRemove = @($candidates | Select-Object -First $need)
if ($toRemove.Count -lt $need) {
  Write-Warning "Only $($toRemove.Count) safe candidates; cannot reach cap without dirty/unmerged/locked WTs."
}
foreach ($c in $toRemove) {
  Write-Host "Removing $($c.Path) ($($c.Branch))"
  git -C $parent worktree remove --force $c.Path
  if ($LASTEXITCODE -ne 0) {
    Write-Warning "worktree remove failed: $($c.Path)"
    continue
  }
}
git -C $parent worktree prune
Write-Host "Done. Now: $((@(git -C $parent worktree list).Count - 1)) extras"
