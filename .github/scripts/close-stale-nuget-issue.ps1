#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Closes any open "Windows NuGet updates ready to open" tracking issue.

.DESCRIPTION
  Companion to .github/workflows/windows-nuget-updates.yml. That workflow opens a
  fallback tracking ISSUE (with a one-click compare link) when GitHub Actions is
  not permitted to create the pull request itself. A maintainer then opens & merges
  the PR from that link — and on merge the bot branch is auto-deleted. Nothing in
  the original design ever closed the tracking issue, so it lingered open with a
  compare link that now shows "no changes" (the branch is gone and the bump is
  already on the default branch).

  This helper finds the single tracking issue by its exact title and closes it,
  leaving an explanatory comment. It is a no-op (and never fails the run) when no
  such issue is open. Uses the `gh` CLI, which reads GH_TOKEN / GH_REPO from the
  environment.

.PARAMETER Reason
  Human-readable reason appended to the auto-close comment.
#>
[CmdletBinding()]
param(
    [string] $Reason = 'the Windows NuGet updates already landed on the default branch, so there is nothing left to open.'
)

$ErrorActionPreference = 'Stop'

$issueTitle = 'Windows NuGet updates ready to open'

try {
    $raw = gh issue list --state open --search 'Windows NuGet updates ready to open in:title' --json number,title
} catch {
    Write-Host "Tracking-issue reconcile skipped (could not list issues): $($_.Exception.Message)"
    return
}

$open = @()
if (-not [string]::IsNullOrWhiteSpace($raw)) { $open = @($raw | ConvertFrom-Json) }

$matches = @($open | Where-Object { $_.title -eq $issueTitle })
if ($matches.Count -eq 0) {
    Write-Host 'No open Windows NuGet tracking issue to reconcile.'
    return
}

foreach ($m in $matches) {
    try {
        Write-Host "Closing stale tracking issue #$($m.number) — $Reason"
        gh issue close $m.number --comment "Closed automatically: $Reason A new tracking issue will be opened if a future bump needs one."
    } catch {
        Write-Host "::warning::Could not close tracking issue #$($m.number): $($_.Exception.Message)"
    }
}
