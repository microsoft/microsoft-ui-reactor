<#
.SYNOPSIS
    Pure, dot-source-able helpers for the on-demand /perf comparison workflow
    (.github/workflows/perf-compare.yml).

.DESCRIPTION
    These functions have no side effects beyond Write-Warning so they can be
    unit-tested locally without running a WinUI harness:

      Read-HarnessMetrics       parse one run's {AppName}.metrics.json (preferred)
                                or fall back to {AppName}.report.txt.
      Get-PerfMedian            median of a numeric sample.
      Get-PerfRelativeSpreadPct (max-min)/|median| as a percent — run-to-run noise.
      Measure-PerfRuns          median + spread across N per-run metric objects.
      Get-PerfDelta             signed %, direction-aware, with a noise band.
      Format-PerfComment        the sticky two-table markdown comment.

    The four headline metrics (Release build, StocksGrid workload):
      Renders/sec      higher is better
      Avg Reconcile ms lower is better
      Avg Diff ms      lower is better
      Avg Memory MB    lower is better
#>

Set-StrictMode -Version Latest

# Hidden marker used to find + update-in-place the sticky PR comment.
$script:PerfCommentMarker = '<!-- reactor-perf-compare -->'

# Headline metric table spec, shared by the comment renderer.
$script:PerfMetricSpec = @(
    [pscustomobject]@{ Key = 'RendersPerSec';  Label = 'Renders/sec';       LowerIsBetter = $false; Digits = 2; Arrow = [char]0x2191 } # up
    [pscustomobject]@{ Key = 'AvgReconcileMs'; Label = 'Avg Reconcile (ms)'; LowerIsBetter = $true;  Digits = 1; Arrow = [char]0x2193 } # down
    [pscustomobject]@{ Key = 'AvgDiffMs';      Label = 'Avg Diff (ms)';      LowerIsBetter = $true;  Digits = 1; Arrow = [char]0x2193 }
    [pscustomobject]@{ Key = 'AvgMemoryMB';    Label = 'Avg Memory (MB)';    LowerIsBetter = $true;  Digits = 1; Arrow = [char]0x2193 }
)

# Allocation metric table spec (Reactor main-vs-PR only — there is no meaningful
# cross-framework allocation comparison). Lower is better for both. These move
# directly when an allocation-reduction PR lands, where the mean-ms / working-set
# headline metrics above are largely insensitive. Emitted by PerfTracker as
# allocBytesPerRender / gen0PerKRenders; absent (n/a) for harness builds that
# predate the metric (e.g. a PR head opened before this gate merged).
$script:PerfAllocMetricSpec = @(
    [pscustomobject]@{ Key = 'AllocBytesPerRender'; Label = 'Alloc bytes/render';   LowerIsBetter = $true; Digits = 0; Arrow = [char]0x2193 }
    [pscustomobject]@{ Key = 'Gen0PerKRenders';     Label = 'Gen0 GC / 1k renders'; LowerIsBetter = $true; Digits = 2; Arrow = [char]0x2193 }
)

# Minimum-effect band (percent) for the micro-suite ALLOC flag. The per-side micro
# runs are not rep-interleaved, so a sub-1% systematic process-to-process alloc
# offset on non-deterministic benches (dispatcher / background-thread allocations,
# e.g. M5 "Dispatch_Switch_Warm" measured 6/6 distinct alloc values per rep) can
# make the tight within-process 95% CI exclude 0 on identical code. Requiring the CI
# to clear +-this band absorbs that offset while still catching real structural
# alloc changes, which are several percent to many-x. Set to 0 to restore the pure
# "CI excludes 0" rule.
$script:MicroAllocMinEffectPct = 1.0

function ConvertTo-PerfDouble {
    <#
    .SYNOPSIS Culture-tolerant parse of a captured numeric string, or $null.
    #>
    param([string]$Raw)
    if ([string]::IsNullOrWhiteSpace($Raw)) { return $null }
    # Harness report numbers carry no thousands separators, so a comma can only
    # be a decimal separator emitted under a comma-decimal culture — normalise it.
    $norm = ($Raw.Trim() -replace ',', '.')
    [double]$val = 0
    $ok = [double]::TryParse(
        $norm,
        [System.Globalization.NumberStyles]::Float,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$val)
    if ($ok) { return $val }
    return $null
}

function Get-PerfReportField {
    param([string]$Text, [string]$Pattern, [switch]$AsInt)
    $m = [regex]::Match($Text, $Pattern)
    if (-not $m.Success) { return $null }
    $val = ConvertTo-PerfDouble $m.Groups[1].Value
    if ($null -eq $val) { return $null }
    if ($AsInt) { return [int][math]::Round($val) }
    return $val
}

