<#
.SYNOPSIS
    Reads the fix-list and hands approved findings to an AI agent for implementation.

.DESCRIPTION
    Parses fix-list.md for findings with manager decision "APPROVED", groups them
    into implementation batches, and invokes Claude Code to implement each fix.
    Tracks completion status back into fix-list.md.

.PARAMETER FixList
    Path to the fix-list file. Defaults to tools/reviewer/reports/fix-list.md.

.PARAMETER FindingId
    Implement a specific finding only (e.g., "F001").

.PARAMETER DryRun
    Parse and show what would be implemented without running any agents.

.PARAMETER MaxParallel
    Maximum concurrent implementation agents. Default: 2 (conservative to avoid conflicts).

.EXAMPLE
    # Implement all approved findings
    ./tools/reviewer/apply-fixes.ps1

    # Implement a specific finding
    ./tools/reviewer/apply-fixes.ps1 -FindingId F003

    # Dry run
    ./tools/reviewer/apply-fixes.ps1 -DryRun
#>

param(
    [string]$FixList,
    [string]$FindingId,
    [switch]$DryRun,
    [int]$MaxParallel = 2
)

$ErrorActionPreference = "Stop"
$ReviewerDir = $PSScriptRoot
$ReportsDir = Join-Path $ReviewerDir "reports"

if (-not $FixList) {
    $FixList = Join-Path $ReportsDir "fix-list.md"
}

if (-not (Test-Path $FixList)) {
    Write-Error "Fix list not found at: $FixList. Run run-review.ps1 first."
    return
}

Write-Host "`n=== Fix Implementation Agent ===" -ForegroundColor Cyan

# ------------------------------------------------------------------
# Parse fix-list.md to extract findings
# ------------------------------------------------------------------

$content = Get-Content $FixList -Raw
$findingBlocks = [regex]::Matches($content, '(?m)^## (F\d+)\s*\n([\s\S]*?)(?=^## F\d+|\z)')

$findings = @()
foreach ($match in $findingBlocks) {
    $id = $match.Groups[1].Value
    $body = $match.Groups[2].Value

    # Extract fields
    $file = if ($body -match '\*\*File\*\*:\s*(.+)') { $Matches[1].Trim() } else { "" }
    $severity = if ($body -match '\*\*Severity\*\*:\s*(\w+)') { $Matches[1].Trim() } else { "" }
    $priority = if ($body -match '\*\*Priority\*\*:\s*(\w+)') { $Matches[1].Trim() } else { "" }
    $domain = if ($body -match '\*\*Domain\*\*:\s*(.+)') { $Matches[1].Trim() } else { "" }
    $pattern = if ($body -match '\*\*Pattern\*\*:\s*(.+)') { $Matches[1].Trim() } else { "" }
    $finding = if ($body -match '\*\*Finding\*\*:\s*(.+)') { $Matches[1].Trim() } else { "" }
    $fix = if ($body -match '\*\*Fix\*\*:\s*(.+)') { $Matches[1].Trim() } else { "" }
    $evidence = if ($body -match '\*\*Evidence\*\*:\s*(.+)') { $Matches[1].Trim() } else { "" }
    $decision = if ($body -match '\*\*Manager Decision\*\*:\s*(.+)') { $Matches[1].Trim() } else { "pending" }
    $impl = if ($body -match '\*\*Implementation\*\*:\s*(.+)') { $Matches[1].Trim() } else { "" }

    $findings += [PSCustomObject]@{
        Id        = $id
        File      = $file
        Severity  = $severity
        Priority  = $priority
        Domain    = $domain
        Pattern   = $pattern
        Finding   = $finding
        Fix       = $fix
        Evidence  = $evidence
        Decision  = $decision
        ImplStatus = $impl
        RawBody   = $body
    }
}

Write-Host "Parsed $($findings.Count) findings from fix-list"

# Filter to approved findings
$approved = @($findings | Where-Object {
    $_.Decision -match 'APPROVED|approved|Approved' -and
    $_.ImplStatus -notmatch 'Complete|complete|COMPLETE'
})

