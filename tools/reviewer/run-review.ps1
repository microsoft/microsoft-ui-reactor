<#
.SYNOPSIS
    Parallelized code review orchestrator using Claude Code CLI agents.

.DESCRIPTION
    Runs specialist review agents in parallel across file batches, then runs
    a general-purpose sweep, and finally consolidates all findings into a
    fix-list with manager approve/decline workflow.

.PARAMETER Agent
    Run only a specific agent (safety, lifecycle, interop, security, test-quality, general).
    If omitted, runs all agents.

.PARAMETER Batch
    Run only a specific batch ID (e.g., "safety-batch-1").
    If omitted, runs all batches for the selected agent(s).

.PARAMETER ConsolidateOnly
    Skip agent runs entirely. Just re-merge existing reports into fix-list.

.PARAMETER MaxParallel
    Maximum concurrent agent invocations. Default: 4.

.PARAMETER DryRun
    Show what would be run without executing any agents.

.EXAMPLE
    # Full review — all agents, all batches
    ./tools/reviewer/run-review.ps1

    # Single agent
    ./tools/reviewer/run-review.ps1 -Agent safety

    # Single batch
    ./tools/reviewer/run-review.ps1 -Batch safety-batch-1

    # Just consolidate existing reports
    ./tools/reviewer/run-review.ps1 -ConsolidateOnly

    # Dry run to see what would happen
    ./tools/reviewer/run-review.ps1 -DryRun
#>