function Read-HarnessMetrics {
    <#
    .SYNOPSIS
        Normalised metrics for one harness run. Prefers {AppName}.metrics.json
        (emitted by --json); falls back to {AppName}.report.txt regex parsing
        for harness builds/variants that predate --json (e.g. StressPerf.Direct).
    .OUTPUTS
        PSCustomObject with RendersPerSec, AvgReconcileMs, AvgDiffMs,
        AvgMemoryMB, TotalRenders, DurationSeconds (any of which may be $null
        when not applicable), and Source ('json' | 'report' | 'none').
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string]$AppName
    )

    $result = [pscustomobject]@{
        AppName             = $AppName
        RendersPerSec       = $null
        AvgReconcileMs      = $null
        AvgDiffMs           = $null
        AvgMemoryMB         = $null
        AllocBytesPerRender = $null
        Gen0PerKRenders     = $null
        Gen0                = $null
        Gen1                = $null
        Gen2                = $null
        TotalRenders        = $null
        DurationSeconds     = $null
        Source              = 'none'
    }

    $jsonPath   = Join-Path $Directory ("{0}.metrics.json" -f $AppName)
    $reportPath = Join-Path $Directory ("{0}.report.txt" -f $AppName)

    if (Test-Path -LiteralPath $jsonPath) {
        try {
            $j = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
            # Every field below is always emitted by GetMetricsJson, so a missing
            # or null one means a partial write or a schema mismatch. Reject the
            # JSON and fall back to report.txt rather than coerce null -> 0 and
            # surface a misleading metric / delta.
            $required = 'rendersPerSec', 'avgReconcileMs', 'avgDiffMs', 'avgMemoryMB', 'totalRenders', 'durationSeconds'
            $missing = @($required | Where-Object {
                    $p = $j.PSObject.Properties[$_]
                    (-not $p) -or ($null -eq $p.Value)
                })
            if ($missing.Count -gt 0) {
                Write-Warning "Read-HarnessMetrics: '$jsonPath' is missing/null field(s): $($missing -join ', '); falling back to report.txt."
            }
            else {
                $result.RendersPerSec   = [double]$j.rendersPerSec
                $result.AvgReconcileMs  = [double]$j.avgReconcileMs
                $result.AvgDiffMs       = [double]$j.avgDiffMs
                $result.AvgMemoryMB     = [double]$j.avgMemoryMB
                $result.TotalRenders    = [int]$j.totalRenders
                $result.DurationSeconds = [double]$j.durationSeconds
                # Optional allocation fields (added after the headline four). Read
                # when present so a PR head that predates them still parses via the
                # required-set above (its alloc cells just read n/a) instead of being
                # rejected. Guarded with PSObject.Properties for Set-StrictMode.
                if ($j.PSObject.Properties['allocBytesPerRender'] -and $null -ne $j.allocBytesPerRender) { $result.AllocBytesPerRender = [double]$j.allocBytesPerRender }
                if ($j.PSObject.Properties['gen0PerKRenders'] -and $null -ne $j.gen0PerKRenders) { $result.Gen0PerKRenders = [double]$j.gen0PerKRenders }
                if ($j.PSObject.Properties['gen0'] -and $null -ne $j.gen0) { $result.Gen0 = [int]$j.gen0 }
                if ($j.PSObject.Properties['gen1'] -and $null -ne $j.gen1) { $result.Gen1 = [int]$j.gen1 }
                if ($j.PSObject.Properties['gen2'] -and $null -ne $j.gen2) { $result.Gen2 = [int]$j.gen2 }
                $result.Source          = 'json'
                return $result
            }
        }
        catch {
            Write-Warning "Read-HarnessMetrics: '$jsonPath' is not valid JSON ($($_.Exception.Message)); falling back to report.txt."
        }
    }

    if (-not (Test-Path -LiteralPath $reportPath)) {
        Write-Warning "Read-HarnessMetrics: no metrics.json or report.txt for '$AppName' in '$Directory'."
        return $result
    }

    $text = Get-Content -LiteralPath $reportPath -Raw
    $result.Source          = 'report'
    $result.TotalRenders    = Get-PerfReportField $text 'Total Renders:\s*([0-9][0-9.,]*)' -AsInt
    $result.DurationSeconds = Get-PerfReportField $text 'Duration:\s*([0-9][0-9.,]*)\s*s'
    # Reconcile / Diff lines only exist for declarative (Reactor) variants;
    # imperative WinUI3 (StressPerf.Direct) omits them -> stay $null (n/a).
    $result.AvgReconcileMs  = Get-PerfReportField $text 'Avg Reconcile:\s*([0-9][0-9.,]*)\s*ms'
    $result.AvgDiffMs       = Get-PerfReportField $text 'Avg Diff:\s*([0-9][0-9.,]*)\s*ms'
    $result.AvgMemoryMB     = Get-PerfReportField $text 'Avg Memory:\s*([0-9][0-9.,]*)\s*MB'
    # Optional allocation lines: absent in pre-metric harness builds and in the
    # Rust port's report.txt, so they stay $null (n/a) there.
    $result.AllocBytesPerRender = Get-PerfReportField $text 'Alloc/render:\s*([0-9][0-9.,]*)\s*bytes'
    $result.Gen0PerKRenders     = Get-PerfReportField $text 'Gen0/Krender:\s*([0-9][0-9.,]*)'
    $gcm = [regex]::Match($text, 'GC Gen0/1/2:\s*([0-9]+)\s*/\s*([0-9]+)\s*/\s*([0-9]+)')
    if ($gcm.Success) {
        $result.Gen0 = [int]$gcm.Groups[1].Value
        $result.Gen1 = [int]$gcm.Groups[2].Value
        $result.Gen2 = [int]$gcm.Groups[3].Value
    }

    if ($null -ne $result.TotalRenders -and $null -ne $result.DurationSeconds -and $result.DurationSeconds -gt 0) {
        $result.RendersPerSec = [math]::Round($result.TotalRenders / $result.DurationSeconds, 4)
    }
    return $result
}

function Get-PerfMedian {
    param([Parameter(ValueFromPipeline)][AllowNull()][double[]]$Values)
    $v = @($Values | Where-Object { $null -ne $_ })
    if ($v.Count -eq 0) { return $null }
    $sorted = @($v | Sort-Object)
    $n = $sorted.Count
    if ($n % 2 -eq 1) { return [double]$sorted[[int][math]::Floor($n / 2)] }
    return ([double]$sorted[$n / 2 - 1] + [double]$sorted[$n / 2]) / 2.0
}

function Get-PerfRelativeSpreadPct {
    <#
    .SYNOPSIS Run-to-run dispersion (max-min)/|median| as a percent. 0 for <2 samples.
    #>
    param([AllowNull()][double[]]$Values)
    $v = @($Values | Where-Object { $null -ne $_ })
    if ($v.Count -lt 2) { return 0.0 }
    $min = ($v | Measure-Object -Minimum).Minimum
    $max = ($v | Measure-Object -Maximum).Maximum
    $med = Get-PerfMedian $v
    if ($null -eq $med -or $med -eq 0) { return 0.0 }
    return [math]::Round((($max - $min) / [math]::Abs($med)) * 100.0, 1)
}

function Get-StudentTCritical {
    <#
    .SYNOPSIS
        Two-sided 95% Student-t critical value for the given degrees of freedom.
        Exact hard-coded table for df 1..30, then a CONSERVATIVE step function
        for df > 30 — keeps PerfLib dependency-free.
    .DESCRIPTION
        t(.975, df) decreases monotonically in df, so for any df in a band
        (a, b] the largest true value occurs at the small-df edge. Each band
        therefore returns t(.975, a) (the value at its left edge), which is
        >= the true critical value for every df it covers. This guarantees the
        resulting CI half-width is never understated (we never flag a delta as
        significant on too-narrow an interval); it is mildly conservative within
        a band. The default reps (12 -> df=11) land in the exact table, so this
        only matters if someone runs >31 reps.
    #>
    param([int]$Df)
    if ($Df -lt 1) { return [double]::NaN }
    $t = @(
        12.706, 4.303, 3.182, 2.776, 2.571, 2.447, 2.365, 2.306, 2.262, 2.228,
        2.201, 2.179, 2.160, 2.145, 2.131, 2.120, 2.110, 2.101, 2.093, 2.086,
        2.080, 2.074, 2.069, 2.064, 2.060, 2.056, 2.052, 2.048, 2.045, 2.042
    )
    if ($Df -le 30) { return [double]$t[$Df - 1] }
    # Step down using each band's left-edge t(.975) value (>= true across the band).
    if ($Df -le 35) { return 2.042 }   # t(30)
    if ($Df -le 40) { return 2.030 }   # t(35)
    if ($Df -le 45) { return 2.021 }   # t(40)
    if ($Df -le 50) { return 2.014 }   # t(45)
    if ($Df -le 60) { return 2.009 }   # t(50)
    if ($Df -le 70) { return 2.000 }   # t(60)
    if ($Df -le 80) { return 1.994 }   # t(70)
    if ($Df -le 90) { return 1.990 }   # t(80)
    if ($Df -le 100) { return 1.987 }  # t(90)
    if ($Df -le 120) { return 1.984 }  # t(100)
    if ($Df -le 150) { return 1.980 }  # t(120)
    if ($Df -le 200) { return 1.976 }  # t(150)
    if ($Df -le 300) { return 1.972 }  # t(200)
    if ($Df -le 500) { return 1.968 }  # t(300)
    if ($Df -le 1000) { return 1.965 } # t(500)
    return 1.962                       # t(1000); asymptote -> 1.960
}

