<#
.SYNOPSIS
    Pure, dot-source-able helpers for the automatic merged-coverage PR comment
    (.github/workflows/coverage.yml + coverage-comment.yml).

.DESCRIPTION
    These functions have no side effects beyond Write-Warning, so they can be
    unit-tested locally (CoverageLib.Tests.ps1) without building or instrumenting
    anything:

      Get-CoberturaRatesFromXml   sum a merged cobertura document into line %,
                                  branch %, and the branch covered/total counts.
      Get-CoberturaRates          same, from a file path.
      ConvertTo-SafePercent       gate an artifact-supplied percentage (0..100).
      ConvertTo-SafeCount         gate an artifact-supplied non-negative integer.
      ConvertTo-SafeCoverageMetrics  project raw (untrusted) numbers onto a
                                  validated metrics object — the render-time
                                  security boundary for the privileged poster.
      Test-CoverageMetricsPresent whether a metrics object carries a real line %.
      Format-Percent              humanize a percentage ('87.30%' / 'n/a').
      Format-BranchCell           branch % plus its (covered/total) counts.
      Format-SignedPoints         signed percentage-point delta ('+1.20 pp').
      Get-CoverageDelta           base-vs-head delta, direction-aware, with a
                                  small noise band (up / down / unchanged / na).
      Get-CoverageStatusGlyph     emoji/glyph for a delta status.
      Format-CoverageDeltaCell    the 'Δ' table cell.
      Format-CoverageComment      the sticky base-vs-PR coverage markdown comment.

    A "coverage metrics" object is a [pscustomobject] with:
      Line             merged line coverage, percent (0..100), or $null.
      Branch           merged branch coverage, percent (0..100), or $null.
      BranchesCovered  covered conditional branches, or $null.
      BranchesTotal    total conditional branches, or $null.

    HIGHER coverage is the "good" direction, so a rise is flagged as the
    improvement (up) and a fall as the regression (down). The comment is
    informational only — it never fails a PR.
#>

Set-StrictMode -Version Latest

# Hidden marker used to find + update-in-place the sticky PR comment. Matches the
# marker the original inline poster used, so it keeps updating the SAME comment
# rather than orphaning it and posting a duplicate. Mirrors the convention in
# tests/build_metrics/ci/BuildMetricsLib.ps1 ('<!-- reactor-build-metrics -->').
$script:CoverageCommentMarker = '<!-- reactor-coverage -->'

# Default significance band, in percentage points. Merged coverage is largely
# deterministic, but selftest branch coverage can wiggle by a hair across
# otherwise-identical runs (timing/ordering-dependent branches). A delta counts as
# a real move only when it clears this floor, so sub-noise jitter doesn't render as
# a spurious ✅/⚠️.
$script:CoverageNoiseFloorPoints = 0.1

function Get-CoverageCommentMarker {
    <#
    .SYNOPSIS
        The hidden HTML marker used to find + update the sticky coverage comment.
    #>
    return $script:CoverageCommentMarker
}

