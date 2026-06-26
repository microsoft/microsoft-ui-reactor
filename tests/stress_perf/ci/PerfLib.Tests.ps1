<#
.SYNOPSIS
    Dependency-free unit tests for PerfLib.ps1 (the pure parser/median/delta/
    renderer used by the /perf comparison workflow).

.DESCRIPTION
    Runs headless with no WinUI harness and no external test framework, so it is
    safe on any runner (it is wired into .github/workflows/perf-lib-tests.yml on
    changes under tests/stress_perf/ci/**). Exits non-zero if any assertion fails.

    Run locally:  pwsh tests/stress_perf/ci/PerfLib.Tests.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'PerfLib.ps1')

$script:Pass = 0
$script:Fail = 0
$script:Failures = [System.Collections.Generic.List[string]]::new()

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -eq $Actual) {
        $script:Pass++
    } else {
        $script:Fail++
        $script:Failures.Add("$Message`n    expected: [$Expected]`n    actual:   [$Actual]")
    }
}

function Assert-Null {
    param($Actual, [string]$Message)
    if ($null -eq $Actual) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add("$Message`n    expected: <null>`n    actual:   [$Actual]") }
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if ($Condition) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add($Message) }
}

function Assert-Match {
    param([string]$Haystack, [string]$Needle, [string]$Message)
    if ($Haystack -like "*$Needle*") { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add("$Message`n    missing substring: [$Needle]") }
}

# ── Get-PerfMedian ───────────────────────────────────────────────────────────
Assert-Equal 2   (Get-PerfMedian @(3, 1, 2))        'median odd'
Assert-Equal 2.5 (Get-PerfMedian @(1, 2, 3, 4))     'median even'
Assert-Equal 5   (Get-PerfMedian @(5))              'median single'
Assert-Null      (Get-PerfMedian @())               'median empty -> null'
Assert-Equal 4   (Get-PerfMedian @(4, $null, 4))    'median ignores nulls'

# ── Get-PerfRelativeSpreadPct ────────────────────────────────────────────────
Assert-Equal 0    (Get-PerfRelativeSpreadPct @(10, 10)) 'spread identical -> 0'
Assert-Equal 18.2 (Get-PerfRelativeSpreadPct @(10, 12)) 'spread (2/11)%'
Assert-Equal 0    (Get-PerfRelativeSpreadPct @(7))      'spread single -> 0'
Assert-Equal 0    (Get-PerfRelativeSpreadPct @())       'spread empty -> 0'

# ── ConvertTo-PerfDouble ─────────────────────────────────────────────────────
Assert-Equal 8.7 (ConvertTo-PerfDouble '8.70')  'parse invariant decimal'
Assert-Equal 8.7 (ConvertTo-PerfDouble '8,70')  'parse comma-decimal culture'
Assert-Equal 12  (ConvertTo-PerfDouble '  12 ') 'parse trims whitespace'
Assert-Null      (ConvertTo-PerfDouble '')      'parse empty -> null'
Assert-Null      (ConvertTo-PerfDouble 'abc')   'parse junk -> null'

# ── Get-PerfDelta (direction-aware + noise band) ─────────────────────────────
$d = Get-PerfDelta -Baseline 10 -Candidate 12 -LowerIsBetter $false
Assert-Equal 20      $d.DeltaPct 'higher-better +20% delta'
Assert-Equal 'better' $d.Status  'higher-better improvement status'
Assert-True  $d.Improved         'higher-better improved flag'

$d = Get-PerfDelta -Baseline 10 -Candidate 8 -LowerIsBetter $false
Assert-Equal 'worse' $d.Status 'higher-better regression status'

$d = Get-PerfDelta -Baseline 10 -Candidate 8 -LowerIsBetter $true
Assert-Equal 'better' $d.Status 'lower-better improvement status'

$d = Get-PerfDelta -Baseline 10 -Candidate 12 -LowerIsBetter $true
Assert-Equal 'worse' $d.Status 'lower-better regression status'

$d = Get-PerfDelta -Baseline 10 -Candidate 10.2 -LowerIsBetter $false
Assert-Equal 'noise' $d.Status 'small delta within 4% floor -> noise'

# Spread wider than the 4% floor widens the band, so +20% can still be noise.
$d = Get-PerfDelta -Baseline 10 -Candidate 12 -LowerIsBetter $false -SpreadPct 25
Assert-Equal 'noise' $d.Status 'delta below spread band -> noise'

$d = Get-PerfDelta -Baseline $null -Candidate 12 -LowerIsBetter $false
Assert-Equal 'na' $d.Status 'null baseline -> na'
Assert-Null  $d.DeltaPct    'null baseline -> null delta'

$d = Get-PerfDelta -Baseline 0 -Candidate 12 -LowerIsBetter $false
Assert-Equal 'na' $d.Status 'zero baseline -> na'

# ── Format-PerfNumber / Format-PerfDeltaCell ─────────────────────────────────
Assert-Equal 'n/a'   (Format-PerfNumber $null 1) 'format null -> n/a'
Assert-Equal '8.70'  (Format-PerfNumber 8.7 2)   'format 2 digits invariant'
Assert-Equal '52000' (Format-PerfNumber 52000 0) 'format 0 digits -> clean integer (no trailing dot)'
Assert-Equal '13'    (Format-PerfNumber 12.6 0)  'format 0 digits rounds to integer'
Assert-Equal '+20.0%' (Format-PerfDeltaCell ([pscustomobject]@{ DeltaPct = 20.0 })) 'delta cell signs positive'
Assert-Equal '-5.0%'  (Format-PerfDeltaCell ([pscustomobject]@{ DeltaPct = -5.0 })) 'delta cell signs negative'
Assert-Equal '—'      (Format-PerfDeltaCell ([pscustomobject]@{ DeltaPct = $null })) 'delta cell na -> dash'

# ── Get-PerfStatusGlyph (status color coding) ────────────────────────────────
Assert-Equal "$([char]0x2705) improvement"             (Get-PerfStatusGlyph 'better') 'better -> check + improvement'
Assert-Equal "$([char]0x26A0)$([char]0xFE0F) regression" (Get-PerfStatusGlyph 'worse')  'worse -> warning + regression'
Assert-Equal "$([char]0x2248) within noise"            (Get-PerfStatusGlyph 'noise')  'noise -> approx + within noise'

# ── Read-HarnessMetrics ──────────────────────────────────────────────────────
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("perflib-tests-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp -Force | Out-Null
try {
    # C# Reactor-style report.txt (declarative: has reconcile + diff).
    $csReport = @"
StressPerf.ReactorOptimized report
Total Renders: 1234
Duration: 10.0 s
Avg Reconcile: 5.50 ms
Avg Diff: 2.20 ms
Avg Memory: 210.0 MB
"@
    Set-Content -LiteralPath (Join-Path $tmp 'StressPerf.ReactorOptimized.report.txt') -Value $csReport -Encoding UTF8
    $m = Read-HarnessMetrics -Directory $tmp -AppName 'StressPerf.ReactorOptimized'
    Assert-Equal 'report' $m.Source        'C# report parsed from report.txt'
    Assert-Equal 123.4 $m.RendersPerSec    'C# renders/sec = total/duration'
    Assert-Equal 5.5   $m.AvgReconcileMs   'C# reconcile'
    Assert-Equal 2.2   $m.AvgDiffMs        'C# diff'
    Assert-Equal 210   $m.AvgMemoryMB      'C# memory'

    # Rust port report.txt: indented Avg Diff, "Duration: 10.0s" (no space), and
    # a Renders/sec: line the parser ignores in favour of Total/Duration.
    $rustReport = @"
StressPerf.Reactor (windows-reactor) report
Renders/sec: 8.70
Avg Reconcile: 7.90 ms
  Avg Diff: 7.10 ms
Avg Memory: 190.0 MB
Total Renders: 87
Duration: 10.0s
"@
    Set-Content -LiteralPath (Join-Path $tmp 'StressPerf.Reactor.report.txt') -Value $rustReport -Encoding UTF8
    $r = Read-HarnessMetrics -Directory $tmp -AppName 'StressPerf.Reactor'
    Assert-Equal 'report' $r.Source       'Rust report parsed from report.txt'
    Assert-Equal 8.7  $r.RendersPerSec    'Rust renders/sec = 87/10'
    Assert-Equal 7.9  $r.AvgReconcileMs   'Rust reconcile'
    Assert-Equal 7.1  $r.AvgDiffMs        'Rust diff (indented line still matched)'
    Assert-Equal 190  $r.AvgMemoryMB      'Rust memory'

    # metrics.json takes precedence over report.txt when both exist.
    $json = '{"app":"StressPerf.ReactorOptimized","percent":50,"durationSeconds":10,"rendersPerSec":99.9,"totalRenders":999,"avgReconcileMs":1.1,"avgDiffMs":2.2,"avgMemoryMB":150.5,"avgFps":60,"sampleCount":5}'
    Set-Content -LiteralPath (Join-Path $tmp 'StressPerf.ReactorOptimized.metrics.json') -Value $json -Encoding UTF8
    $j = Read-HarnessMetrics -Directory $tmp -AppName 'StressPerf.ReactorOptimized'
    Assert-Equal 'json' $j.Source         'metrics.json wins over report.txt'
    Assert-Equal 99.9 $j.RendersPerSec    'json renders/sec'
    Assert-Equal 150.5 $j.AvgMemoryMB     'json memory'

    # A metrics.json with a null required field is rejected -> fall back to
    # report.txt, instead of coercing the null to 0 and reporting it.
    $badJson = '{"app":"StressPerf.Partial","percent":50,"durationSeconds":10,"rendersPerSec":null,"totalRenders":999,"avgReconcileMs":1.1,"avgDiffMs":2.2,"avgMemoryMB":150.5}'
    $partialReport = @"
StressPerf.Partial report
Total Renders: 400
Duration: 10.0 s
Avg Reconcile: 5.0 ms
Avg Diff: 3.0 ms
Avg Memory: 180.0 MB
"@
    Set-Content -LiteralPath (Join-Path $tmp 'StressPerf.Partial.metrics.json') -Value $badJson -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $tmp 'StressPerf.Partial.report.txt') -Value $partialReport -Encoding UTF8
    $p = Read-HarnessMetrics -Directory $tmp -AppName 'StressPerf.Partial' -WarningAction SilentlyContinue
    Assert-Equal 'report' $p.Source       'null json field -> fall back to report.txt'
    Assert-Equal 40 $p.RendersPerSec      'fallback renders/sec from report (400/10)'

    # Imperative WinUI3 (StressPerf.Direct): no reconcile/diff lines -> n/a.
    $directReport = @"
StressPerf.Direct report
Total Renders: 500
Duration: 10.0 s
Avg Memory: 205.0 MB
"@
    Set-Content -LiteralPath (Join-Path $tmp 'StressPerf.Direct.report.txt') -Value $directReport -Encoding UTF8
    $w = Read-HarnessMetrics -Directory $tmp -AppName 'StressPerf.Direct'
    Assert-Equal 50 $w.RendersPerSec      'Direct renders/sec'
    Assert-Null  $w.AvgReconcileMs        'Direct reconcile -> n/a'
    Assert-Null  $w.AvgDiffMs             'Direct diff -> n/a'

    # Nothing on disk -> source 'none'.
    $none = Read-HarnessMetrics -Directory $tmp -AppName 'Nope.Missing'
    Assert-Equal 'none' $none.Source      'missing files -> none'
}
finally {
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# ── Format-PerfComment (renderer smoke) ──────────────────────────────────────
$mainRuns = @(
    [pscustomobject]@{ RendersPerSec = 10; AvgReconcileMs = 5; AvgDiffMs = 2; AvgMemoryMB = 200; TotalRenders = 100; DurationSeconds = 10 }
    [pscustomobject]@{ RendersPerSec = 10; AvgReconcileMs = 5; AvgDiffMs = 2; AvgMemoryMB = 200; TotalRenders = 100; DurationSeconds = 10 }
)
$prRuns = @(
    [pscustomobject]@{ RendersPerSec = 12; AvgReconcileMs = 4; AvgDiffMs = 1.8; AvgMemoryMB = 195; TotalRenders = 120; DurationSeconds = 10 }
    [pscustomobject]@{ RendersPerSec = 12; AvgReconcileMs = 4; AvgDiffMs = 1.8; AvgMemoryMB = 195; TotalRenders = 120; DurationSeconds = 10 }
)
$main = Measure-PerfRuns -Runs $mainRuns
$pr = Measure-PerfRuns -Runs $prRuns
$winui3 = Measure-PerfRuns -Runs @([pscustomobject]@{ RendersPerSec = 9; AvgReconcileMs = $null; AvgDiffMs = $null; AvgMemoryMB = 220; TotalRenders = 90; DurationSeconds = 10 })
$rust = Measure-PerfRuns -Runs @([pscustomobject]@{ RendersPerSec = 8.5; AvgReconcileMs = 7.5; AvgDiffMs = 6.9; AvgMemoryMB = 188; TotalRenders = 85; DurationSeconds = 10 })
$ctx = @{ Percent = 50; Duration = 10; Reps = 2; Warmup = 1; HeadSha = 'abcdef1234'; BaseSha = '1234567890'; Cpu = 'Test CPU'; Cores = 4; MemoryGB = 16; Timestamp = '2025-01-01T00:00:00Z' }

# With a live Rust measurement.
$comment = Format-PerfComment -Main $main -Pr $pr -WinUI3 $winui3 -Rust $rust -Context $ctx
Assert-Match $comment '<!-- reactor-perf-compare -->' 'comment carries the sticky marker'
Assert-Match $comment 'Regression vs'                 'comment has regression table'
Assert-Match $comment 'Cross-framework reference'     'comment has cross-framework table'
Assert-Match $comment 'live on this runner'           'rust footnote = measured live'
Assert-Match $comment 'improvement'                   'renders/sec improvement glyph present'
Assert-Match $comment 'x64 Release'                   'methodology falls back to x64 when Platform absent'

# Platform threads through to the methodology line (and the missing-key fallback
# above must not throw under Set-StrictMode -Version Latest).
$armCtx = $ctx.Clone(); $armCtx['Platform'] = 'ARM64'
$armComment = Format-PerfComment -Main $main -Pr $pr -WinUI3 $null -Rust $null -Context $armCtx
Assert-Match $armComment 'ARM64 Release' 'methodology reflects -Platform ARM64'

# Rust absent -> column n/a, footnote says not run, and it must not throw.
$noRust = Format-PerfComment -Main $main -Pr $pr -WinUI3 $null -Rust $null -Context $ctx
Assert-Match $noRust 'Not run'  'rust footnote = not run when null'
Assert-Match $noRust 'n/a'      'n/a cells rendered when winui3 + rust null'

# ── Get-StudentTCritical (95% two-sided t-table) ─────────────────────────────
Assert-Equal 12.706 (Get-StudentTCritical -Df 1)  't critical df=1'
Assert-Equal 2.201  (Get-StudentTCritical -Df 11) 't critical df=11'
Assert-Equal 2.042  (Get-StudentTCritical -Df 30) 't critical df=30 (table edge)'
Assert-Equal 2.021  (Get-StudentTCritical -Df 45) 't critical df 41..45 -> t(40)=2.021 conservative'
Assert-Equal 1.99   (Get-StudentTCritical -Df 90) 't critical df 81..90 -> t(80)=1.990 conservative'
Assert-Equal 1.968  (Get-StudentTCritical -Df 500) 't critical df 301..500 -> t(300)=1.968 conservative'
Assert-True ([double]::IsNaN((Get-StudentTCritical -Df 0))) 't critical df<1 -> NaN'
# Conservatism property: returned value must be >= the true two-sided 95% t critical.
Assert-True ((Get-StudentTCritical -Df 45)  -ge 2.0141) 't(45) >= true 2.0141 (never understated)'
Assert-True ((Get-StudentTCritical -Df 90)  -ge 1.9867) 't(90) >= true 1.9867 (never understated)'
Assert-True ((Get-StudentTCritical -Df 500) -ge 1.9647) 't(500) >= true 1.9647 (never understated)'
Assert-True ((Get-StudentTCritical -Df 5000) -ge 1.96)  't(5000) >= z 1.960 (never understated)'

# ── Get-PerfPairedDeltaStats (paired CI over per-iteration deltas) ────────────
# A clear, low-variance ~5% reduction: the 95% CI must exclude 0.
$bImp = @(100, 102, 98, 101, 99, 100, 103, 97, 99, 101)
$cImp = @(95,  96,  93, 95,  94, 95,  97,  92, 94, 96)
$sImp = Get-PerfPairedDeltaStats -BaselineSamples $bImp -CandidateSamples $cImp
Assert-Equal 10 $sImp.N                          'paired N = 10 valid pairs'
Assert-True ($sImp.MeanPct -lt 0)                'paired mean delta is negative (improvement)'
Assert-True ($sImp.CiHighPct -lt 0)              'paired improvement CI excludes 0 (high < 0)'

# Pure jitter around the baseline: the CI must straddle 0.
$bN = @(100, 102, 98, 101, 99, 100, 103, 97, 99, 101)
$cN = @(101, 99, 100, 98, 102, 99, 101, 100, 98, 102)
$sN = Get-PerfPairedDeltaStats -BaselineSamples $bN -CandidateSamples $cN
Assert-True (($sN.CiLowPct -lt 0) -and ($sN.CiHighPct -gt 0)) 'noise CI includes 0'

# Nulls (a dropped/failed run on either side) are skipped, not zipped as zero.
$bNull = @(100, $null, 98, 101)
$cNull = @(95, 96, $null, 95)
$sNull = Get-PerfPairedDeltaStats -BaselineSamples $bNull -CandidateSamples $cNull
Assert-Equal 2 $sNull.N                          'paired stats skips null-containing pairs (2 valid)'

# Fewer than 2 valid pairs -> null (a CI needs >= 2).
Assert-Null (Get-PerfPairedDeltaStats -BaselineSamples @(100) -CandidateSamples @(95)) 'one pair -> null'
Assert-Null (Get-PerfPairedDeltaStats -BaselineSamples $null -CandidateSamples $null)  'null samples -> null'
Assert-Null (Get-PerfPairedDeltaStats -BaselineSamples @(0, 0) -CandidateSamples @(5, 5)) 'zero baselines skipped -> null'

# Stats are returned at FULL precision (not pre-rounded to 2dp): the per-pair
# delta here is exactly -0.999%, which 2dp rounding would have collapsed to -1.00.
$bPrec = @(100, 100, 100, 100)
$cPrec = @(99.001, 99.001, 99.001, 99.001)
$sPrec = Get-PerfPairedDeltaStats -BaselineSamples $bPrec -CandidateSamples $cPrec
Assert-True ([math]::Abs([double]$sPrec.MeanPct - (-0.999)) -lt 1e-9) 'stats keep full precision (mean not rounded to 2dp)'

# ── Get-PerfDelta with samples (CI-excludes-0 replaces the 4% floor) ──────────
# A ~2% improvement that the OLD 4% floor would have buried as "noise", now
# resolved as a real improvement because the paired CI excludes 0.
$bSmall = @(100, 100.5, 99.5, 100, 100.2, 99.8, 100.1, 99.9, 100.3, 99.7)
$cSmall = @(98,  98.4,  97.6, 98,  98.2,  97.8, 98.1,  97.9, 98.3,  97.7)
$dSmall = Get-PerfDelta -Baseline 100 -Candidate 98 -LowerIsBetter $true -BaselineSamples $bSmall -CandidateSamples $cSmall
Assert-Equal 'better' $dSmall.Status   'sub-4% improvement resolved via CI (was noise under floor)'
Assert-True  ($null -ne $dSmall.CiLowPct) 'sample delta carries CiLowPct'
Assert-Equal 10 $dSmall.N              'sample delta carries N'

# Genuine jitter with samples -> noise even though the point delta is non-trivial.
$dNoise = Get-PerfDelta -Baseline 100 -Candidate 100 -LowerIsBetter $true -BaselineSamples $bN -CandidateSamples $cN
Assert-Equal 'noise' $dNoise.Status    'paired jitter CI includes 0 -> noise'

# Higher-is-better regression caught when the CI excludes 0.
$bRps = @(3.6, 3.58, 3.62, 3.59, 3.61, 3.57, 3.6, 3.63, 3.58, 3.6)
$cRps = @(3.4, 3.38, 3.42, 3.39, 3.41, 3.37, 3.4, 3.43, 3.38, 3.4)
$dReg = Get-PerfDelta -Baseline 3.6 -Candidate 3.4 -LowerIsBetter $false -BaselineSamples $bRps -CandidateSamples $cRps
Assert-Equal 'worse' $dReg.Status      'higher-better paired regression (CI excludes 0)'

# Back-compat: with < 2 usable pairs it falls back to the scalar floor path.
$dFallback = Get-PerfDelta -Baseline 100 -Candidate 95 -LowerIsBetter $true -BaselineSamples @(100) -CandidateSamples @(95)
Assert-Equal 'better' $dFallback.Status 'insufficient samples -> floor fallback still flags'
Assert-Null  $dFallback.CiLowPct        'fallback path has null CI'

# Full-precision gating: a CI strictly just above 0 (here +0.004%) must be flagged,
# not collapsed to "noise" by 2dp rounding of the bound to 0.00. Under the old
# pre-rounded stats this returned 'noise'; the full-precision decision returns it.
$bTiny = @(100, 100, 100, 100)
$cTiny = @(100.004, 100.004, 100.004, 100.004)
$dTiny = Get-PerfDelta -Baseline 100 -Candidate 100.004 -LowerIsBetter $false -BaselineSamples $bTiny -CandidateSamples $cTiny
Assert-Equal 'better' $dTiny.Status 'CI just above 0 (+0.004) flagged, not rounded to noise'

# ── Format-PerfDeltaCell with CI ─────────────────────────────────────────────
$cellCi = Format-PerfDeltaCell ([pscustomobject]@{ DeltaPct = -3.4; CiLowPct = -6.1; CiHighPct = -0.6 })
Assert-Match $cellCi '-3.4%'            'CI cell shows point delta'
Assert-Match $cellCi '95% CI'            'CI cell shows the interval label'
Assert-Match $cellCi '-6.1, -0.6'        'CI cell shows the interval bounds'
# Legacy callers passing only DeltaPct must still render a bare percentage.
Assert-Equal '+20.0%' (Format-PerfDeltaCell ([pscustomobject]@{ DeltaPct = 20.0 })) 'no-CI cell stays bare'

# ── Measure-PerfRuns carries ordered per-run samples (incl. alloc keys) ───────
$runsForSamples = @(
    [pscustomobject]@{ RendersPerSec = 10; AvgReconcileMs = 5; AvgDiffMs = 2; AvgMemoryMB = 200; AllocBytesPerRender = 52000; Gen0PerKRenders = 4.1; Gen0 = 10; Gen1 = 2; Gen2 = 0; TotalRenders = 100; DurationSeconds = 10 }
    [pscustomobject]@{ RendersPerSec = 12; AvgReconcileMs = 4; AvgDiffMs = 1.8; AvgMemoryMB = 195; AllocBytesPerRender = 48000; Gen0PerKRenders = 3.8; Gen0 = 9; Gen1 = 2; Gen2 = 0; TotalRenders = 120; DurationSeconds = 10 }
)
$aggS = Measure-PerfRuns -Runs $runsForSamples
Assert-Equal 2 $aggS.RendersPerSecSamples.Count        'samples array preserves per-run order/count'
Assert-Equal 10 $aggS.RendersPerSecSamples[0]          'samples array keeps first run value'
Assert-Equal 52000 $aggS.AllocBytesPerRenderSamples[0] 'alloc samples captured'
Assert-Equal 50000 $aggS.AllocBytesPerRender           'alloc median across runs'
# A run object lacking the alloc key (legacy harness) -> null placeholder, not a throw.
$aggMixed = Measure-PerfRuns -Runs @(
    [pscustomobject]@{ RendersPerSec = 10; AvgReconcileMs = 5; AvgDiffMs = 2; AvgMemoryMB = 200; TotalRenders = 100; DurationSeconds = 10 }
)
Assert-Null $aggMixed.AllocBytesPerRender               'absent alloc key -> null median (no throw under StrictMode)'
Assert-Null $aggMixed.AllocBytesPerRenderSamples[0]     'absent alloc key -> null sample placeholder'

# ── Read-HarnessMetrics parses the new allocation fields ─────────────────────
$tmp2 = Join-Path ([IO.Path]::GetTempPath()) ("perflib-alloc-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp2 -Force | Out-Null
try {
    # report.txt with the alloc lines PerfTracker now emits.
    $allocReport = @"
StressPerf.ReactorOptimized report
Total Renders: 1000
Duration: 10.0 s
Avg Reconcile: 5.50 ms
Avg Diff: 2.20 ms
Avg Memory: 210.0 MB
Alloc/render: 51234 bytes
GC Gen0/1/2: 12 / 3 / 1
Gen0/Krender: 4.07
"@
    Set-Content -LiteralPath (Join-Path $tmp2 'StressPerf.ReactorOptimized.report.txt') -Value $allocReport -Encoding UTF8
    $am = Read-HarnessMetrics -Directory $tmp2 -AppName 'StressPerf.ReactorOptimized'
    Assert-Equal 51234 $am.AllocBytesPerRender 'report alloc bytes/render parsed'
    Assert-Equal 4.07  $am.Gen0PerKRenders     'report Gen0/Krender parsed'
    Assert-Equal 12    $am.Gen0                'report Gen0 count parsed'
    Assert-Equal 1     $am.Gen2                'report Gen2 count parsed'

    # metrics.json with the new alloc fields wins and is parsed.
    $allocJson = '{"app":"StressPerf.ReactorOptimized","percent":50,"durationSeconds":10,"rendersPerSec":99.9,"totalRenders":999,"avgReconcileMs":1.1,"avgDiffMs":2.2,"avgMemoryMB":150.5,"allocBytesPerRender":47777,"gen0":9,"gen1":2,"gen2":0,"gen0PerKRenders":3.55,"avgFps":60,"sampleCount":5}'
    Set-Content -LiteralPath (Join-Path $tmp2 'StressPerf.ReactorOptimized.metrics.json') -Value $allocJson -Encoding UTF8
    $aj = Read-HarnessMetrics -Directory $tmp2 -AppName 'StressPerf.ReactorOptimized'
    Assert-Equal 'json' $aj.Source             'json wins over report'
    Assert-Equal 47777 $aj.AllocBytesPerRender 'json alloc bytes/render parsed'
    Assert-Equal 3.55  $aj.Gen0PerKRenders     'json Gen0/Krender parsed'

    # Back-compat: a metrics.json WITHOUT alloc fields still parses, alloc -> null.
    $oldJson = '{"app":"StressPerf.Old","percent":50,"durationSeconds":10,"rendersPerSec":50,"totalRenders":500,"avgReconcileMs":3,"avgDiffMs":1,"avgMemoryMB":180,"avgFps":60,"sampleCount":5}'
    Set-Content -LiteralPath (Join-Path $tmp2 'StressPerf.Old.metrics.json') -Value $oldJson -Encoding UTF8
    $oj = Read-HarnessMetrics -Directory $tmp2 -AppName 'StressPerf.Old'
    Assert-Equal 'json' $oj.Source             'legacy json (no alloc) still parses'
    Assert-Null  $oj.AllocBytesPerRender       'legacy json -> alloc n/a (null)'
}
finally {
    Remove-Item -LiteralPath $tmp2 -Recurse -Force -ErrorAction SilentlyContinue
}

# ── Format-PerfComment: allocation table + CI header + CI-based footnote ──────
$allocMain = Measure-PerfRuns -Runs @(
    [pscustomobject]@{ RendersPerSec = 3.55; AvgReconcileMs = 76; AvgDiffMs = 66; AvgMemoryMB = 303; AllocBytesPerRender = 52000; Gen0PerKRenders = 4.10; Gen0 = 10; Gen1 = 2; Gen2 = 0; TotalRenders = 35; DurationSeconds = 10 }
    [pscustomobject]@{ RendersPerSec = 3.56; AvgReconcileMs = 76; AvgDiffMs = 66; AvgMemoryMB = 303; AllocBytesPerRender = 52100; Gen0PerKRenders = 4.12; Gen0 = 10; Gen1 = 2; Gen2 = 0; TotalRenders = 35; DurationSeconds = 10 }
    [pscustomobject]@{ RendersPerSec = 3.54; AvgReconcileMs = 76; AvgDiffMs = 66; AvgMemoryMB = 303; AllocBytesPerRender = 51900; Gen0PerKRenders = 4.08; Gen0 = 10; Gen1 = 2; Gen2 = 0; TotalRenders = 35; DurationSeconds = 10 }
)
$allocPr = Measure-PerfRuns -Runs @(
    [pscustomobject]@{ RendersPerSec = 3.55; AvgReconcileMs = 76; AvgDiffMs = 66; AvgMemoryMB = 303; AllocBytesPerRender = 48000; Gen0PerKRenders = 3.80; Gen0 = 9; Gen1 = 2; Gen2 = 0; TotalRenders = 35; DurationSeconds = 10 }
    [pscustomobject]@{ RendersPerSec = 3.56; AvgReconcileMs = 76; AvgDiffMs = 66; AvgMemoryMB = 303; AllocBytesPerRender = 48100; Gen0PerKRenders = 3.82; Gen0 = 9; Gen1 = 2; Gen2 = 0; TotalRenders = 35; DurationSeconds = 10 }
    [pscustomobject]@{ RendersPerSec = 3.54; AvgReconcileMs = 76; AvgDiffMs = 66; AvgMemoryMB = 303; AllocBytesPerRender = 47900; Gen0PerKRenders = 3.78; Gen0 = 9; Gen1 = 2; Gen2 = 0; TotalRenders = 35; DurationSeconds = 10 }
)
$allocComment = Format-PerfComment -Main $allocMain -Pr $allocPr -WinUI3 $null -Rust $null -Context $ctx
Assert-Match $allocComment 'Allocation (Reactor)'  'comment renders the allocation table when alloc present'
Assert-Match $allocComment 'Alloc bytes/render'    'alloc table has bytes/render row'
Assert-Match $allocComment 'Gen0 GC / 1k renders'  'alloc table has Gen0 row'
Assert-Match $allocComment '95% CI'                'delta cells carry a 95% CI'
Assert-Match $allocComment 'confidence interval of the paired'  'footnote describes the CI-based noise rule'

# Alloc table is omitted when neither side reports allocation metrics (legacy heads).
$noAllocComment = Format-PerfComment -Main $main -Pr $pr -WinUI3 $null -Rust $null -Context $ctx
Assert-True (-not ($noAllocComment -like '*Allocation (Reactor)*')) 'alloc table omitted when no alloc metric present'


if ($script:Fail -gt 0) {
    Write-Host "FAILED: $($script:Fail) / $($script:Pass + $script:Fail) assertions" -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  ✗ $f" -ForegroundColor Red }
    exit 1
}
Write-Host "PASSED: all $($script:Pass) assertions" -ForegroundColor Green
exit 0