function Get-PerfPairedDeltaStats {
    <#
    .SYNOPSIS
        Paired per-iteration percent-change statistics for two index-aligned
        samples (Baseline run i vs Candidate run i, as collected interleaved).
        Returns $null when fewer than 2 valid pairs survive (a CI needs >= 2).
    .DESCRIPTION
        For each index i where both samples are non-null and the baseline is
        non-zero, the per-pair change is ((cand_i - base_i) / |base_i|) * 100.
        Reports N, the mean / median change, the sample standard deviation, the
        standard error, and a two-sided 95% confidence interval for the MEAN
        change (mean +/- t(.975, N-1) * SE). The interval excluding 0 is the
        data-driven "is this a real change?" test that replaces a fixed % floor.
        All fields are returned at full precision (no rounding) because callers
        gate significance on them; round only when formatting for display.
    .OUTPUTS
        PSCustomObject { N; MeanPct; MedianPct; SdPct; SePct; CiLowPct; CiHighPct;
        CiHalfWidthPct } or $null.
    #>
    param(
        [AllowNull()][object[]]$BaselineSamples,
        [AllowNull()][object[]]$CandidateSamples
    )
    if ($null -eq $BaselineSamples -or $null -eq $CandidateSamples) { return $null }
    $n = [math]::Min($BaselineSamples.Count, $CandidateSamples.Count)
    $deltas = [System.Collections.Generic.List[double]]::new()
    for ($i = 0; $i -lt $n; $i++) {
        $b = $BaselineSamples[$i]
        $c = $CandidateSamples[$i]
        if ($null -eq $b -or $null -eq $c) { continue }
        $bd = [double]$b
        $cd = [double]$c
        if ($bd -eq 0) { continue }
        $deltas.Add((($cd - $bd) / [math]::Abs($bd)) * 100.0)
    }
    if ($deltas.Count -lt 2) { return $null }
    $cnt = $deltas.Count
    $mean = ($deltas | Measure-Object -Average).Average
    $ss = 0.0
    foreach ($d in $deltas) { $ss += ($d - $mean) * ($d - $mean) }
    $sd = [math]::Sqrt($ss / ($cnt - 1))
    $se = $sd / [math]::Sqrt($cnt)
    $half = (Get-StudentTCritical -Df ($cnt - 1)) * $se
    # Full precision on purpose: Get-PerfDelta gates significance/direction on
    # these values, so rounding here (e.g. a CI bound of 0.004 -> 0.00) could
    # silently flip a real change to "noise". Rounding is a display concern and
    # happens at format time (Format-PerfDeltaCell / Get-PerfDelta display fields).
    return [pscustomobject]@{
        N              = $cnt
        MeanPct        = $mean
        MedianPct      = Get-PerfMedian ([double[]]$deltas.ToArray())
        SdPct          = $sd
        SePct          = $se
        CiLowPct       = $mean - $half
        CiHighPct      = $mean + $half
        CiHalfWidthPct = $half
    }
}

function Measure-PerfRuns {
    <#
    .SYNOPSIS
        Collapse N per-run metric objects (from Read-HarnessMetrics) into a
        single object carrying the median of each metric plus a "<Key>Spread"
        relative-dispersion percent.
    #>
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Runs)

    $keys = 'RendersPerSec', 'AvgReconcileMs', 'AvgDiffMs', 'AvgMemoryMB',
            'AllocBytesPerRender', 'Gen0PerKRenders', 'Gen0', 'Gen1', 'Gen2',
            'TotalRenders', 'DurationSeconds'
    $agg = [ordered]@{ RunCount = @($Runs).Count }
    foreach ($k in $keys) {
        # Ordered per-run values WITH $null placeholders preserved (and tolerant of
        # run objects that lack a key, e.g. a variant that predates the metric), so
        # a paired analysis downstream can zip Baseline run i against Candidate run i
        # by index. Get-PerfPairedDeltaStats skips any pair with a null/zero side.
        $ordered = @($Runs | ForEach-Object { if ($_.PSObject.Properties[$k]) { $_.$k } else { $null } })
        $vals = @($ordered | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ })
        $agg[$k] = Get-PerfMedian $vals
        $agg["${k}Spread"] = Get-PerfRelativeSpreadPct $vals
        $agg["${k}Samples"] = $ordered
    }
    return [pscustomobject]$agg
}

function Get-PerfDelta {
    <#
    .SYNOPSIS
        Signed percent change of Candidate vs Baseline, direction-aware. Status is
        one of: better | worse | noise | na.
    .DESCRIPTION
        Preferred path (when per-iteration samples are supplied): a paired 95%
        confidence interval over the per-pair percent deltas. The change is flagged
        better/worse ONLY when that CI excludes 0 — the data-driven band that
        replaces the fixed % floor. DeltaPct is the mean paired change; CiLowPct /
        CiHighPct / N describe the interval.

        Fallback path (no samples, e.g. legacy callers / unit tests): the point
        delta of the scalar Baseline/Candidate vs a max(NoiseFloorPct, SpreadPct)
        noise band.
    .PARAMETER NoiseFloorPct
        Absolute floor for the fallback "within noise" band (default 4%). Unused on
        the sample/CI path.
    .PARAMETER SpreadPct
        Run-to-run dispersion for this metric; the fallback band is
        max(NoiseFloorPct, SpreadPct).
    .PARAMETER BaselineSamples / CandidateSamples
        Index-aligned per-run values (with $null placeholders) for the paired CI.
    .PARAMETER RequirePairedCI
        When set, the paired 95% CI is the ONLY admissible flag: if fewer than 2
        aligned pairs survive (so Get-PerfPairedDeltaStats returns $null) the result
        is 'na' rather than the point-delta vs % -floor fallback. Used by the
        micro-suite, whose contract is "flag only when the paired CI excludes 0" —
        without it a single surviving pair (e.g. after later reps error out and are
        filtered) would be flagged better/worse off one sample with N=$null. The
        macro path leaves it off and keeps the legacy point-delta fallback.
    .PARAMETER MinEffectPct
        Minimum effect size (percent) for a paired-CI flag. The change is flagged
        better/worse only when the 95% CI lies ENTIRELY beyond +-MinEffectPct, not
        merely beyond 0. Default 0 keeps the pure "CI excludes 0" rule (macro path).
        The micro-suite passes a small band (~1%) on the ALLOC delta because the
        per-side micro runs are not rep-interleaved: a sub-1% systematic
        process-to-process alloc offset (dispatcher / background-thread allocations
        on benches like M5 are not bit-deterministic) can make the tight
        within-process CI exclude 0 on identical code. Requiring >= MinEffectPct
        absorbs that offset while still catching real structural alloc changes,
        which are multiple percent to many-x. This is NOT the blanket point-delta
        floor PR1 removed: it is a CI-vs-band test, applied only to the
        non-interleaved micro alloc metric.
    #>
    param(
        [AllowNull()]$Baseline,
        [AllowNull()]$Candidate,
        [Parameter(Mandatory)][bool]$LowerIsBetter,
        [double]$NoiseFloorPct = 4.0,
        [double]$SpreadPct = 0.0,
        [AllowNull()][object[]]$BaselineSamples,
        [AllowNull()][object[]]$CandidateSamples,
        [switch]$RequirePairedCI,
        [double]$MinEffectPct = 0.0
    )
    if ($null -eq $Baseline -or $null -eq $Candidate -or [double]$Baseline -eq 0) {
        return [pscustomobject]@{ DeltaPct = $null; Status = 'na'; Improved = $null; CiLowPct = $null; CiHighPct = $null; N = $null }
    }
    $b = [double]$Baseline
    $c = [double]$Candidate
    $deltaPct = (($c - $b) / [math]::Abs($b)) * 100.0

    # Preferred: paired CI over the per-iteration deltas. Significant only when the
    # 95% CI excludes 0 — the data-driven band that replaces the fixed % floor.
    $stats = Get-PerfPairedDeltaStats -BaselineSamples $BaselineSamples -CandidateSamples $CandidateSamples
    if ($null -ne $stats) {
        # Decide direction + significance on FULL-PRECISION stats. Rounding is a
        # display-only concern: a CI bound like 0.004 must not round to 0.00 and
        # silently flip a genuine change to "noise". The returned CI fields are
        # rounded to 2dp for presentation; the cell format rounds again to 1dp.
        $meanPct = [double]$stats.MeanPct
        $ciLow = [double]$stats.CiLowPct
        $ciHigh = [double]$stats.CiHighPct
        $improved = if ($LowerIsBetter) { $meanPct -lt 0 } else { $meanPct -gt 0 }
        # Flag only when the CI lies entirely beyond +-MinEffectPct (default 0 => the
        # pure "excludes 0" rule). The micro alloc path passes a small band so a
        # sub-band process-to-process offset on non-deterministic benches reads noise.
        $ciExcludesBand = ($ciLow -gt $MinEffectPct) -or ($ciHigh -lt -$MinEffectPct)
        $status = if (-not $ciExcludesBand) { 'noise' } elseif ($improved) { 'better' } else { 'worse' }
        return [pscustomobject]@{
            DeltaPct  = [math]::Round($meanPct, 1)
            Status    = $status
            Improved  = $improved
            CiLowPct  = [math]::Round($ciLow, 2)
            CiHighPct = [math]::Round($ciHigh, 2)
            N         = $stats.N
        }
    }

    # Micro-suite contract: with < 2 rep-aligned pairs there is no admissible CI, so
    # report 'na' rather than flag a lone surviving pair off the point-delta fallback.
    if ($RequirePairedCI) {
        return [pscustomobject]@{ DeltaPct = $null; Status = 'na'; Improved = $null; CiLowPct = $null; CiHighPct = $null; N = $null }
    }

    # Fallback: point delta vs a max(floor, spread) noise band.
    $improved = if ($LowerIsBetter) { $deltaPct -lt 0 } else { $deltaPct -gt 0 }
    $band = [math]::Max($NoiseFloorPct, $SpreadPct)
    $status = if ([math]::Abs($deltaPct) -lt $band) { 'noise' } elseif ($improved) { 'better' } else { 'worse' }
    return [pscustomobject]@{ DeltaPct = [math]::Round($deltaPct, 1); Status = $status; Improved = $improved; CiLowPct = $null; CiHighPct = $null; N = $null }
}