if ($FindingId) {
    $approved = @($approved | Where-Object { $_.Id -eq $FindingId })
    if ($approved.Count -eq 0) {
        # Check if finding exists but isn't approved
        $exists = $findings | Where-Object { $_.Id -eq $FindingId }
        if ($exists) {
            Write-Host "Finding $FindingId exists but is not approved (Decision: $($exists.Decision))" -ForegroundColor Yellow
        } else {
            Write-Host "Finding $FindingId not found in fix-list" -ForegroundColor Red
        }
        return
    }
}

Write-Host "Approved findings to implement: $($approved.Count)" -ForegroundColor Green

if ($approved.Count -eq 0) {
    Write-Host "`nNo approved findings to implement." -ForegroundColor Yellow
    Write-Host "To approve findings, edit $FixList and change 'Manager Decision' to 'APPROVED'"
    return
}

# Show what will be implemented
Write-Host "`nFindings to implement:" -ForegroundColor Cyan
foreach ($f in $approved) {
    Write-Host "  $($f.Id) | $($f.Severity) | $($f.File) | $($f.Finding.Substring(0, [Math]::Min(80, $f.Finding.Length)))..."
}

if ($DryRun) {
    Write-Host "`nDRY RUN — no changes will be made" -ForegroundColor Yellow
    return
}

# ------------------------------------------------------------------
# Group findings by file for efficient implementation
# ------------------------------------------------------------------

$byFile = $approved | Group-Object -Property { ($_.File -split ':')[0] }

foreach ($group in $byFile) {
    $filePath = $group.Name
    $fileFindings = $group.Group

    Write-Host "`n--- Implementing fixes for: $filePath ($($fileFindings.Count) findings) ---" -ForegroundColor Cyan

    $findingsList = ($fileFindings | ForEach-Object {
        @"
### $($_.Id): $($_.Finding)
- **Location**: $($_.File)
- **Pattern**: $($_.Pattern)
- **Severity**: $($_.Severity) / $($_.Priority)
- **Evidence**: $($_.Evidence)
- **Required Fix**: $($_.Fix)
"@
    }) -join "`n`n"

    $implPrompt = @"
You are implementing approved code review fixes. A code review identified the following issues
in $filePath. Each has been reviewed and approved by a manager. Implement the fixes.

## Rules
1. Read the file first to understand the full context
2. Make minimal, targeted changes — fix exactly what was flagged
3. Do not refactor surrounding code or add features
4. Do not change code that was not flagged
5. Preserve existing style, formatting, and conventions
6. If a fix would require a larger refactor, note it but make the minimal safe fix

## Findings to Fix

$findingsList

## Instructions
Read the file, implement the fixes, and report what you changed for each finding ID.
"@

    try {
        $result = claude --print --output-format text -p $implPrompt 2>&1
        Write-Host "  Fixes applied for: $filePath" -ForegroundColor Green

        # Update fix-list with completion status
        foreach ($f in $fileFindings) {
            $oldImpl = "**Implementation**: :black_square_button: Not started"
            $newImpl = "**Implementation**: :white_check_mark: Complete ($(Get-Date -Format 'yyyy-MM-dd'))"
            $content = $content -replace [regex]::Escape("## $($f.Id)`n"), "## $($f.Id)`n"
            # Simple status update
            $content = Get-Content $FixList -Raw
            $content = $content -replace "(?m)(## $([regex]::Escape($f.Id))[\s\S]*?)\*\*Implementation\*\*:.*", "`${1}**Implementation**: :white_check_mark: Complete ($(Get-Date -Format 'yyyy-MM-dd'))"
            $content | Set-Content $FixList -Encoding UTF8
        }
    }
    catch {
        Write-Warning "  Failed to implement fixes for $filePath : $_"
    }
}

# ------------------------------------------------------------------
# Summary
# ------------------------------------------------------------------

Write-Host "`n=== Implementation Complete ===" -ForegroundColor Cyan
Write-Host "Fix list updated: $FixList"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Review the changes made by the implementation agent"
Write-Host "  2. Run tests: dotnet test"
Write-Host "  3. Verify findings are resolved"
Write-Host ""
