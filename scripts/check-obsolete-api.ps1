# Regression guard: State API (phase 2) — no misleading obsolete calls in production.
# Usage: pwsh -File scripts/check-obsolete-api.ps1
#        pwsh -File scripts/check-obsolete-api.ps1 -RepoRoot C:\path\to\HarveyStressMeter

param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"
$failed = $false

function Write-CheckHeader([string]$title) {
    Write-Host ""
    Write-Host "=== $title ===" -ForegroundColor Cyan
}

function Fail([string]$message) {
    Write-Host "FAIL: $message" -ForegroundColor Red
    $script:failed = $true
}

function Pass([string]$message) {
    Write-Host "PASS: $message" -ForegroundColor Green
}

# Production = all .cs under repo root, excluding backups and script folder.
$csFiles = Get-ChildItem -Path $RepoRoot -Filter "*.cs" -Recurse -File |
    Where-Object {
        $_.FullName -notmatch '\\obj\\|\\bin\\|\.backup\\|ModEntry\.cs\.backup'
    }

# --- Check 1: obsolete HasActiveBuffInGame / HasQuestInJournal (call sites) ---
Write-CheckHeader "Obsolete API call sites"

$obsoleteCallPatterns = @(
    @{ Name = "HasActiveBuffInGame"; Regex = '\.HasActiveBuffInGame\s*\(' },
    @{ Name = "HasQuestInJournal";   Regex = '\.HasQuestInJournal\s*\(' }
)

# Files that may DEFINE obsolete members (not call them from production logic).
$obsoleteDefinitionAllowFiles = @(
    "Services\StateService.cs",
    "Models\PlayerStressState.cs"
)

foreach ($pat in $obsoleteCallPatterns) {
    $hits = @()
    foreach ($file in $csFiles) {
        $rel = $file.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
        if ($obsoleteDefinitionAllowFiles -contains $rel) { continue }
        $lines = Get-Content -LiteralPath $file.FullName -Encoding UTF8
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match $pat.Regex) {
                $hits += [PSCustomObject]@{
                    File = $rel
                    Line = $i + 1
                    Text = $lines[$i].Trim()
                }
            }
        }
    }

    if ($hits.Count -eq 0) {
        Pass "No production calls to $($pat.Name)"
    }
    else {
        foreach ($h in $hits) {
            Fail "$($pat.Name) at $($h.File):$($h.Line) — $($h.Text)"
        }
    }
}

# PlayerStressState throw-stubs must not be invoked (same patterns); already covered above.

# --- Check 2: direct ActiveTreatments dictionary mutation outside allowlist ---
Write-CheckHeader "ActiveTreatments direct mutation"

$mutationRegex = 'ActiveTreatments\s*(\[[^\]]+\]\s*=|\.Clear\s*\(|\.Add\s*\(|\.Remove\s*\()'

# Baseline: known direct writes until migrated to PlayerStressState.AddTreatment / RemoveTreatment.
# New lines outside this map should fail the check.
# All direct ActiveTreatments writes live in PlayerStressState (fix 25).
$mutationBaseline = @{
    "Models\PlayerStressState.cs" = @(
        261, 283, 306, 320, 329, 331
    )
}

$mutationHits = @()
foreach ($file in $csFiles) {
    $rel = $file.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
    $lines = Get-Content -LiteralPath $file.FullName -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -notmatch $mutationRegex) { continue }
        $lineNum = $i + 1
        $allowed = $false
        if ($mutationBaseline.ContainsKey($rel)) {
            if ($mutationBaseline[$rel] -contains $lineNum) { $allowed = $true }
        }
        if (-not $allowed) {
            $mutationHits += [PSCustomObject]@{
                File = $rel
                Line = $lineNum
                Text = $lines[$i].Trim()
            }
        }
    }
}

if ($mutationHits.Count -eq 0) {
    Pass "No new ActiveTreatments direct mutations outside baseline allowlist"
}
else {
    foreach ($h in $mutationHits) {
        Fail "ActiveTreatments mutation at $($h.File):$($h.Line) — $($h.Text)"
    }
}

# --- Check 3: debug should prefer explicit API names (grep advisory, warn only) ---
Write-CheckHeader "Debug/reporting explicit API (advisory)"

$debugFiles = @(
    "Helpers\HsDebugReporter.cs",
    "Handlers\ConsoleCommandHandler.cs"
)

$misleadingInDebug = @()
foreach ($rel in $debugFiles) {
    $path = Join-Path $RepoRoot $rel
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $lines = Get-Content -LiteralPath $path -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '\.HasActiveBuffInGame\s*\(' -or $lines[$i] -match '\.HasQuestInJournal\s*\(') {
            $misleadingInDebug += "$rel`:$($i + 1)"
        }
    }
}

if ($misleadingInDebug.Count -eq 0) {
    Pass "HsDebugReporter / ConsoleCommandHandler do not use obsolete API names"
}
else {
    Write-Host "WARN: obsolete API in debug files: $($misleadingInDebug -join ', ')" -ForegroundColor Yellow
}

Write-Host ""
if ($failed) {
    Write-Host "Regression check FAILED." -ForegroundColor Red
    exit 1
}

Write-Host "Regression check PASSED." -ForegroundColor Green
exit 0