function Format-PerfNumber {
    param([AllowNull()]$Value, [int]$Digits = 1)
    if ($null -eq $Value) { return 'n/a' }
    # Digits=0 (used by the alloc-bytes metric) must render a clean integer — a
    # "0.<zeros>" format with zero zeros collapses to "0." and emits a trailing
    # dot ("52000."), so format integers with a plain "0" pattern instead.
    $d = [math]::Max(0, $Digits)
    $rounded = [math]::Round([double]$Value, $d)
    $fmt = if ($d -le 0) { '0' } else { "0.$('0' * $d)" }
    return $rounded.ToString($fmt, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-PerfDeltaCell {
    param([pscustomobject]$Delta)
    if ($null -eq $Delta.DeltaPct) { return '—' }
    $s = ('{0:+0.0;-0.0;0.0}' -f $Delta.DeltaPct)
    $cell = "$s%"
    # Append the 95% CI of the paired delta when present. Set-StrictMode-safe
    # property probe so legacy callers passing only DeltaPct still render a bare %.
    if (($Delta.PSObject.Properties['CiLowPct']) -and ($null -ne $Delta.CiLowPct) -and
        ($Delta.PSObject.Properties['CiHighPct']) -and ($null -ne $Delta.CiHighPct)) {
        $lo = ('{0:+0.0;-0.0;0.0}' -f $Delta.CiLowPct)
        $hi = ('{0:+0.0;-0.0;0.0}' -f $Delta.CiHighPct)
        $cell += " <sub>95% CI [$lo, $hi]</sub>"
    }
    return $cell
}

function Get-PerfStatusGlyph {
    param([string]$Status)
    switch ($Status) {
        'better' { return [char]0x2705 + ' improvement' }                       # checkmark
        'worse'  { return [char]0x26A0 + [char]0xFE0F + ' regression' }          # warning
        'noise'  { return [char]0x2248 + ' within noise' }                       # almost-equal
        default  { return '—' }
    }
}

# ── Reconciler micro-suite (PerfBench.ControlModel) ──────────────────────────
# /perf's macro StocksGrid workload is render-bound and WinUI-diluted, so it can
# neither resolve small Core/Reconciler time deltas nor see allocation changes.
# The PerfBench.ControlModel micro-suite (spec-047 M1-M13) runs the production
# `--variant Reactor` control-model path as a headless loop bracketed by
# per-thread alloc + GC counters at ns-resolution, with no render pipeline in the
# measured region. These helpers parse its JSON-Lines output and compute the
# PR-vs-main paired delta per bench, reusing the same CI machinery as the macro
# table (Get-PerfDelta / Get-PerfPairedDeltaStats).

function Read-MicroBenchResults {
    <#
    .SYNOPSIS
        Parse a PerfBench.ControlModel results.jsonl (JSON-Lines) into a per-bench
        sample map for the production `Reactor` variant.
    .DESCRIPTION
        Each line is one MeasurementResult (camelCase JSON). Only variant == 'Reactor'
        rows with status 'ok' are kept — the legacy Direct / ReactorToday variants are
        an intra-build A/B (Reactor-vs-today / Reactor-vs-raw-WinUI), NOT the PR-vs-main
        delta /perf reports. Rows are ordered by repetition and tagged with their
        repetition index so the paired analysis can align baseline rep i against
        candidate rep i BY REPETITION (not array position) — a dropped/errored rep on
        one side must not shift every later sample. Malformed lines are skipped,
        including any row missing benchId / meanNs / allocBytes or whose repetition
        is absent or non-numeric (it would otherwise throw on the [int] cast below).
    .OUTPUTS
        OrderedDictionary benchId -> [pscustomobject]@{ BenchId; Name;
        Repetitions [int[]]; MeanNsSamples [double[]]; AllocBytesSamples [double[]] }
        (the three arrays are parallel / index-aligned). MeanNsSamples and
        AllocBytesSamples are both PER-OP: BenchRunner already divides meanNs by the
        iteration count, and allocBytes (reported as the whole-loop total) is divided
        by the row's iterations here so both sit on the same per-op basis as the
        "B/op" column. Empty when the file is missing / empty / has no usable
        Reactor rows.
    #>
    param([AllowNull()][string]$Path)
    $map = [ordered]@{}
    if (-not $Path -or -not (Test-Path -LiteralPath $Path)) { return $map }
    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($line in [System.IO.File]::ReadLines($Path)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $obj = $null
        try { $obj = $line | ConvertFrom-Json } catch { continue }
        if ($null -eq $obj) { continue }
        $variant = if ($obj.PSObject.Properties['variant']) { [string]$obj.variant } else { '' }
        if ($variant -ne 'Reactor') { continue }
        $status = if ($obj.PSObject.Properties['status']) { [string]$obj.status } else { 'ok' }
        if ($status -and $status -ne 'ok') { continue }
        if (-not $obj.PSObject.Properties['benchId'] -or
            -not $obj.PSObject.Properties['meanNs'] -or
            -not $obj.PSObject.Properties['allocBytes'] -or
            -not $obj.PSObject.Properties['repetition']) { continue }
        # repetition drives the rep-keyed pairing and is cast to [int] when ordering /
        # tagging samples below; a present-but-non-numeric value would throw PAST the
        # malformed-line guard and break /perf comment generation, so validate the cast
        # here and skip the row if it won't parse (honors "malformed lines are skipped").
        try { $null = [int]$obj.repetition } catch { continue }
        $rows.Add($obj)
    }
    if ($rows.Count -eq 0) { return $map }
    foreach ($g in ($rows | Group-Object -Property benchId)) {
        $ordered = @($g.Group | Sort-Object { [int]$_.repetition })
        $name = ''
        if ($ordered.Count -gt 0 -and $ordered[0].PSObject.Properties['benchName']) { $name = [string]$ordered[0].benchName }
        $map[[string]$g.Name] = [pscustomobject]@{
            BenchId           = [string]$g.Name
            Name              = $name
            Repetitions       = [int[]]@($ordered | ForEach-Object { [int]$_.repetition })
            MeanNsSamples     = [double[]]@($ordered | ForEach-Object { [double]$_.meanNs })
            AllocBytesSamples = [double[]]@($ordered | ForEach-Object {
                    # Per-OP allocation: meanNs is already per-op but BenchRunner reports
                    # allocBytes as the whole-loop total, so normalize by the row's
                    # iterations to match the "B/op" column. Constant divisor keeps it
                    # deterministic for identical code.
                    $iter = if ($_.PSObject.Properties['iterations']) { [double]$_.iterations } else { 0 }
                    if ($iter -gt 0) { [double]$_.allocBytes / $iter } else { [double]$_.allocBytes }
                })
        }
    }
    return $map
}

function Get-PerfMicroComparison {
    <#
    .SYNOPSIS
        Per-bench PR-vs-main paired comparison for the micro-suite. For each bench
        present in BOTH maps, computes the paired 95%-CI delta of mean ns/op and
        alloc bytes/op (lower is better), reusing Get-PerfDelta. Returns rows sorted
        by the numeric bench-id suffix (M1, M2, ... M13).
    .OUTPUTS
        Array of [pscustomobject]@{ BenchId; Name; MainMeanNs; PrMeanNs; NsDelta;
        MainAllocBytes; PrAllocBytes; AllocDelta }. Empty when no bench overlaps.
    #>
    param([AllowNull()]$Main, [AllowNull()]$Pr)
    if ($null -eq $Main -or $null -eq $Pr) { return @() }
    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($benchId in @($Main.Keys)) {
        if (-not $Pr.Contains($benchId)) { continue }
        $m = $Main[$benchId]
        $p = $Pr[$benchId]
        # Align samples BY REPETITION, not by array position: if a repetition was
        # dropped/errored (and filtered) on one side, position-zipping would compare
        # main rep i against pr rep i+1 for every later sample and silently mis-pair
        # the paired CI. Pair only repetitions present on BOTH sides.
        $prIdxByRep = @{}
        for ($j = 0; $j -lt $p.Repetitions.Count; $j++) { $prIdxByRep[[int]$p.Repetitions[$j]] = $j }
        $mNs = [System.Collections.Generic.List[object]]::new()
        $pNs = [System.Collections.Generic.List[object]]::new()
        $mAlloc = [System.Collections.Generic.List[object]]::new()
        $pAlloc = [System.Collections.Generic.List[object]]::new()
        for ($i = 0; $i -lt $m.Repetitions.Count; $i++) {
            $rep = [int]$m.Repetitions[$i]
            if (-not $prIdxByRep.ContainsKey($rep)) { continue }
            $j = $prIdxByRep[$rep]
            $mNs.Add($m.MeanNsSamples[$i]);       $pNs.Add($p.MeanNsSamples[$j])
            $mAlloc.Add($m.AllocBytesSamples[$i]); $pAlloc.Add($p.AllocBytesSamples[$j])
        }
        # Displayed medians AND the paired CI are both computed over the SAME
        # rep-aligned sample set, so the table's main/PR columns can't be drawn from
        # a different set of reps than the Δ when a repetition is missing on one side.
        $mNsArr = [object[]]$mNs.ToArray();    $pNsArr = [object[]]$pNs.ToArray()
        $mAllocArr = [object[]]$mAlloc.ToArray(); $pAllocArr = [object[]]$pAlloc.ToArray()
        $mainMeanNs = Get-PerfMedian $mNsArr; $prMeanNs = Get-PerfMedian $pNsArr
        $mainAlloc = Get-PerfMedian $mAllocArr; $prAlloc = Get-PerfMedian $pAllocArr
        $nsDelta = Get-PerfDelta -Baseline $mainMeanNs -Candidate $prMeanNs `
            -LowerIsBetter $true -BaselineSamples $mNsArr -CandidateSamples $pNsArr -RequirePairedCI
        # Alloc DRIVES the row flag, and not every bench is alloc-deterministic:
        # dispatcher / background-thread benches (e.g. M5) carry a sub-1% systematic
        # process-to-process offset that the within-process CI can't cancel, so flag
        # only when the CI clears the +-MinEffectPct band. ns is informational-only
        # (never drives the flag) so it keeps the pure CI-excludes-0 rule.
        $allocDelta = Get-PerfDelta -Baseline $mainAlloc -Candidate $prAlloc `
            -LowerIsBetter $true -BaselineSamples $mAllocArr -CandidateSamples $pAllocArr `
            -RequirePairedCI -MinEffectPct $script:MicroAllocMinEffectPct
        $rows.Add([pscustomobject]@{
            BenchId        = $benchId
            Name           = $m.Name
            MainMeanNs     = $mainMeanNs
            PrMeanNs       = $prMeanNs
            NsDelta        = $nsDelta
            MainAllocBytes = $mainAlloc
            PrAllocBytes   = $prAlloc
            AllocDelta     = $allocDelta
        })
    }
    return @($rows | Sort-Object `
        @{ Expression = { $n = 0; if ([int]::TryParse(($_.BenchId -replace '\D', ''), [ref]$n)) { $n } else { [int]::MaxValue } } }, `
        @{ Expression = { $_.BenchId } })
}

function Get-PerfMicroRowStatus {
    <#
    .SYNOPSIS
        Row flag for a micro-bench. v1 tracks the DETERMINISTIC allocation signal
        only; the ns/op delta is informational and does NOT drive the flag.
    .DESCRIPTION
        Allocated bytes/op is deterministic for identical code (an unchanged diff
        reproduces the same byte count exactly), so its paired CI is a trustworthy
        flag. ns/op is NOT: the per-side micro runs are not yet rep-interleaved, so a
        systematic process-to-process timing offset (thermal / scheduling drift
        between the two back-to-back invocations) shifts every paired ns difference
        the same way and makes the paired CI exclude 0 even for an identical binary
        — empirically a no-op diff flagged -14.8% ns on one bench. Flagging ns would
        therefore emit false improvements/regressions. So the row tracks alloc; ns is
        shown for context. (Rep-level interleaving is the documented fast-follow that
        would let ns be promoted to a flagged signal.)
    #>
    param([AllowNull()][string]$NsStatus, [AllowNull()][string]$AllocStatus)
    if ([string]::IsNullOrEmpty($AllocStatus)) { return 'na' }
    return $AllocStatus
}

function Format-PerfMicroSection {
    <#
    .SYNOPSIS
        Render the reconciler micro-benchmark table (mean ns/op + alloc bytes/op,
        PR vs main) as markdown lines. Empty array when there is nothing to show.
    #>
    param([AllowNull()][object[]]$Micro)
    if ($null -eq $Micro -or @($Micro).Count -eq 0) { return @() }
    $down = [char]0x2193
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("### Reconciler micro-benchmarks (``PerfBench.ControlModel``)")
    $lines.Add('')
    $lines.Add("Production ``--variant Reactor`` control-model path, ns-resolution and WinUI-undiluted (spec-047 M1&ndash;M13) &mdash; $down lower is better. **Status tracks allocated bytes/op**, the authoritative signal here; it is deterministic for structurally-fixed benches, while dispatcher / background-thread benches carry a small process-to-process offset, so a bench is flagged only when its 95% CI clears a &plusmn;$($script:MicroAllocMinEffectPct)% minimum-effect band (real structural alloc changes are several percent to many-x). **ns/op is shown for context** but is not auto-flagged in v1 (the per-side runs are not yet rep-interleaved, so a process-to-process timing offset can otherwise read as significant). Δ is the mean paired change with a 95% CI.")
    $lines.Add('')
    $lines.Add('| Bench | `main` ns/op | Δ ns (95% CI) | `main` B/op | Δ alloc (95% CI) | Status |')
    $lines.Add('|---|--:|--:|--:|--:|:--|')
    foreach ($r in $Micro) {
        $label = if ($r.Name) { ('`{0}` {1}' -f $r.BenchId, $r.Name) } else { ('`{0}`' -f $r.BenchId) }
        $status = Get-PerfMicroRowStatus -NsStatus $r.NsDelta.Status -AllocStatus $r.AllocDelta.Status
        $lines.Add(('| {0} | {1} | {2} | {3} | {4} | {5} |' -f `
                $label, `
            (Format-PerfNumber $r.MainMeanNs 1), `
            (Format-PerfDeltaCell $r.NsDelta), `
            (Format-PerfNumber $r.MainAllocBytes 1), `
            (Format-PerfDeltaCell $r.AllocDelta), `
            (Get-PerfStatusGlyph $status)))
    }
    $lines.Add('')
    return $lines.ToArray()
}

function Format-PerfSkipFloorSection {
    <#
    .SYNOPSIS
        Render the low-mutation skip-floor table: the four headline metrics measured
        at a near-zero mutation percent, isolating the per-tick O(n) child skip-walk
        floor that the 50%-mutation headline workload dilutes. Empty array when there
        is nothing to show.
    .DESCRIPTION
        At ``--percent 0`` the StocksGrid source still mutates exactly one cell per
        tick (StockDataSource.Update clamps the change count to Math.Max(1, ...)), so
        virtually every child is unchanged and reconcile/diff time is dominated by
        ChildReconciler's positional re-walk over all children — the fixed per-tick
        cost a structural-skip optimization targets. The 50%-mutation headline table
        dilutes this floor; here it is the whole signal. Reuses the same paired-Δ 95%
        CI machinery (Get-PerfDelta over the index-aligned per-run samples) as the
        headline table. Returns an empty array when either floor aggregate is $null
        (skip-floor leg disabled, or one side produced no metrics), so the caller
        renders nothing.
    .PARAMETER MainFloor  Aggregated baseline low-mutation metrics (Measure-PerfRuns), or $null.
    .PARAMETER PrFloor    Aggregated PR-head low-mutation metrics, or $null.
    .PARAMETER Percent    The mutation percent the floor leg ran at (heading / preamble).
    #>
    param(
        [AllowNull()][pscustomobject]$MainFloor,
        [AllowNull()][pscustomobject]$PrFloor,
        [double]$Percent = 0
    )
    if ($null -eq $MainFloor -or $null -eq $PrFloor) { return @() }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("### Low-mutation skip-floor (``--percent $Percent``)")
    $lines.Add('')
    $lines.Add("At ``--percent $Percent`` the workload mutates few cells per tick (always at least one), so **reconcile/diff isolate the O(n) per-tick child skip-walk floor** that higher mutation rates dilute &mdash; ``ChildReconciler`` re-walks every child each tick even when nothing moved. The closer ``--percent`` is to 0, the more this floor _is_ the signal, so a structural-skip optimization shows up cleanly where the headline table above buries it. Δ is the mean paired change with a 95% CI.")
    $lines.Add('')
    $lines.Add('| Metric | `main` (baseline) | This PR | Δ (95% CI) | Status |')
    $lines.Add('|---|--:|--:|--:|:--|')
    foreach ($m in $script:PerfMetricSpec) {
        $bVal = $MainFloor.($m.Key)
        $pVal = $PrFloor.($m.Key)
        $spread = [math]::Max([double]$MainFloor."$($m.Key)Spread", [double]$PrFloor."$($m.Key)Spread")
        $delta = Get-PerfDelta -Baseline $bVal -Candidate $pVal -LowerIsBetter $m.LowerIsBetter -SpreadPct $spread `
            -BaselineSamples $MainFloor."$($m.Key)Samples" -CandidateSamples $PrFloor."$($m.Key)Samples"
        $lines.Add(('| {0} {1} | {2} | {3} | {4} | {5} |' -f `
                $m.Label, $m.Arrow, `
            (Format-PerfNumber $bVal $m.Digits), `
            (Format-PerfNumber $pVal $m.Digits), `
            (Format-PerfDeltaCell $delta), `
            (Get-PerfStatusGlyph $delta.Status)))
    }
    $lines.Add('')
    return $lines.ToArray()
}

function Format-PerfKeyedListSection {
    <#
    .SYNOPSIS
        Render the keyed-list workload table: the four headline metrics measured on
        StressPerf.KeyedList — a ~500-row stably keyed list whose rows are reordered /
        inserted / removed each tick. Empty array when there is nothing to show.
    .DESCRIPTION
        Unlike the positional StocksGrid headline/skip-floor legs (whose cells mutate
        in place by index, always taking ChildReconciler.ReconcilePositional), this is
        a SEPARATE macro workload that drives the child reconciler's KEYED arm
        (ReconcileKeyed → ReconcileKeyedMiddle, the LIS-based minimal-move pass). It is
        the sensitive macro measure for keyed-diff optimizations (keyed-list diff,
        keyed structural-skip) that the StocksGrid workload can never exercise. Reuses
        the same paired-Δ 95% CI machinery (Get-PerfDelta over the index-aligned
        per-run samples) as the headline table. Returns an empty array when either
        aggregate is $null (keyed-list leg disabled, build omitted, or one side
        produced no metrics), so the caller renders nothing.
    .PARAMETER MainKeyed  Aggregated baseline keyed-list metrics (Measure-PerfRuns), or $null.
    .PARAMETER PrKeyed     Aggregated PR-head keyed-list metrics, or $null.
    .PARAMETER Percent     The mutation percent the keyed-list leg ran at (heading / preamble).
    #>
    param(
        [AllowNull()][pscustomobject]$MainKeyed,
        [AllowNull()][pscustomobject]$PrKeyed,
        [double]$Percent = 50
    )
    if ($null -eq $MainKeyed -or $null -eq $PrKeyed) { return @() }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("### Keyed-list workload (``StressPerf.KeyedList``, ``--percent $Percent``)")
    $lines.Add('')
    $lines.Add("A separate macro workload: a ~500-row **stably keyed** list whose rows are reordered / inserted / removed each tick. Because every child carries a key, the child reconciler takes its **keyed arm** (``ReconcileKeyed`` → ``ReconcileKeyedMiddle``, the LIS-based minimal-move pass) instead of the positional re-walk the StocksGrid tables above measure &mdash; so this is the sensitive macro signal for **keyed-diff** work the positional cells can never reach. Same interleaved paired-Δ 95% CI as the headline table.")
    $lines.Add('')
    $lines.Add('| Metric | `main` (baseline) | This PR | Δ (95% CI) | Status |')
    $lines.Add('|---|--:|--:|--:|:--|')
    foreach ($m in $script:PerfMetricSpec) {
        $bVal = $MainKeyed.($m.Key)
        $pVal = $PrKeyed.($m.Key)
        $spread = [math]::Max([double]$MainKeyed."$($m.Key)Spread", [double]$PrKeyed."$($m.Key)Spread")
        $delta = Get-PerfDelta -Baseline $bVal -Candidate $pVal -LowerIsBetter $m.LowerIsBetter -SpreadPct $spread `
            -BaselineSamples $MainKeyed."$($m.Key)Samples" -CandidateSamples $PrKeyed."$($m.Key)Samples"
        $lines.Add(('| {0} {1} | {2} | {3} | {4} | {5} |' -f `
                $m.Label, $m.Arrow, `
            (Format-PerfNumber $bVal $m.Digits), `
            (Format-PerfNumber $pVal $m.Digits), `
            (Format-PerfDeltaCell $delta), `
            (Get-PerfStatusGlyph $delta.Status)))
    }
    $lines.Add('')
    return $lines.ToArray()
}

function Format-PerfComment {
    <#
    .SYNOPSIS
        Render the sticky two-table comparison comment (markdown), prefixed with
        the hidden marker used for update-in-place.
    .PARAMETER Main       Aggregated baseline metrics (Measure-PerfRuns output).
    .PARAMETER Pr         Aggregated PR-head metrics.
    .PARAMETER WinUI3     Aggregated vanilla-WinUI3 (StressPerf.Direct) metrics, or $null.
    .PARAMETER Rust       Aggregated Rust windows-reactor (test_reactor_perf) metrics
                          measured live on this runner, or $null when not run.
    .PARAMETER Micro      Per-bench reconciler micro-suite comparison rows
                          (Get-PerfMicroComparison output), or $null when not run.
    .PARAMETER MainFloor  Aggregated baseline low-mutation skip-floor metrics, or $null.
    .PARAMETER PrFloor    Aggregated PR-head low-mutation skip-floor metrics, or $null.
    .PARAMETER MainKeyed  Aggregated baseline keyed-list workload metrics, or $null.
    .PARAMETER PrKeyed     Aggregated PR-head keyed-list workload metrics, or $null.
    .PARAMETER Context    Hashtable: Percent, Duration, Reps, Warmup, SkipFloorPercent,
                          BaseSha, HeadSha, Runner, Cpu, Cores, MemoryGB, RunUrl,
                          Timestamp, Note.
    #>
    param(
        [Parameter(Mandatory)][pscustomobject]$Main,
        [Parameter(Mandatory)][pscustomobject]$Pr,
        [AllowNull()][pscustomobject]$WinUI3,
        [AllowNull()][pscustomobject]$Rust,
        [AllowNull()][object[]]$Micro,
        [AllowNull()][pscustomobject]$MainFloor,
        [AllowNull()][pscustomobject]$PrFloor,
        [AllowNull()][pscustomobject]$MainKeyed,
        [AllowNull()][pscustomobject]$PrKeyed,
        [Parameter(Mandatory)][hashtable]$Context
    )

    $nl = "`n"
    $lines = [System.Collections.Generic.List[string]]::new()
    $add = { param($t) $lines.Add($t) }

    & $add $script:PerfCommentMarker
    & $add "## $([char]0x26A1) Reactor perf comparison"
    & $add ''
    $plat = if ($Context.ContainsKey('Platform') -and $Context.Platform) { $Context.Platform } else { 'x64' }
    $methodology = "**Workload:** ``StressPerf.ReactorOptimized`` StocksGrid &middot; " +
        "``--percent $($Context.Percent) --duration $($Context.Duration)`` &middot; $plat Release &middot; " +
        "median of $($Context.Reps) paired runs ($($Context.Warmup) warmup dropped); Δ is the mean change with a 95% CI &middot; " +
        "PR head and ``main`` built and run **interleaved on the same runner**."
    & $add $methodology
    & $add ''

    # ── Table 1: regression vs main ──────────────────────────────────────────
    & $add "### Regression vs ``main`` baseline"
    & $add ''
    & $add '| Metric | `main` (baseline) | This PR | Δ (95% CI) | Status |'
    & $add '|---|--:|--:|--:|:--|'
    foreach ($m in $script:PerfMetricSpec) {
        $bVal = $Main.($m.Key)
        $pVal = $Pr.($m.Key)
        $spread = [math]::Max([double]$Main."$($m.Key)Spread", [double]$Pr."$($m.Key)Spread")
        $delta = Get-PerfDelta -Baseline $bVal -Candidate $pVal -LowerIsBetter $m.LowerIsBetter -SpreadPct $spread `
            -BaselineSamples $Main."$($m.Key)Samples" -CandidateSamples $Pr."$($m.Key)Samples"
        $row = '| {0} {1} | {2} | {3} | {4} | {5} |' -f `
            $m.Label, $m.Arrow, `
            (Format-PerfNumber $bVal $m.Digits), `
            (Format-PerfNumber $pVal $m.Digits), `
            (Format-PerfDeltaCell $delta), `
            (Get-PerfStatusGlyph $delta.Status)
        & $add $row
    }
    & $add ''

    # ── Low-mutation skip-floor table (--percent ~0) ─────────────────────────
    # A second interleaved A/B leg at near-zero mutation. With ~1 cell changing per
    # tick, reconcile/diff isolate the O(n) positional child skip-walk floor that the
    # 50%-mutation headline table above dilutes — the signal a structural-skip
    # optimization moves. Rendered only when both floor aggregates are present.
    $floorPct = if ($Context.ContainsKey('SkipFloorPercent')) { [double]$Context.SkipFloorPercent } else { 0 }
    foreach ($fline in (Format-PerfSkipFloorSection -MainFloor $MainFloor -PrFloor $PrFloor -Percent $floorPct)) { & $add $fline }

    # ── Allocation table (Reactor main vs PR; lower is better) ────────────────
    # Rendered only when at least one side reports an allocation metric. A PR head
    # opened before this metric landed reports n/a until rebased onto main.
    $hasAlloc = ($null -ne $Main.AllocBytesPerRender) -or ($null -ne $Pr.AllocBytesPerRender) -or
                ($null -ne $Main.Gen0PerKRenders) -or ($null -ne $Pr.Gen0PerKRenders)
    if ($hasAlloc) {
        & $add "### Allocation (Reactor) &mdash; lower is better"
        & $add ''
        & $add '| Metric | `main` (baseline) | This PR | Δ (95% CI) | Status |'
        & $add '|---|--:|--:|--:|:--|'
        foreach ($m in $script:PerfAllocMetricSpec) {
            $bVal = $Main.($m.Key)
            $pVal = $Pr.($m.Key)
            $spread = [math]::Max([double]$Main."$($m.Key)Spread", [double]$Pr."$($m.Key)Spread")
            $delta = Get-PerfDelta -Baseline $bVal -Candidate $pVal -LowerIsBetter $m.LowerIsBetter -SpreadPct $spread `
                -BaselineSamples $Main."$($m.Key)Samples" -CandidateSamples $Pr."$($m.Key)Samples"
            $row = '| {0} {1} | {2} | {3} | {4} | {5} |' -f `
                $m.Label, $m.Arrow, `
                (Format-PerfNumber $bVal $m.Digits), `
                (Format-PerfNumber $pVal $m.Digits), `
                (Format-PerfDeltaCell $delta), `
                (Get-PerfStatusGlyph $delta.Status)
            & $add $row
        }
        & $add ''
    }

    # ── Keyed-list workload table (StressPerf.KeyedList) ─────────────────────
    # A separate macro workload driving the child reconciler's KEYED arm
    # (ReconcileKeyed → ReconcileKeyedMiddle, the LIS minimal-move pass) that the
    # positional StocksGrid cells above never reach. The sensitive macro signal for
    # keyed-diff optimizations. Rendered only when both keyed aggregates are present.
    $keyedPct = if ($Context.ContainsKey('Percent')) { [double]$Context.Percent } else { 50 }
    foreach ($kline in (Format-PerfKeyedListSection -MainKeyed $MainKeyed -PrKeyed $PrKeyed -Percent $keyedPct)) { & $add $kline }

    # ── Reconciler micro-benchmarks (ns-resolution, WinUI-undiluted) ──────────
    # Rendered only when the PerfBench.ControlModel micro leg produced results for
    # both sides. Resolves Core/Reconciler time + allocation deltas the macro
    # StocksGrid workload above cannot (it is render-bound and working-set diluted).
    foreach ($mline in (Format-PerfMicroSection -Micro $Micro)) { & $add $mline }

    # ── Table 2: cross-framework reference ───────────────────────────────────
    & $add "### Cross-framework reference (same StocksGrid workload)"
    & $add ''
    & $add ('| Metric | vanilla WinUI3{0} | Rust `windows-reactor`{1} | Reactor (this PR) |' -f [char]0x00B9, [char]0x00B2)
    & $add '|---|--:|--:|--:|'
    foreach ($m in $script:PerfMetricSpec) {
        $w = if ($null -ne $WinUI3) { $WinUI3.($m.Key) } else { $null }
        $r = if ($null -ne $Rust) { $Rust.($m.Key) } else { $null }
        $row = '| {0} {1} | {2} | {3} | {4} |' -f `
            $m.Label, $m.Arrow, `
            (Format-PerfNumber $w $m.Digits), `
            (Format-PerfNumber $r $m.Digits), `
            (Format-PerfNumber $Pr.($m.Key) $m.Digits)
        & $add $row
    }
    & $add ''

    # ── Footnotes ────────────────────────────────────────────────────────────
    $up = [char]0x2191; $down = [char]0x2193
    $rustNote = if ($null -ne $Rust) {
        'Built from source and measured **live on this runner**.'
    } else {
        '*Not run* on this runner (Rust toolchain/checkout unavailable or the Rust leg failed) — its cells read *n/a*.'
    }
    & $add "<sub>$up higher is better &middot; $down lower is better. **Within noise** = the 95% confidence interval of the paired Δ includes 0 (no change resolvable at this sample size); $([char]0x2705) improvement / $([char]0x26A0)$([char]0xFE0F) regression require the CI to **exclude** 0.</sub>"
    & $add "<sub>Allocation metrics (alloc bytes/render, Gen0 GC) are the sensitive signal for allocation-reduction work, where the mean-ms / memory figures are largely flat. They read *n/a* for a harness built from a revision that predates them (rebase the PR onto ``main`` to populate them).</sub>"
    if ($null -ne $Micro -and @($Micro).Count -gt 0) {
        & $add "<sub>Reconciler micro-benchmarks run ``PerfBench.ControlModel --variant Reactor`` (M1&ndash;M13) as a headless loop bracketed by per-thread alloc + GC counters &mdash; ns-resolution and free of WinUI render / working-set dilution, so they resolve Core/Reconciler allocation deltas the macro StocksGrid workload cannot. ``main`` and PR each link their own ``src/Reactor`` build; Δ is the paired 95% CI over per-rep means. The **Status** column tracks allocated bytes/op (deterministic for identical code); ns/op is informational &mdash; it is not auto-flagged until the per-side runs are rep-interleaved (a documented fast-follow), because a process-to-process timing offset can otherwise make the paired ns CI exclude 0 for an unchanged diff.</sub>"
    }
    & $add "<sub>$([char]0x00B9) vanilla WinUI3 = ``StressPerf.Direct`` (imperative; no virtual-DOM, so it has no reconcile/diff phase — those cells read *n/a*). Measured live on this runner.</sub>"
    & $add "<sub>$([char]0x00B2) Rust = ``test_reactor_perf`` from [microsoft/windows-rs](https://github.com/microsoft/windows-rs/tree/master/crates/tests/libs/reactor_perf) — a port of this harness (same StocksGrid, same ``--percent``/``--duration`` CLI). $rustNote</sub>"
    & $add "<sub>Absolute numbers are runner-dependent — trust the **Δ vs main**, not the absolute values. Memory (working set) is the noisiest metric.</sub>"

    $ctxBits = @()
    if ($Context.ContainsKey('Cpu') -and $Context.Cpu)       { $ctxBits += "CPU: $($Context.Cpu)" }
    if ($Context.ContainsKey('Cores') -and $Context.Cores)   { $ctxBits += "$($Context.Cores) logical cores" }
    if ($Context.ContainsKey('MemoryGB') -and $Context.MemoryGB) { $ctxBits += "$($Context.MemoryGB) GB RAM" }
    if ($Context.ContainsKey('Runner') -and $Context.Runner) { $ctxBits += "runner: $($Context.Runner)" }
    if ($ctxBits.Count -gt 0) { & $add ("<sub>Runner: " + ($ctxBits -join ' &middot; ') + ".</sub>") }

    $shaBits = @()
    if ($Context.ContainsKey('HeadSha') -and $Context.HeadSha) { $shaBits += "PR ``$($Context.HeadSha)``" }
    if ($Context.ContainsKey('BaseSha') -and $Context.BaseSha) { $shaBits += "main ``$($Context.BaseSha)``" }
    $genLine = "<sub>Generated by ``.github/workflows/perf-compare.yml``"
    if ($shaBits.Count -gt 0) { $genLine += ' &middot; ' + ($shaBits -join ' vs ') }
    if ($Context.ContainsKey('Timestamp') -and $Context.Timestamp) { $genLine += " &middot; $($Context.Timestamp)" }
    if ($Context.ContainsKey('RunUrl') -and $Context.RunUrl) { $genLine += " &middot; [run log]($($Context.RunUrl))" }
    $genLine += '.</sub>'
    & $add $genLine

    if ($Context.ContainsKey('Note') -and $Context.Note) {
        & $add ''
        & $add "> [!NOTE]$nl> $($Context.Note)"
    }

    return ($lines -join $nl)
}
