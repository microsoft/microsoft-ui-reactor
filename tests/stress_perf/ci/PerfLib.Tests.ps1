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


# ── Reconciler micro-suite: Read-MicroBenchResults / comparison / render ──────
function New-MicroRow {
    param([string]$BenchId, [string]$Name, [string]$Variant, [int]$Rep, [double]$MeanNs, [double]$AllocBytes, [string]$Status = 'ok', [int]$Iterations = 1)
    [pscustomobject]@{
        benchId = $BenchId; benchName = $Name; variant = $Variant; iterations = $Iterations
        repetition = $Rep; totalMs = ($MeanNs * $Iterations / 1e6); meanNs = $MeanNs; allocBytes = $AllocBytes
        gen0 = 0; gen1 = 0; gen2 = 0; heapDeltaBytes = 0; counter = 0; counterLabel = $null
        status = $Status; machineSku = 'x'; architecture = 'X64'; configuration = 'Release'
    } | ConvertTo-Json -Compress
}

$microTmp = Join-Path ([System.IO.Path]::GetTempPath()) ("perflib-micro-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $microTmp | Out-Null
try {
    $mainJsonl = Join-Path $microTmp 'main.jsonl'
    $prJsonl = Join-Path $microTmp 'pr.jsonl'

    # main side: M1 (fast/cheap), M2 (mid), M10 (cheap), M99 (main-only). Plus a Direct
    # variant row and a status=error row that MUST be filtered out by the parser.
    $mainLines = @(
        (New-MicroRow 'M1'  'PropertyDiff'      'Reactor' 0 100 520)
        (New-MicroRow 'M1'  'PropertyDiff'      'Reactor' 1 102 525)
        (New-MicroRow 'M1'  'PropertyDiff'      'Reactor' 2 98  515)
        (New-MicroRow 'M1'  'PropertyDiff'      'Direct'  0 999 9999)        # filtered: not Reactor
        (New-MicroRow 'M2'  'StructuralSharing' 'Reactor' 0 200 1000)
        (New-MicroRow 'M2'  'StructuralSharing' 'Reactor' 1 203 1005)
        (New-MicroRow 'M2'  'StructuralSharing' 'Reactor' 2 197 995)
        (New-MicroRow 'M2'  'StructuralSharing' 'Reactor' 9 0   0    'error') # filtered: status error
        (New-MicroRow 'M3'  'TimeOnly'          'Reactor' 0 300 5000)
        (New-MicroRow 'M3'  'TimeOnly'          'Reactor' 1 303 5005)
        (New-MicroRow 'M3'  'TimeOnly'          'Reactor' 2 297 4995)
        (New-MicroRow 'M10' 'Allocation'        'Reactor' 0 50  300)
        (New-MicroRow 'M10' 'Allocation'        'Reactor' 1 51  303)
        (New-MicroRow 'M10' 'Allocation'        'Reactor' 2 49  297)
        (New-MicroRow 'M99' 'MainOnly'          'Reactor' 0 10  100)         # no PR counterpart
    )
    # pr side: M1 faster + fewer allocs; M2 ~unchanged; M3 much faster but SAME allocs
    # (the no-false-flag case); M10 slower + more allocs.
    $prLines = @(
        (New-MicroRow 'M1'  'PropertyDiff'      'Reactor' 0 80  400)
        (New-MicroRow 'M1'  'PropertyDiff'      'Reactor' 1 79  398)
        (New-MicroRow 'M1'  'PropertyDiff'      'Reactor' 2 82  403)
        (New-MicroRow 'M2'  'StructuralSharing' 'Reactor' 0 199 1001)
        (New-MicroRow 'M2'  'StructuralSharing' 'Reactor' 1 204 1003)
        (New-MicroRow 'M2'  'StructuralSharing' 'Reactor' 2 198 998)
        (New-MicroRow 'M3'  'TimeOnly'          'Reactor' 0 200 5004)
        (New-MicroRow 'M3'  'TimeOnly'          'Reactor' 1 201 4998)
        (New-MicroRow 'M3'  'TimeOnly'          'Reactor' 2 199 5002)
        (New-MicroRow 'M10' 'Allocation'        'Reactor' 0 60  360)
        (New-MicroRow 'M10' 'Allocation'        'Reactor' 1 61  363)
        (New-MicroRow 'M10' 'Allocation'        'Reactor' 2 59  357)
    )
    Set-Content -LiteralPath $mainJsonl -Value $mainLines -Encoding UTF8
    Set-Content -LiteralPath $prJsonl -Value $prLines -Encoding UTF8

    $mainMap = Read-MicroBenchResults $mainJsonl
    $prMap = Read-MicroBenchResults $prJsonl

    Assert-Equal 5 $mainMap.Count                          'parser keeps 5 main benches (M1,M2,M3,M10,M99)'
    Assert-Equal 4 $prMap.Count                            'parser keeps 4 pr benches (M1,M2,M3,M10)'
    Assert-Equal 3 $mainMap['M1'].MeanNsSamples.Count      'M1 keeps 3 Reactor reps (Direct row dropped)'
    Assert-Equal 3 $mainMap['M2'].MeanNsSamples.Count      'M2 keeps 3 ok reps (error row dropped)'
    Assert-True  (-not $mainMap.Contains('XX'))            'no phantom benches from filtered rows'
    Assert-Equal 'PropertyDiff' $mainMap['M1'].Name        'bench name carried through'
    # rows ordered by repetition: first sample is rep 0 (=100 / =520).
    Assert-Equal 100 $mainMap['M1'].MeanNsSamples[0]       'samples ordered by repetition (ns)'
    Assert-Equal 520 $mainMap['M1'].AllocBytesSamples[0]   'samples ordered by repetition (alloc)'

    # Missing / empty inputs -> empty map (no throw under StrictMode).
    Assert-Equal 0 (Read-MicroBenchResults (Join-Path $microTmp 'nope.jsonl')).Count 'missing file -> empty map'
    $emptyJsonl = Join-Path $microTmp 'empty.jsonl'; Set-Content -LiteralPath $emptyJsonl -Value '' -Encoding UTF8
    Assert-Equal 0 (Read-MicroBenchResults $emptyJsonl).Count 'empty file -> empty map'
    Assert-Equal 0 (Read-MicroBenchResults $null).Count       'null path -> empty map'

    # AllocBytesSamples are PER-OP: allocBytes (the whole-loop total) is divided by the
    # row's iterations so the "B/op" column and the per-op meanNs share one basis. A row
    # with iterations=10000 and a 1,400,000-byte loop total must parse to 140 B/op.
    $perOpJsonl = Join-Path $microTmp 'perop.jsonl'
    Set-Content -LiteralPath $perOpJsonl -Encoding UTF8 -Value @(
        (New-MicroRow 'M1' 'PropertyDiff' 'Reactor' 0 100 1400000 'ok' 10000)
        (New-MicroRow 'M1' 'PropertyDiff' 'Reactor' 1 100 1400000 'ok' 10000)
    )
    $perOpMap = Read-MicroBenchResults $perOpJsonl
    Assert-Equal 140 $perOpMap['M1'].AllocBytesSamples[0] 'alloc parsed per-op (allocBytes / iterations)'
    Assert-Equal 100 $perOpMap['M1'].MeanNsSamples[0]     'ns already per-op, carried unchanged'

    $cmp = @(Get-PerfMicroComparison -Main $mainMap -Pr $prMap)
    Assert-Equal 4 $cmp.Count                              'comparison = overlapping benches only (M99 excluded)'
    # numeric (not lexical) sort: M1, M2, M3, M10 — lexical would put M10 before M2/M3.
    Assert-Equal 'M1'  $cmp[0].BenchId                     'sorted: M1 first'
    Assert-Equal 'M2'  $cmp[1].BenchId                     'sorted: M2 second'
    Assert-Equal 'M3'  $cmp[2].BenchId                     'sorted: M3 third'
    Assert-Equal 'M10' $cmp[3].BenchId                     'sorted: M10 last (numeric, not lexical)'

    $byId = @{}; foreach ($r in $cmp) { $byId[$r.BenchId] = $r }
    $m1 = $byId['M1']; $m2 = $byId['M2']; $m3 = $byId['M3']; $m10 = $byId['M10']

    # The row flag tracks the DETERMINISTIC alloc signal; ns is informational.
    Assert-Equal 'better' $m1.AllocDelta.Status            'M1 alloc improvement flagged'
    Assert-Equal 'better' (Get-PerfMicroRowStatus -NsStatus $m1.NsDelta.Status -AllocStatus $m1.AllocDelta.Status) 'M1 row = better (alloc down)'
    Assert-Equal 'noise'  $m2.AllocDelta.Status            'M2 alloc within noise'
    Assert-Equal 'noise'  (Get-PerfMicroRowStatus -NsStatus $m2.NsDelta.Status -AllocStatus $m2.AllocDelta.Status) 'M2 row = noise'
    Assert-Equal 'worse'  $m10.AllocDelta.Status           'M10 alloc regression flagged'
    Assert-Equal 'worse'  (Get-PerfMicroRowStatus -NsStatus $m10.NsDelta.Status -AllocStatus $m10.AllocDelta.Status) 'M10 row = worse (alloc up)'

    # KEY v1 property: M3 has a clear ns improvement but UNCHANGED alloc — the ns delta
    # must NOT flag the row (ns is informational until per-side runs are rep-interleaved).
    Assert-Equal 'better' $m3.NsDelta.Status               'M3 ns delta itself reads better (big ns drop)'
    Assert-Equal 'noise'  $m3.AllocDelta.Status            'M3 alloc unchanged -> noise'
    Assert-Equal 'noise'  (Get-PerfMicroRowStatus -NsStatus $m3.NsDelta.Status -AllocStatus $m3.AllocDelta.Status) 'M3 row = noise despite ns better (no ns false-flag)'

    Assert-Equal 100 $m1.MainMeanNs                        'M1 main median ns'
    Assert-Equal 80  $m1.PrMeanNs                          'M1 pr median ns'

    # Empty / null inputs to the comparison.
    Assert-Equal 0 (@(Get-PerfMicroComparison -Main ([ordered]@{}) -Pr $prMap)).Count 'empty main -> no rows'
    Assert-Equal 0 (@(Get-PerfMicroComparison -Main $null -Pr $null)).Count           'null maps -> no rows'

    # Row status tracks the deterministic ALLOC signal only; ns is informational and
    # never drives the flag in v1 (the per-side runs are not yet rep-interleaved).
    Assert-Equal 'worse'  (Get-PerfMicroRowStatus -NsStatus 'better' -AllocStatus 'worse')  'alloc worse -> row worse (ns ignored)'
    Assert-Equal 'better' (Get-PerfMicroRowStatus -NsStatus 'worse'  -AllocStatus 'better') 'alloc better -> row better (ns ignored)'
    Assert-Equal 'noise'  (Get-PerfMicroRowStatus -NsStatus 'better' -AllocStatus 'noise')  'alloc noise -> row noise even when ns better (no ns false-flag)'
    Assert-Equal 'noise'  (Get-PerfMicroRowStatus -NsStatus 'worse'  -AllocStatus 'noise')  'alloc noise -> row noise even when ns worse'
    Assert-Equal 'na'     (Get-PerfMicroRowStatus -NsStatus 'better' -AllocStatus 'na')     'alloc na -> row na (ns does not rescue)'
    Assert-Equal 'na'     (Get-PerfMicroRowStatus -NsStatus 'na'     -AllocStatus 'na')     'na when alloc na'

    # Rep-aligned pairing + consistent medians. Two benches, each with rep1 dropped
    # on the PR side:
    #   A1 — main rep1 is an alloc outlier (2000). Correct rep-keyed alignment pairs
    #        only the common reps {0,2,3} -> a clean -10% alloc Δ; the old position-zip
    #        would pair main rep1's 2000 against pr rep2 and smear the mean.
    #   A2 — main rep1 (unpaired) sits at the ns median position, so the DISPLAYED
    #        median must be taken over the aligned set ({0,2,3} -> 20), not all four
    #        samples ({10,20,30,999} -> 25). Proves the table column and the Δ are
    #        drawn from the same samples.
    $alignMain = @(
        (New-MicroRow 'A1' 'RepAlignDelta'  'Reactor' 0 500 1000)
        (New-MicroRow 'A1' 'RepAlignDelta'  'Reactor' 1 500 2000)
        (New-MicroRow 'A1' 'RepAlignDelta'  'Reactor' 2 500 1000)
        (New-MicroRow 'A1' 'RepAlignDelta'  'Reactor' 3 500 1000)
        (New-MicroRow 'A2' 'RepAlignMedian' 'Reactor' 0 10  500)
        (New-MicroRow 'A2' 'RepAlignMedian' 'Reactor' 1 999 500)
        (New-MicroRow 'A2' 'RepAlignMedian' 'Reactor' 2 20  500)
        (New-MicroRow 'A2' 'RepAlignMedian' 'Reactor' 3 30  500)
    )
    $alignPr = @(
        (New-MicroRow 'A1' 'RepAlignDelta'  'Reactor' 0 500 900)
        (New-MicroRow 'A1' 'RepAlignDelta'  'Reactor' 2 500 900)
        (New-MicroRow 'A1' 'RepAlignDelta'  'Reactor' 3 500 900)
        (New-MicroRow 'A2' 'RepAlignMedian' 'Reactor' 0 10  500)
        (New-MicroRow 'A2' 'RepAlignMedian' 'Reactor' 2 20  500)
        (New-MicroRow 'A2' 'RepAlignMedian' 'Reactor' 3 30  500)
    )
    $alignMainJsonl = Join-Path $microTmp 'align-main.jsonl'
    $alignPrJsonl = Join-Path $microTmp 'align-pr.jsonl'
    Set-Content -LiteralPath $alignMainJsonl -Value $alignMain -Encoding UTF8
    Set-Content -LiteralPath $alignPrJsonl -Value $alignPr -Encoding UTF8
    $alignMainMap = Read-MicroBenchResults $alignMainJsonl
    Assert-Equal 4 $alignMainMap['A1'].Repetitions.Count   'parser captures the repetition index per sample'
    Assert-Equal 1 $alignMainMap['A1'].Repetitions[1]      'repetitions carried in sorted order'
    $alignCmp = @(Get-PerfMicroComparison -Main $alignMainMap -Pr (Read-MicroBenchResults $alignPrJsonl))
    $alignById = @{}; foreach ($r in $alignCmp) { $alignById[$r.BenchId] = $r }
    $a1 = $alignById['A1']; $a2 = $alignById['A2']
    Assert-Equal 3 $a1.AllocDelta.N                        'rep-align: only the 3 common reps paired (not min-count position zip)'
    Assert-True (($a1.AllocDelta.DeltaPct -gt -10.5) -and ($a1.AllocDelta.DeltaPct -lt -9.5)) 'rep-align: paired alloc Δ ≈ -10% (rep-keyed), not smeared by the missing rep'
    Assert-Equal 'better' $a1.AllocDelta.Status            'rep-align: clean -10% alloc flagged better'
    Assert-Equal 1000 $a1.MainAllocBytes                   'rep-align: displayed main alloc median over the aligned set'
    Assert-Equal 900  $a1.PrAllocBytes                     'rep-align: displayed pr alloc median over the aligned set'
    # The unpaired main rep (999) must NOT enter the displayed median.
    Assert-Equal 20 $a2.MainMeanNs                         'consistent medians: main ns median over aligned reps (20), not all-sample (25)'
    Assert-Equal 20 $a2.PrMeanNs                           'consistent medians: pr ns median over aligned reps'
    Assert-Equal 3  $a2.NsDelta.N                          'consistent medians: ns paired over the 3 common reps'

    # Micro paired-CI contract: a bench whose rep-aligned overlap is < 2 pairs has NO
    # admissible CI (Get-PerfPairedDeltaStats needs >= 2 deltas), so the row must read
    # 'na' — never be flagged off the point-delta fallback. Get-PerfMicroComparison
    # passes -RequirePairedCI so Get-PerfDelta returns 'na' instead of the % -floor band.
    #   Z1 — exactly ONE common rep (rep 0); the lone pair is a huge alloc drop the old
    #        point-delta path would have flagged 'better' with N=$null.
    #   Z0 — ZERO common reps (disjoint repetition indices) -> 'na', N stays $null.
    $naMain = @(
        (New-MicroRow 'Z1' 'OneCommonRep' 'Reactor' 0 100 1000)
        (New-MicroRow 'Z1' 'OneCommonRep' 'Reactor' 1 100 1000)
        (New-MicroRow 'Z0' 'NoCommonRep'  'Reactor' 0 100 1000)
        (New-MicroRow 'Z0' 'NoCommonRep'  'Reactor' 1 100 1000)
    )
    $naPr = @(
        (New-MicroRow 'Z1' 'OneCommonRep' 'Reactor' 0 50 100)   # only rep 0 overlaps {0,1}
        (New-MicroRow 'Z1' 'OneCommonRep' 'Reactor' 7 50 100)
        (New-MicroRow 'Z0' 'NoCommonRep'  'Reactor' 8 50 100)   # no overlap with {0,1}
        (New-MicroRow 'Z0' 'NoCommonRep'  'Reactor' 9 50 100)
    )
    $naMainJsonl = Join-Path $microTmp 'na-main.jsonl'
    $naPrJsonl   = Join-Path $microTmp 'na-pr.jsonl'
    Set-Content -LiteralPath $naMainJsonl -Value $naMain -Encoding UTF8
    Set-Content -LiteralPath $naPrJsonl   -Value $naPr   -Encoding UTF8
    $naCmp = @(Get-PerfMicroComparison -Main (Read-MicroBenchResults $naMainJsonl) -Pr (Read-MicroBenchResults $naPrJsonl))
    $naById = @{}; foreach ($r in $naCmp) { $naById[$r.BenchId] = $r }
    $z1 = $naById['Z1']; $z0 = $naById['Z0']
    Assert-Equal 'na' $z1.AllocDelta.Status                'Z1 (1 common rep): alloc na — no point-delta flag off one pair'
    Assert-True ($null -eq $z1.AllocDelta.N)               'Z1 (1 common rep): alloc N stays $null'
    Assert-Equal 'na' $z1.NsDelta.Status                   'Z1 (1 common rep): ns na'
    Assert-Equal 'na' (Get-PerfMicroRowStatus -NsStatus $z1.NsDelta.Status -AllocStatus $z1.AllocDelta.Status) 'Z1 (1 common rep): row na'
    Assert-Equal 'na' $z0.AllocDelta.Status                'Z0 (0 common reps): alloc na'
    Assert-True ($null -eq $z0.AllocDelta.N)               'Z0 (0 common reps): alloc N stays $null'
    Assert-Equal 'na' (Get-PerfMicroRowStatus -NsStatus $z0.NsDelta.Status -AllocStatus $z0.AllocDelta.Status) 'Z0 (0 common reps): row na'

    # Minimum-effect band: not every micro bench is alloc-deterministic. A dispatcher /
    # background-thread bench can carry a sub-1% systematic process-to-process alloc
    # offset whose within-process CI is tight enough to EXCLUDE 0 on identical code (the
    # M5 "Dispatch_Switch_Warm" smoke case). -MinEffectPct keeps such a sub-band delta
    # 'noise' while a real structural delta (>= the band) still flags.
    #   Direct Get-PerfDelta: same tight +0.3% delta is 'worse' at the default band (0)
    #   but 'noise' once a 1% band is required; a large +20% delta flags through both.
    $bandB = @(1000, 1000, 1000, 1000)
    $bandC = @(1003, 1004, 1002, 1003)            # per-pair Δ ≈ +0.2..+0.4%, tight CI excludes 0
    $bandNoFloor = Get-PerfDelta -Baseline 1000 -Candidate 1003 -LowerIsBetter $true `
        -BaselineSamples $bandB -CandidateSamples $bandC -RequirePairedCI
    Assert-Equal 'worse' $bandNoFloor.Status               'band: sub-1% tight delta flags worse at MinEffectPct=0 (CI excludes 0)'
    $bandFloor = Get-PerfDelta -Baseline 1000 -Candidate 1003 -LowerIsBetter $true `
        -BaselineSamples $bandB -CandidateSamples $bandC -RequirePairedCI -MinEffectPct 1.0
    Assert-Equal 'noise' $bandFloor.Status                 'band: same sub-1% delta reads noise once the 1% band is required'
    Assert-True (($bandFloor.DeltaPct -gt 0) -and ($bandFloor.DeltaPct -lt 1)) 'band: DeltaPct still reported (sub-band), only the flag is suppressed'
    $bigB = @(1000, 1000, 1000, 1000)
    $bigC = @(1200, 1201, 1199, 1200)             # +20% — well past any 1% band
    $bandBig = Get-PerfDelta -Baseline 1000 -Candidate 1200 -LowerIsBetter $true `
        -BaselineSamples $bigB -CandidateSamples $bigC -RequirePairedCI -MinEffectPct 1.0
    Assert-Equal 'worse' $bandBig.Status                   'band: a real >1% delta still flags through the band'

    # Integration: the micro comparison passes the alloc band, so a bench with a tight
    # sub-1% alloc rise reads 'noise' at the row level (no false regression), while its
    # ns context is unaffected.
    $bandMain = @(
        (New-MicroRow 'B1' 'BandFloor' 'Reactor' 0 500 1000)
        (New-MicroRow 'B1' 'BandFloor' 'Reactor' 1 500 1000)
        (New-MicroRow 'B1' 'BandFloor' 'Reactor' 2 500 1000)
        (New-MicroRow 'B1' 'BandFloor' 'Reactor' 3 500 1000)
    )
    $bandPr = @(
        (New-MicroRow 'B1' 'BandFloor' 'Reactor' 0 500 1003)
        (New-MicroRow 'B1' 'BandFloor' 'Reactor' 1 500 1004)
        (New-MicroRow 'B1' 'BandFloor' 'Reactor' 2 500 1002)
        (New-MicroRow 'B1' 'BandFloor' 'Reactor' 3 500 1003)
    )
    $bandMainJsonl = Join-Path $microTmp 'band-main.jsonl'
    $bandPrJsonl   = Join-Path $microTmp 'band-pr.jsonl'
    Set-Content -LiteralPath $bandMainJsonl -Value $bandMain -Encoding UTF8
    Set-Content -LiteralPath $bandPrJsonl   -Value $bandPr   -Encoding UTF8
    $bandCmp = @(Get-PerfMicroComparison -Main (Read-MicroBenchResults $bandMainJsonl) -Pr (Read-MicroBenchResults $bandPrJsonl))
    $b1 = $bandCmp[0]
    Assert-Equal 'noise' $b1.AllocDelta.Status             'band integration: sub-1% alloc rise reads noise (not worse) through the micro path'
    Assert-Equal 'noise' (Get-PerfMicroRowStatus -NsStatus $b1.NsDelta.Status -AllocStatus $b1.AllocDelta.Status) 'band integration: row = noise'
    Assert-Equal 4 $b1.AllocDelta.N                        'band integration: all 4 reps paired (band suppresses the flag, not the pairing)'

    # Section rendering.
    Assert-Equal 0 @(Format-PerfMicroSection -Micro $null).Count  'micro section empty when null'
    Assert-Equal 0 @(Format-PerfMicroSection -Micro @()).Count    'micro section empty when no rows'
    $section = Format-PerfMicroSection -Micro $cmp
    $sectionText = $section -join "`n"
    Assert-Match $sectionText 'Reconciler micro-benchmarks'      'section has heading'
    Assert-Match $sectionText 'ns/op'                            'section header names ns/op'
    Assert-Match $sectionText 'B/op'                             'section header names B/op'
    Assert-True ($sectionText.Contains('`M1` PropertyDiff'))     'section row labels bench + name'
    Assert-Match $sectionText '95% CI'                           'section delta cells carry a CI'
    # numeric sort survives into the rendered table (M2 row appears before M10 row).
    Assert-True (($sectionText.IndexOf('`M2`')) -lt ($sectionText.IndexOf('`M10`'))) 'rendered rows keep numeric order'

    # Format-PerfComment threads -Micro through into the comment.
    $microComment = Format-PerfComment -Main $allocMain -Pr $allocPr -WinUI3 $null -Rust $null -Micro $cmp -Context $ctx
    Assert-Match $microComment 'Reconciler micro-benchmarks'     'comment includes micro section when -Micro supplied'
    Assert-Match $microComment 'WinUI-undiluted'                 'comment carries the micro footnote'
    # Omitted / null -Micro -> no micro section (back-compat with existing callers).
    Assert-True (-not ($allocComment -like '*Reconciler micro-benchmarks*')) 'micro section omitted when -Micro not supplied'
}
finally {
    Remove-Item -LiteralPath $microTmp -Recurse -Force -ErrorAction SilentlyContinue
}


if ($script:Fail -gt 0) {
    Write-Host "FAILED: $($script:Fail) / $($script:Pass + $script:Fail) assertions" -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  ✗ $f" -ForegroundColor Red }
    exit 1
}
Write-Host "PASSED: all $($script:Pass) assertions" -ForegroundColor Green
exit 0