function Get-CoberturaRatesFromXml {
    <#
    .SYNOPSIS
        Aggregate a merged cobertura document into line % + branch % (+ the branch
        covered/total counts).
    .DESCRIPTION
        dotnet-coverage's cobertura output records per-line branch data in
        `condition-coverage="P% (covered/total)"` attributes but does NOT aggregate
        them: the root/class `branch-rate` attributes are hard-coded to 1. So the
        line rate is read from the root `line-rate` (0..1, scaled to a percent) and
        the branch rate is summed from the per-line numerators/denominators here.
        Returns a metrics object; Branch/counts are $null when the document has no
        conditional branches at all.
    #>
    param([Parameter(Mandatory)][xml]$Doc)

    $lineRateAttr = $null
    if ($Doc.DocumentElement -and $Doc.DocumentElement.HasAttribute('line-rate')) {
        $lineRateAttr = $Doc.DocumentElement.GetAttribute('line-rate')
    }
    $line = $null
    if ($null -ne $lineRateAttr -and $lineRateAttr -ne '') {
        $parsed = [double]0
        if ([double]::TryParse([string]$lineRateAttr,
                [System.Globalization.NumberStyles]::Float,
                [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
            $line = [math]::Round($parsed * 100, 2)
        }
    }

    $covered = 0
    $total = 0
    foreach ($l in $Doc.SelectNodes('//line[@condition-coverage]')) {
        $cc = [string]$l.GetAttribute('condition-coverage')
        if ($cc -match '\((\d+)/(\d+)\)') {
            $covered += [int]$Matches[1]
            $total += [int]$Matches[2]
        }
    }

    $branch = $null
    $bc = $null
    $bt = $null
    if ($total -gt 0) {
        $branch = [math]::Round(100.0 * $covered / $total, 2)
        $bc = [long]$covered
        $bt = [long]$total
    }

    return [pscustomobject]@{
        Line            = $line
        Branch          = $branch
        BranchesCovered = $bc
        BranchesTotal   = $bt
    }
}

function Get-CoberturaRates {
    <#
    .SYNOPSIS
        Get-CoberturaRatesFromXml from a merged cobertura file on disk. Returns an
        all-null metrics object when the file is missing or not valid XML.
    #>
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Warning "Cobertura report not found: $Path"
        return [pscustomobject]@{ Line = $null; Branch = $null; BranchesCovered = $null; BranchesTotal = $null }
    }
    try {
        [xml]$doc = Get-Content -LiteralPath $Path -Raw
    } catch {
        Write-Warning "Failed to parse cobertura ${Path}: $($_.Exception.Message)"
        return [pscustomobject]@{ Line = $null; Branch = $null; BranchesCovered = $null; BranchesTotal = $null }
    }
    return Get-CoberturaRatesFromXml -Doc $doc
}

function ConvertTo-SafePercent {
    <#
    .SYNOPSIS
        Return $Value as a [double] percentage in [0, 100], or $null when it is
        missing, out of range, or not a plain non-negative decimal (rejects a sign,
        exponent, pipe, or any other markdown/injection character). The sole gate
        for artifact-supplied coverage percentages.
    #>
    param([AllowNull()]$Value)
    if ($null -eq $Value) { return $null }
    $s = ([string]$Value).Trim()
    if ($s -notmatch '^\d+(\.\d+)?$') { return $null }
    $parsed = [double]0
    if (-not [double]::TryParse($s, [System.Globalization.NumberStyles]::AllowDecimalPoint,
            [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
        return $null
    }
    if ($parsed -lt 0 -or $parsed -gt 100) { return $null }
    return $parsed
}

function ConvertTo-SafeCount {
    <#
    .SYNOPSIS
        Return $Value as a non-negative [long], or $null when it is missing,
        negative, or not a plain integer. The sole gate for artifact-supplied
        branch covered/total counts.
    #>
    param([AllowNull()]$Value)
    if ($null -eq $Value) { return $null }
    $parsed = [long]0
    if ([long]::TryParse([string]$Value, [ref]$parsed) -and $parsed -ge 0) {
        return $parsed
    }
    return $null
}

function ConvertTo-SafeCoverageMetrics {
    <#
    .SYNOPSIS
        Project a raw (untrusted) coverage record — e.g. parsed from a coverage.json
        an unprivileged PR job produced — onto a validated metrics object.
    .DESCRIPTION
        Security boundary for the privileged poster: every field is re-validated
        here (percentages via ConvertTo-SafePercent, counts via ConvertTo-SafeCount),
        so nothing artifact-derived reaches the rendered markdown except a handful
        of plain, range-checked numbers. Unknown/extra properties on the input are
        ignored. A $null input yields an all-null metrics object.
    #>
    param([AllowNull()]$Raw)

    function Get-RawProp($obj, [string]$name) {
        if ($null -eq $obj) { return $null }
        if ($obj.PSObject.Properties[$name]) { return $obj.$name }
        return $null
    }

    return [pscustomobject]@{
        Line            = ConvertTo-SafePercent (Get-RawProp $Raw 'line')
        Branch          = ConvertTo-SafePercent (Get-RawProp $Raw 'branch')
        BranchesCovered = ConvertTo-SafeCount   (Get-RawProp $Raw 'branchesCovered')
        BranchesTotal   = ConvertTo-SafeCount   (Get-RawProp $Raw 'branchesTotal')
    }
}

function Test-CoverageMetricsPresent {
    <#
    .SYNOPSIS
        $true when the metrics object carries a real (non-null) line coverage — the
        signal that a side actually produced numbers.
    #>
    param([AllowNull()]$Metrics)
    return ($null -ne $Metrics -and $null -ne $Metrics.Line)
}

function Format-Percent {
    <#
    .SYNOPSIS
        Humanize a percentage as 'NN.NN%', or 'n/a' when $null.
    #>
    param([AllowNull()]$Pct)
    if ($null -eq $Pct) { return 'n/a' }
    $r = [math]::Round([double]$Pct, 2)
    return ('{0:0.00}%' -f $r)
}

function Format-BranchCell {
    <#
    .SYNOPSIS
        Branch percentage plus its (covered/total) counts, e.g. '74.50% (500/671)'.
        Falls back to just the percent when counts are absent, or 'n/a' when the
        percent itself is null.
    #>
    param([AllowNull()]$Pct, [AllowNull()]$Covered, [AllowNull()]$Total)
    if ($null -eq $Pct) { return 'n/a' }
    $p = Format-Percent $Pct
    if ($null -ne $Covered -and $null -ne $Total) {
        return "$p ($Covered/$Total)"
    }
    return $p
}

function Format-SignedPoints {
    <#
    .SYNOPSIS
        Format a percentage-point delta with an explicit sign, e.g. '+1.20 pp' /
        '-0.30 pp' / '0.00 pp'. 'n/a' when $null.
    #>
    param([AllowNull()]$Points)
    if ($null -eq $Points) { return 'n/a' }
    $r = [math]::Round([double]$Points, 2)
    return (('{0:+0.00;-0.00;0.00}' -f $r) + ' pp')
}

function Get-CoverageDelta {
    <#
    .SYNOPSIS
        Base-vs-head coverage delta with a small noise band. Status is one of:
        up | down | unchanged | na.
    .DESCRIPTION
        - na       : either side absent (no baseline / no measurement).
        - unchanged: present on both, |Δ| below the noise floor (percentage points).
        - up/down  : present on both, |Δ| clears the floor.
        HIGHER coverage is the improvement, so Improved is $true only for 'up'.
    #>
    param(
        [AllowNull()]$BasePct,
        [AllowNull()]$HeadPct,
        [double]$NoiseFloorPoints = $script:CoverageNoiseFloorPoints
    )
    if ($null -eq $BasePct -or $null -eq $HeadPct) {
        return [pscustomobject]@{ Status = 'na'; DeltaPoints = $null; Improved = $null }
    }

    $delta = [math]::Round([double]$HeadPct - [double]$BasePct, 2)

    if ([math]::Abs($delta) -lt $NoiseFloorPoints) {
        return [pscustomobject]@{ Status = 'unchanged'; DeltaPoints = $delta; Improved = $null }
    }

    $status = if ($delta -gt 0) { 'up' } else { 'down' }
    return [pscustomobject]@{ Status = $status; DeltaPoints = $delta; Improved = ($delta -gt 0) }
}

function Get-CoverageStatusGlyph {
    <#
    .SYNOPSIS
        Short status glyph for a delta status (a rise is the improvement).
    #>
    param([string]$Status)
    switch ($Status) {
        'up'        { return [char]0x2705 }                 # check mark
        'down'      { return [char]0x26A0 + [char]0xFE0F }  # warning sign
        'unchanged' { return [char]0x2248 }                 # almost-equal-to
        default     { return [char]0x2014 }                 # em dash
    }
}

function Format-CoverageDeltaCell {
    <#
    .SYNOPSIS
        Render the 'Δ' cell: a signed percentage-point delta, or an em dash when no
        baseline is available.
    #>
    param([pscustomobject]$Delta)
    if ($Delta.Status -eq 'na') { return [char]0x2014 }
    return (Format-SignedPoints $Delta.DeltaPoints)
}

function Format-CoverageComment {
    <#
    .SYNOPSIS
        Render the full sticky merged-coverage comment from base + head metrics.
    .PARAMETER BaseMetrics / HeadMetrics
        Coverage metrics objects (Line/Branch/BranchesCovered/BranchesTotal). Head
        drives the PR column; base supplies the comparison column.
    .PARAMETER HeadSha / BaseSha
        Commit SHAs for the header (short-formatted here).
    .PARAMETER RunUrl
        Optional workflow-run URL for the footer.
    .PARAMETER Failed
        When set, emit a short failure notice instead of the table (so the poster
        always has a body to write).
    .PARAMETER BaselineUnavailable
        When set, the base branch measurement is missing (e.g. its build failed), so
        render the PR's absolute coverage with an em-dash delta and a note, instead
        of a misleading comparison.
    .PARAMETER NoBaseline
        When set, there is intentionally no base to compare against (a manual
        branch-dispatch measure). Render the head's absolute coverage as a plain
        two-column table with no base column, delta, or "vs the base branch" wording.
    #>
    param(
        [AllowNull()]$BaseMetrics,
        [AllowNull()]$HeadMetrics,
        [string]$HeadSha = '',
        [string]$BaseSha = '',
        [string]$RunUrl = '',
        [switch]$Failed,
        [switch]$BaselineUnavailable,
        [switch]$NoBaseline
    )
    $marker = $script:CoverageCommentMarker
    $flask = [char]::ConvertFromUtf32(0x1F9EA)  # test tube
    $headShort = if ($HeadSha) { $HeadSha.Substring(0, [math]::Min(7, $HeadSha.Length)) } else { 'unknown' }
    $baseShort = if ($BaseSha) { $BaseSha.Substring(0, [math]::Min(7, $BaseSha.Length)) } else { '' }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add($marker)
    $lines.Add("## $flask Merged coverage")
    $lines.Add('')

    if ($Failed -or -not (Test-CoverageMetricsPresent $HeadMetrics)) {
        $lines.Add('> [!CAUTION]')
        $suffix = if ($RunUrl) { " See the [workflow run]($RunUrl) for details." } else { '' }
        $lines.Add("> Coverage run failed for ``$headShort`` (build or measurement error).$suffix")
        $lines.Add('')
        return ($lines -join "`n")
    }

    if ($NoBaseline) {
        # No base to compare against (a manual branch-dispatch measure). Show the
        # absolute numbers in a plain two-column table — no base column, no delta,
        # and none of the "vs the base branch" / "baseline unavailable" wording.
        $lines.Add("Coverage for ``$headShort`` — unit + selftest merged.")
        $lines.Add('')
        $lines.Add('| Metric | Coverage |')
        $lines.Add('|---|--:|')
        $lines.Add("| Line   | $(Format-Percent $HeadMetrics.Line) |")
        $lines.Add("| Branch | $(Format-BranchCell $HeadMetrics.Branch $HeadMetrics.BranchesCovered $HeadMetrics.BranchesTotal) |")
        $lines.Add('')
        $absLegend = 'Coverage is unit + selftest merged (Debug x64) on the CI runner; no base branch to compare against.'
        if ($RunUrl) {
            $lines.Add("<sub>$absLegend Cobertura reports attached to the <a href=`"$RunUrl`">workflow run</a> as artifacts.</sub>")
        } else {
            $lines.Add("<sub>$absLegend</sub>")
        }
        return ($lines -join "`n")
    }

    $hasBase = (Test-CoverageMetricsPresent $BaseMetrics) -and -not $BaselineUnavailable
    $baseSuffix = if ($hasBase -and $baseShort) { " (``$baseShort``)" } else { '' }
    $lines.Add("Coverage for ``$headShort`` vs the base branch$baseSuffix — unit + selftest merged.")
    $lines.Add('')

    if ($BaselineUnavailable) {
        $lines.Add('> [!WARNING]')
        $lines.Add('> The base branch measurement is unavailable, so no delta is shown — reporting the PR''s absolute coverage only.')
        $lines.Add('')
    }

    $baseLine   = if ($hasBase) { $BaseMetrics.Line } else { $null }
    $baseBranch = if ($hasBase) { $BaseMetrics.Branch } else { $null }
    $baseBc     = if ($hasBase) { $BaseMetrics.BranchesCovered } else { $null }
    $baseBt     = if ($hasBase) { $BaseMetrics.BranchesTotal } else { $null }

    $lineDelta   = Get-CoverageDelta -BasePct $baseLine   -HeadPct $HeadMetrics.Line
    $branchDelta = Get-CoverageDelta -BasePct $baseBranch -HeadPct $HeadMetrics.Branch

    $lines.Add('| Metric | base | PR | ' + [char]0x0394 + ' | |')
    $lines.Add('|---|--:|--:|--:|:-:|')
    $lines.Add(('| Line   | {0} | {1} | {2} | {3} |' -f `
        (Format-Percent $baseLine), `
        (Format-Percent $HeadMetrics.Line), `
        (Format-CoverageDeltaCell $lineDelta), `
        (Get-CoverageStatusGlyph $lineDelta.Status)))
    $lines.Add(('| Branch | {0} | {1} | {2} | {3} |' -f `
        (Format-BranchCell $baseBranch $baseBc $baseBt), `
        (Format-BranchCell $HeadMetrics.Branch $HeadMetrics.BranchesCovered $HeadMetrics.BranchesTotal), `
        (Format-CoverageDeltaCell $branchDelta), `
        (Get-CoverageStatusGlyph $branchDelta.Status)))
    $lines.Add('')

    if ($hasBase -and $lineDelta.Status -eq 'unchanged' -and $branchDelta.Status -eq 'unchanged') {
        $lines.Add('No coverage change beyond the noise floor. ' + [char]0x2705)
        $lines.Add('')
    }

    $legend = [char]0x2705 + ' higher / ' + [char]0x26A0 + [char]0xFE0F + ' lower / ' +
        [char]0x2248 + ' within noise. Δ is in percentage points; coverage is unit + selftest merged (Debug x64) on the CI runner.'
    if ($RunUrl) {
        $lines.Add("<sub>$legend Cobertura reports attached to the <a href=`"$RunUrl`">workflow run</a> as artifacts.</sub>")
    } else {
        $lines.Add("<sub>$legend</sub>")
    }

    return ($lines -join "`n")
}

function Format-CoverageCommentFromMetrics {
    <#
    .SYNOPSIS
        Pick the right Format-CoverageComment render mode from a pair of (already
        sanitized) metrics objects, so the poster and the run-summary select the mode
        the same way — one tested code path instead of duplicated YAML branching.
    .DESCRIPTION
        Selection:
          - head not present                -> Failed (CAUTION).
          - head present, HasBase = $false  -> NoBaseline (absolute, branch dispatch).
          - head + base present             -> full base | PR | Δ comparison.
          - head present, base missing      -> BaselineUnavailable (absolute + warning).
        Pass metrics already run through ConvertTo-SafeCoverageMetrics.
    #>
    param(
        [AllowNull()]$HeadMetrics,
        [AllowNull()]$BaseMetrics,
        [bool]$HasBase = $true,
        [string]$HeadSha = '',
        [string]$BaseSha = '',
        [string]$RunUrl = ''
    )
    if (-not (Test-CoverageMetricsPresent $HeadMetrics)) {
        return Format-CoverageComment -Failed -HeadSha $HeadSha -RunUrl $RunUrl
    }
    if (-not $HasBase) {
        return Format-CoverageComment -HeadMetrics $HeadMetrics -NoBaseline -HeadSha $HeadSha -RunUrl $RunUrl
    }
    if (Test-CoverageMetricsPresent $BaseMetrics) {
        return Format-CoverageComment -BaseMetrics $BaseMetrics -HeadMetrics $HeadMetrics `
            -HeadSha $HeadSha -BaseSha $BaseSha -RunUrl $RunUrl
    }
    return Format-CoverageComment -HeadMetrics $HeadMetrics -BaselineUnavailable `
        -HeadSha $HeadSha -BaseSha $BaseSha -RunUrl $RunUrl
}
