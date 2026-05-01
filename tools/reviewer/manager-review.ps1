<#
.SYNOPSIS
    Interactive manager review workflow for the fix-list.

.DESCRIPTION
    Displays each pending finding from fix-list.md and prompts the manager
    to approve, decline, or defer each one. Updates fix-list.md in place.

.PARAMETER FixList
    Path to the fix-list file. Defaults to tools/reviewer/reports/fix-list.md.

.PARAMETER SeverityFilter
    Only show findings of this severity or higher.
    Values: critical, high, medium, low (default: low = show all)

.EXAMPLE
    # Review all pending findings
    ./tools/reviewer/manager-review.ps1

    # Review only critical and high severity
    ./tools/reviewer/manager-review.ps1 -SeverityFilter high
#>

param(
    [string]$FixList,
    [ValidateSet("critical", "high", "medium", "low")]
    [string]$SeverityFilter = "low"
)

$ReviewerDir = $PSScriptRoot
$ReportsDir = Join-Path $ReviewerDir "reports"

if (-not $FixList) {
    $FixList = Join-Path $ReportsDir "fix-list.md"
}

if (-not (Test-Path $FixList)) {
    Write-Error "Fix list not found. Run run-review.ps1 first."
    return
}

$severityOrder = @{ "critical" = 0; "high" = 1; "medium" = 2; "low" = 3 }
$minSeverity = $severityOrder[$SeverityFilter]

# Parse findings
$content = Get-Content $FixList -Raw
$findingBlocks = [regex]::Matches($content, '(?m)^## (F\d+)\s*\n([\s\S]*?)(?=^## F\d+|\z)')

$pending = @()
foreach ($match in $findingBlocks) {
    $id = $match.Groups[1].Value
    $body = $match.Groups[2].Value

    $decision = if ($body -match '\*\*Manager Decision\*\*:\s*(.+)') { $Matches[1].Trim() } else { "" }
    $severity = if ($body -match '\*\*Severity\*\*:\s*(\w+)') { $Matches[1].Trim().ToLower() } else { "low" }

    # Filter by severity
    $sevLevel = $severityOrder[$severity]
    if ($null -eq $sevLevel) { $sevLevel = 3 }

    if ($decision -match 'pending|_pending_' -and $sevLevel -le $minSeverity) {
        $file = if ($body -match '\*\*File\*\*:\s*(.+)') { $Matches[1].Trim() } else { "unknown" }
        $finding = if ($body -match '\*\*Finding\*\*:\s*(.+)') { $Matches[1].Trim() } else { "" }
        $fix = if ($body -match '\*\*Fix\*\*:\s*(.+)') { $Matches[1].Trim() } else { "" }
        $evidence = if ($body -match '\*\*Evidence\*\*:\s*(.+)') { $Matches[1].Trim() } else { "" }
        $priority = if ($body -match '\*\*Priority\*\*:\s*(\w+)') { $Matches[1].Trim() } else { "" }
        $pattern = if ($body -match '\*\*Pattern\*\*:\s*(.+)') { $Matches[1].Trim() } else { "" }

        $pending += [PSCustomObject]@{
            Id       = $id
            Severity = $severity
            Priority = $priority
            File     = $file
            Finding  = $finding
            Fix      = $fix
            Evidence = $evidence
            Pattern  = $pattern
        }
    }
}

Write-Host "`n=== Manager Review ===" -ForegroundColor Cyan
Write-Host "Fix list: $FixList"
Write-Host "Pending findings (severity >= $SeverityFilter): $($pending.Count)"
Write-Host ""

if ($pending.Count -eq 0) {
    Write-Host "No pending findings to review." -ForegroundColor Green
    return
}

$approved = 0
$declined = 0
$deferred = 0

foreach ($f in $pending) {
    $sevColor = switch ($f.Severity) {
        "critical" { "Red" }
        "high"     { "Yellow" }
        "medium"   { "Cyan" }
        default    { "Gray" }
    }

    Write-Host "---" -ForegroundColor DarkGray
    Write-Host "[$($f.Id)] " -NoNewline -ForegroundColor White
    Write-Host "$($f.Severity.ToUpper())" -NoNewline -ForegroundColor $sevColor
    Write-Host " / $($f.Priority)" -ForegroundColor Gray
    Write-Host "File: $($f.File)" -ForegroundColor Gray
    Write-Host "Pattern: $($f.Pattern)" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "Finding: $($f.Finding)" -ForegroundColor White
    Write-Host "Evidence: $($f.Evidence)" -ForegroundColor DarkGray
    Write-Host "Fix: $($f.Fix)" -ForegroundColor Green
    Write-Host ""

    $choice = Read-Host "  [A]pprove  [D]ecline  [S]kip  [Q]uit"

    switch ($choice.ToUpper()) {
        "A" {
            $notes = Read-Host "  Notes (optional, press Enter to skip)"
            $newDecision = "APPROVED"
            if ($notes) { $newDecision += " — $notes" }
            $content = $content -replace "(?m)(## $([regex]::Escape($f.Id))[\s\S]*?)\*\*Manager Decision\*\*:.*", "`${1}**Manager Decision**: $newDecision"
            $approved++
            Write-Host "  -> Approved" -ForegroundColor Green
        }
        "D" {
            $reason = Read-Host "  Reason for declining"
            $newDecision = "DECLINED"
            if ($reason) { $newDecision += " — $reason" }
            $content = $content -replace "(?m)(## $([regex]::Escape($f.Id))[\s\S]*?)\*\*Manager Decision\*\*:.*", "`${1}**Manager Decision**: $newDecision"
            $declined++
            Write-Host "  -> Declined" -ForegroundColor Red
        }
        "S" {
            $deferred++
            Write-Host "  -> Skipped (still pending)" -ForegroundColor Yellow
        }
        "Q" {
            Write-Host "`nSaving and exiting..." -ForegroundColor Yellow
            break
        }
        default {
            $deferred++
            Write-Host "  -> Skipped" -ForegroundColor Yellow
        }
    }
}

# Save updates
$content | Set-Content $FixList -Encoding UTF8

Write-Host "`n=== Review Summary ===" -ForegroundColor Cyan
Write-Host "Approved: $approved" -ForegroundColor Green
Write-Host "Declined: $declined" -ForegroundColor Red
Write-Host "Deferred: $deferred" -ForegroundColor Yellow
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Run ./tools/reviewer/apply-fixes.ps1 to implement approved fixes"
Write-Host "  2. Run ./tools/reviewer/manager-review.ps1 again to review remaining findings"
Write-Host ""