param(
    [ValidateSet("safety", "lifecycle", "interop", "security", "test-quality", "general")]
    [string]$Agent,

    [string]$Batch,

    [switch]$ConsolidateOnly,

    [int]$MaxParallel = 4,

    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ReviewerDir = $PSScriptRoot
$ReportsDir = Join-Path $ReviewerDir "reports"
$ManifestPath = Join-Path $ReviewerDir "manifest.json"
$PromptsDir = Join-Path $ReviewerDir "prompts"
$FixListPath = Join-Path $ReportsDir "fix-list.md"

# Ensure reports directory exists
if (-not (Test-Path $ReportsDir)) {
    New-Item -ItemType Directory -Path $ReportsDir -Force | Out-Null
}

# ------------------------------------------------------------------
# Phase 0: Load manifest
# ------------------------------------------------------------------

Write-Host "`n=== Code Review Orchestrator ===" -ForegroundColor Cyan
Write-Host "Repo root: $RepoRoot"
Write-Host "Reviewer dir: $ReviewerDir"

$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json

$batches = $manifest.batches
Write-Host "Manifest loaded: $($batches.Count) batches across $($manifest.agents.Count) agents"

# Filter batches
if ($Batch) {
    $batches = @($batches | Where-Object { $_.id -eq $Batch })
    if ($batches.Count -eq 0) {
        Write-Error "Batch '$Batch' not found in manifest"
        return
    }
    Write-Host "Filtered to batch: $Batch"
}
elseif ($Agent) {
    $batches = @($batches | Where-Object { $_.agent -eq $Agent })
    Write-Host "Filtered to agent '$Agent': $($batches.Count) batches"
}

if ($ConsolidateOnly) {
    Write-Host "`nSkipping agent runs — consolidation only" -ForegroundColor Yellow
    $batches = @()
}

# ------------------------------------------------------------------
# Phase 1: Run specialist agents (safety, lifecycle, interop, security, test-quality)
# ------------------------------------------------------------------

$specialistBatches = @($batches | Where-Object { $_.agent -ne "general" })
$generalBatches = @($batches | Where-Object { $_.agent -eq "general" })

function Build-AgentPrompt {
    param(
        [object]$BatchObj
    )
    $agentName = $BatchObj.agent
    $promptTemplate = Get-Content (Join-Path $PromptsDir "$agentName.md") -Raw

    # Build file list for this batch
    $fileList = ($BatchObj.files | ForEach-Object { "- $_" }) -join "`n"

    $fullPrompt = @"
$promptTemplate

---

## YOUR BATCH: $($BatchObj.id)

**Description**: $($BatchObj.description)

**Primary files to review:**
$fileList

Review each file thoroughly. For each file, read it completely before analyzing.
You may read other files in the repo for context (e.g., to understand types, base classes, callers).

Output your findings in the exact format specified above. If you find no issues in a file, note it briefly.

Begin your review now.
"@

    return $fullPrompt
}

function Invoke-ReviewAgent {
    param(
        [object]$BatchObj
    )
    $agentName = $BatchObj.agent
    $batchId = $BatchObj.id
    $reportPath = Join-Path $ReportsDir "$batchId.md"

    $prompt = Build-AgentPrompt -BatchObj $BatchObj

    Write-Host "  [$batchId] Starting ($($BatchObj.files.Count) files)..." -ForegroundColor Gray

    if ($DryRun) {
        Write-Host "  [$batchId] DRY RUN — would invoke claude --print" -ForegroundColor Yellow
        # Write a placeholder
        @"
# Review Report: $batchId
## Agent: $agentName
## Status: DRY RUN — no findings generated
## Files: $($BatchObj.files -join ', ')
"@ | Set-Content $reportPath
        return
    }

    try {
        # Invoke Claude Code CLI in print mode
        $result = claude --print --output-format text -p $prompt 2>&1
        $exitCode = $LASTEXITCODE

        if ($exitCode -ne 0) {
            Write-Warning "  [$batchId] claude exited with code $exitCode"
        }

        # Write report
        @"
# Review Report: $batchId

**Agent**: $agentName
**Description**: $($BatchObj.description)
**Files reviewed**: $($BatchObj.files.Count)
**Generated**: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

---

$result
"@ | Set-Content $reportPath -Encoding UTF8

        Write-Host "  [$batchId] Complete -> $reportPath" -ForegroundColor Green
    }
    catch {
        Write-Warning "  [$batchId] FAILED: $_"
        @"
# Review Report: $batchId

**Agent**: $agentName
**Status**: FAILED
**Error**: $_
"@ | Set-Content $reportPath -Encoding UTF8
    }
}

# Run specialist batches in parallel
if ($specialistBatches.Count -gt 0) {
    Write-Host "`n--- Phase 1: Specialist Reviews ($($specialistBatches.Count) batches, max $MaxParallel parallel) ---" -ForegroundColor Cyan

    $specialistBatches | ForEach-Object -ThrottleLimit $MaxParallel -Parallel {
        # Re-import functions in parallel scope
        $ReviewerDir = $using:ReviewerDir
        $ReportsDir = $using:ReportsDir
        $PromptsDir = $using:PromptsDir
        $DryRun = $using:DryRun
        $BatchObj = $_
        $agentName = $BatchObj.agent
        $batchId = $BatchObj.id
        $reportPath = Join-Path $ReportsDir "$batchId.md"

        $promptTemplate = Get-Content (Join-Path $PromptsDir "$agentName.md") -Raw
        $fileList = ($BatchObj.files | ForEach-Object { "- $_" }) -join "`n"

        $fullPrompt = @"
$promptTemplate

---

## YOUR BATCH: $batchId

**Description**: $($BatchObj.description)

**Primary files to review:**
$fileList

Review each file thoroughly. For each file, read it completely before analyzing.
You may read other files in the repo for context.

Output your findings in the exact format specified above. If you find no issues in a file, note it briefly.

Begin your review now.
"@

        Write-Host "  [$batchId] Starting ($($BatchObj.files.Count) files)..." -ForegroundColor Gray

        if ($DryRun) {
            Write-Host "  [$batchId] DRY RUN" -ForegroundColor Yellow
            "# Review Report: $batchId`n## Agent: $agentName`n## Status: DRY RUN" | Set-Content $reportPath
        }
        else {
            try {
                # Write prompt to temp file and use Start-Process with I/O redirection
                # to avoid StandardOutputEncoding errors in parallel runspaces
                $tempPrompt = Join-Path $env:TEMP "claude-prompt-$batchId.txt"
                $tempOutput = Join-Path $env:TEMP "claude-output-$batchId.txt"
                $tempError = Join-Path $env:TEMP "claude-error-$batchId.txt"
                $fullPrompt | Set-Content $tempPrompt -Encoding UTF8

                $proc = Start-Process -FilePath "claude" `
                    -ArgumentList "--print","--output-format","text" `
                    -RedirectStandardInput $tempPrompt `
                    -RedirectStandardOutput $tempOutput `
                    -RedirectStandardError $tempError `
                    -NoNewWindow -Wait -PassThru

                $result = ""
                if (Test-Path $tempOutput) { $result = Get-Content $tempOutput -Raw }

                Remove-Item $tempPrompt, $tempOutput, $tempError -Force -ErrorAction SilentlyContinue

                if ($proc.ExitCode -ne 0) {
                    Write-Warning "  [$batchId] claude exited with code $($proc.ExitCode)"
                }

                @"
# Review Report: $batchId

**Agent**: $agentName
**Description**: $($BatchObj.description)
**Files reviewed**: $($BatchObj.files.Count)
**Generated**: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

---

$result
"@ | Set-Content $reportPath -Encoding UTF8

                Write-Host "  [$batchId] Complete" -ForegroundColor Green
            }
            catch {
                Write-Warning "  [$batchId] FAILED: $_"
                "# Review Report: $batchId`n**Status**: FAILED`n**Error**: $_" | Set-Content $reportPath
            }
        }
    }
}

# ------------------------------------------------------------------
# Phase 2: Run general agent (has access to specialist findings)
# ------------------------------------------------------------------

if ($generalBatches.Count -gt 0) {
    Write-Host "`n--- Phase 2: General Review ($($generalBatches.Count) batches) ---" -ForegroundColor Cyan

    # Collect specialist findings for context
    $specialistFindings = ""
    $specialistReports = Get-ChildItem $ReportsDir -Filter "*.md" | Where-Object { $_.Name -notmatch "^general-" -and $_.Name -ne "fix-list.md" }
    if ($specialistReports) {
        $specialistFindings = "`n`n---`n## SPECIALIST FINDINGS (do NOT re-report these)`n`n"
        foreach ($report in $specialistReports) {
            $specialistFindings += "### From: $($report.BaseName)`n"
            $specialistFindings += (Get-Content $report.FullName -Raw) + "`n`n"
        }
    }

    $generalBatches | ForEach-Object -ThrottleLimit $MaxParallel -Parallel {
        $ReviewerDir = $using:ReviewerDir
        $ReportsDir = $using:ReportsDir
        $PromptsDir = $using:PromptsDir
        $DryRun = $using:DryRun
        $specialistFindings = $using:specialistFindings
        $BatchObj = $_
        $batchId = $BatchObj.id
        $reportPath = Join-Path $ReportsDir "$batchId.md"

        $promptTemplate = Get-Content (Join-Path $PromptsDir "general.md") -Raw
        $fileList = ($BatchObj.files | ForEach-Object { "- $_" }) -join "`n"

        $fullPrompt = @"
$promptTemplate

$specialistFindings

---

## YOUR BATCH: $batchId

**Description**: $($BatchObj.description)

**Primary files to review:**
$fileList

Review each file. Focus on what the specialists missed. Do NOT re-report their findings.

Begin your review now.
"@

        Write-Host "  [$batchId] Starting ($($BatchObj.files.Count) files)..." -ForegroundColor Gray

        if ($DryRun) {
            Write-Host "  [$batchId] DRY RUN" -ForegroundColor Yellow
            "# Review Report: $batchId`n## Agent: general`n## Status: DRY RUN" | Set-Content $reportPath
        }
        else {
            try {
                # Write prompt to temp file and use Start-Process with I/O redirection
                # to avoid StandardOutputEncoding errors in parallel runspaces
                $tempPrompt = Join-Path $env:TEMP "claude-prompt-$batchId.txt"
                $tempOutput = Join-Path $env:TEMP "claude-output-$batchId.txt"
                $tempError = Join-Path $env:TEMP "claude-error-$batchId.txt"
                $fullPrompt | Set-Content $tempPrompt -Encoding UTF8

                $proc = Start-Process -FilePath "claude" `
                    -ArgumentList "--print","--output-format","text" `
                    -RedirectStandardInput $tempPrompt `
                    -RedirectStandardOutput $tempOutput `
                    -RedirectStandardError $tempError `
                    -NoNewWindow -Wait -PassThru

                $result = ""
                if (Test-Path $tempOutput) { $result = Get-Content $tempOutput -Raw }

                Remove-Item $tempPrompt, $tempOutput, $tempError -Force -ErrorAction SilentlyContinue

                if ($proc.ExitCode -ne 0) {
                    Write-Warning "  [$batchId] claude exited with code $($proc.ExitCode)"
                }

                @"
# Review Report: $batchId

**Agent**: general
**Description**: $($BatchObj.description)
**Files reviewed**: $($BatchObj.files.Count)
**Generated**: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

---

$result
"@ | Set-Content $reportPath -Encoding UTF8

                Write-Host "  [$batchId] Complete" -ForegroundColor Green
            }
            catch {
                Write-Warning "  [$batchId] FAILED: $_"
                "# Review Report: $batchId`n**Status**: FAILED`n**Error**: $_" | Set-Content $reportPath
            }
        }
    }
}

# ------------------------------------------------------------------
# Phase 3: Consolidate findings into fix-list
# ------------------------------------------------------------------

Write-Host "`n--- Phase 3: Consolidation ---" -ForegroundColor Cyan

$allReports = Get-ChildItem $ReportsDir -Filter "*.md" | Where-Object { $_.Name -ne "fix-list.md" }

if ($allReports.Count -eq 0) {
    Write-Host "No reports found to consolidate." -ForegroundColor Yellow
    return
}

Write-Host "Consolidating $($allReports.Count) reports..."

# Build consolidation prompt
$reportContents = ""
foreach ($report in ($allReports | Sort-Object Name)) {
    $reportContents += "`n`n---`n### Report: $($report.BaseName)`n`n"
    $reportContents += Get-Content $report.FullName -Raw
}

$consolidationPrompt = @"
You are consolidating code review findings from multiple specialist agents into a single fix-list.

## Instructions

1. Read all the report findings below
2. Deduplicate: if multiple agents flagged the same issue on the same file/lines, keep the highest-severity version
3. Sort findings by: severity (critical first), then priority (P0 first), then file path
4. Assign each finding a unique ID: F001, F002, etc.
5. Output in the EXACT format specified below

## Output Format

Start with a summary header, then list every finding in this format:

```
# Code Review Fix List

**Generated**: [date]
**Total Findings**: [count]
**By Severity**: Critical: [n], High: [n], Medium: [n], Low: [n]
**Reports Consolidated**: [count]

---

## F001
- **File**: [path]:[line_range]
- **Severity**: [critical|high|medium|low]
- **Priority**: [P0|P1|P2|P3]
- **Domain**: [domain]
- **Pattern**: [pattern ID]
- **Agent**: [which agent found this]
- **Status**: :black_square_button: PENDING
- **Finding**: [description]
- **Evidence**: [specific code evidence]
- **Fix**: [actionable fix description]
- **Manager Decision**: _pending_
- **Implementation**: :black_square_button: Not started
```

IMPORTANT:
- Every finding MUST have the Status and Manager Decision fields — these are for the manager workflow
- Implementation checkbox is for tracking when fixes are applied
- If a finding appears in multiple reports, note "Also flagged by: [agent]" at the end
- Keep the file path and line references precise

## Reports

$reportContents
"@

if ($DryRun) {
    Write-Host "DRY RUN — would invoke consolidation agent" -ForegroundColor Yellow
    @"
# Code Review Fix List

**Generated**: $(Get-Date -Format "yyyy-MM-dd")
**Status**: DRY RUN — no findings consolidated

This file will be populated when the review agents are run without -DryRun.
"@ | Set-Content $FixListPath -Encoding UTF8
}
else {
    Write-Host "Running consolidation agent..."
    try {
        # Write prompt to temp file and use Start-Process with I/O redirection
        # to avoid StandardOutputEncoding errors
        $tempPrompt = Join-Path $env:TEMP "claude-consolidation-prompt.txt"
        $tempOutput = Join-Path $env:TEMP "claude-consolidation-output.txt"
        $tempError = Join-Path $env:TEMP "claude-consolidation-error.txt"
        $consolidationPrompt | Set-Content $tempPrompt -Encoding UTF8

        $proc = Start-Process -FilePath "claude" `
            -ArgumentList "--print","--output-format","text" `
            -RedirectStandardInput $tempPrompt `
            -RedirectStandardOutput $tempOutput `
            -RedirectStandardError $tempError `
            -NoNewWindow -Wait -PassThru

        $fixListContent = ""
        if (Test-Path $tempOutput) { $fixListContent = Get-Content $tempOutput -Raw }

        Remove-Item $tempPrompt, $tempOutput, $tempError -Force -ErrorAction SilentlyContinue

        if ($proc.ExitCode -ne 0) {
            Write-Warning "Consolidation claude exited with code $($proc.ExitCode)"
        }

        $fixListContent | Set-Content $FixListPath -Encoding UTF8
        Write-Host "Fix list written to: $FixListPath" -ForegroundColor Green
    }
    catch {
        Write-Warning "Consolidation failed: $_"
        Write-Warning "Individual reports are still available in $ReportsDir"
    }
}

# ------------------------------------------------------------------
# Summary
# ------------------------------------------------------------------

Write-Host "`n=== Review Complete ===" -ForegroundColor Cyan
Write-Host "Reports dir: $ReportsDir"
Write-Host "Reports generated: $($allReports.Count)"
if (Test-Path $FixListPath) {
    Write-Host "Fix list: $FixListPath" -ForegroundColor Green
}
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Review individual reports in $ReportsDir"
Write-Host "  2. Review consolidated fix-list: $FixListPath"
Write-Host "  3. Manager: approve/decline findings in fix-list.md"
Write-Host "  4. Run: ./tools/reviewer/apply-fixes.ps1 to hand approved fixes to AI"
Write-Host ""
