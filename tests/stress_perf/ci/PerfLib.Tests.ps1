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


# ── Format-PerfSkipFloorSection + Format-PerfComment: low-mutation skip-floor ──
# 12 paired floor runs. rps, reconcile, and diff all move DOWN main->PR (memory is
# held flat as a within-noise control), but the verdicts must differ BY DIRECTION:
# rps (higher-better) going down is a regression, while
# reconcile/diff (lower-better) going down are improvements — locking that the
# section is direction-aware per metric (reusing Table 1's paired-CI machinery),
# not hard-coded to one direction. Small jitter keeps each paired CI off 0.
$floorMainRuns = @(); $floorPrRuns = @()
1..12 | ForEach-Object {
    $j = ($_ % 4) * 0.04
    $floorMainRuns += [pscustomobject]@{ RendersPerSec = 6.0 + $j; AvgReconcileMs = 14.0 + $j; AvgDiffMs = 12.0 + $j; AvgMemoryMB = 300 + $j; TotalRenders = 60; DurationSeconds = 10 }
    $floorPrRuns   += [pscustomobject]@{ RendersPerSec = 5.0 + $j; AvgReconcileMs = 11.0 + $j; AvgDiffMs = 10.0 + $j; AvgMemoryMB = 300 + $j; TotalRenders = 50; DurationSeconds = 10 }
}
$floorMain = Measure-PerfRuns -Runs $floorMainRuns
$floorPr   = Measure-PerfRuns -Runs $floorPrRuns

# Direct section renderer: empty when either side is null, populated when both present.
Assert-Equal 0 @(Format-PerfSkipFloorSection -MainFloor $null -PrFloor $floorPr -Percent 0).Count 'skip-floor section empty when main floor null'
Assert-Equal 0 @(Format-PerfSkipFloorSection -MainFloor $floorMain -PrFloor $null -Percent 0).Count 'skip-floor section empty when pr floor null'
$floorSection = Format-PerfSkipFloorSection -MainFloor $floorMain -PrFloor $floorPr -Percent 0
$floorSectionText = $floorSection -join "`n"
Assert-Match $floorSectionText 'Low-mutation skip-floor' 'skip-floor section has heading'
Assert-Match $floorSectionText 'Avg Reconcile'           'skip-floor section has reconcile row'
Assert-Match $floorSectionText 'skip-walk floor'         'skip-floor preamble explains the O(n) skip-walk floor'
# Direction-awareness: rps and reconcile both DECREASE main->PR, yet rps (higher-is-
# better) must read regression while reconcile (lower-is-better) reads improvement.
$floorRpsRow   = ($floorSection | Where-Object { $_ -match 'Renders/sec' })   -join ' '
$floorReconRow = ($floorSection | Where-Object { $_ -match 'Avg Reconcile' }) -join ' '
Assert-Match $floorRpsRow   'regression'  'skip-floor: rps DOWN reads regression (higher-is-better honored)'
Assert-Match $floorReconRow 'improvement' 'skip-floor: reconcile DOWN reads improvement (lower-is-better honored)'

# Threaded through Format-PerfComment, between the regression and cross-framework tables.
$floorComment = Format-PerfComment -Main $main -Pr $pr -WinUI3 $null -Rust $null -MainFloor $floorMain -PrFloor $floorPr -Context $ctx
Assert-Match $floorComment 'Low-mutation skip-floor' 'comment renders skip-floor table when floor aggregates present'
$idxReg = $floorComment.IndexOf('Regression vs')
$idxFloor = $floorComment.IndexOf('Low-mutation skip-floor')
$idxXfw = $floorComment.IndexOf('Cross-framework reference')
Assert-True (($idxReg -lt $idxFloor) -and ($idxFloor -lt $idxXfw)) 'skip-floor table sits between the regression and cross-framework tables'

# Omitted entirely when floor aggregates are absent (skip-floor leg disabled).
$noFloorComment = Format-PerfComment -Main $main -Pr $pr -WinUI3 $null -Rust $null -MainFloor $null -PrFloor $null -Context $ctx
Assert-True (-not ($noFloorComment -like '*Low-mutation skip-floor*')) 'skip-floor table omitted when floor aggregates null'

# Context.SkipFloorPercent threads into the heading (default 0 when absent).
$ctxFloor1 = $ctx.Clone(); $ctxFloor1['SkipFloorPercent'] = 1
$floorComment1 = Format-PerfComment -Main $main -Pr $pr -WinUI3 $null -Rust $null -MainFloor $floorMain -PrFloor $floorPr -Context $ctxFloor1
Assert-Match $floorComment1 '--percent 1' 'skip-floor heading reflects Context.SkipFloorPercent'


# ── Format-PerfKeyedListSection + Format-PerfComment: keyed-list workload ──────
# 12 paired keyed-list runs exercising ALL FOUR headline metrics by direction AND by
# significance: rps/reconcile/diff move DOWN main->PR, while memory carries a small
# SYMMETRIC per-pair jitter (mean Δ ~0). So the verdicts must split: rps (higher-
# better) DOWN = regression; reconcile/diff (lower-better) DOWN = improvement; memory's
# paired CI straddles 0 = within noise — proving the keyed section reuses Table 1's
# direction-aware paired-CI machinery, not a hard-coded verdict. The small jitter on
# the directional metrics keeps each of their paired CIs off 0.
$keyedMainRuns = @(); $keyedPrRuns = @()
1..12 | ForEach-Object {
    $j = ($_ % 4) * 0.05
    $mj = ((($_ % 2) * 2) - 1) * 0.2  # alternating +0.2 / -0.2 so the paired memory Δ straddles 0
    $keyedMainRuns += [pscustomobject]@{ RendersPerSec = 8.0 + $j; AvgReconcileMs = 9.0 + $j; AvgDiffMs = 7.0 + $j; AvgMemoryMB = 250 + $mj; TotalRenders = 80; DurationSeconds = 10 }
    $keyedPrRuns   += [pscustomobject]@{ RendersPerSec = 7.0 + $j; AvgReconcileMs = 7.0 + $j; AvgDiffMs = 5.0 + $j; AvgMemoryMB = 250 - $mj; TotalRenders = 70; DurationSeconds = 10 }
}
$keyedMain = Measure-PerfRuns -Runs $keyedMainRuns
$keyedPr   = Measure-PerfRuns -Runs $keyedPrRuns

# Direct section renderer: empty when either side is null, populated when both present.
Assert-Equal 0 @(Format-PerfKeyedListSection -MainKeyed $null -PrKeyed $keyedPr -Percent 50).Count 'keyed section empty when main keyed null'
Assert-Equal 0 @(Format-PerfKeyedListSection -MainKeyed $keyedMain -PrKeyed $null -Percent 50).Count 'keyed section empty when pr keyed null'
$keyedSection = Format-PerfKeyedListSection -MainKeyed $keyedMain -PrKeyed $keyedPr -Percent 50
$keyedSectionText = $keyedSection -join "`n"
Assert-Match $keyedSectionText 'Keyed-list workload'    'keyed section has heading'
Assert-Match $keyedSectionText 'StressPerf.KeyedList'   'keyed heading names the workload'
Assert-Match $keyedSectionText 'Avg Reconcile'          'keyed section has reconcile row'
Assert-Match $keyedSectionText 'keyed arm'              'keyed preamble explains the keyed arm'
Assert-Match $keyedSectionText 'LIS'                    'keyed preamble cites the LIS minimal-move pass'
# Direction-awareness: rps and reconcile both DECREASE main->PR, yet rps (higher-is-
# better) must read regression while reconcile (lower-is-better) reads improvement.
$keyedRpsRow   = ($keyedSection | Where-Object { $_ -match 'Renders/sec' })   -join ' '
$keyedReconRow = ($keyedSection | Where-Object { $_ -match 'Avg Reconcile' }) -join ' '
$keyedDiffRow  = ($keyedSection | Where-Object { $_ -match 'Avg Diff' })      -join ' '
$keyedMemRow   = ($keyedSection | Where-Object { $_ -match 'Avg Memory' })    -join ' '
Assert-Match $keyedRpsRow   'regression'  'keyed: rps DOWN reads regression (higher-is-better honored)'
Assert-Match $keyedReconRow 'improvement' 'keyed: reconcile DOWN reads improvement (lower-is-better honored)'
Assert-Match $keyedDiffRow  'improvement' 'keyed: diff DOWN reads improvement (lower-is-better honored)'
Assert-Match $keyedMemRow   'within noise' 'keyed: symmetric memory Δ reads within noise (paired CI straddles 0)'
# -Percent threads into the heading independently of the methodology line.
$keyedSection75 = (Format-PerfKeyedListSection -MainKeyed $keyedMain -PrKeyed $keyedPr -Percent 75) -join "`n"
Assert-Match $keyedSection75 'Keyed-list workload*--percent 75' 'keyed heading reflects the -Percent argument'

# Threaded through Format-PerfComment: present when keyed aggregates present, sitting
# after the regression/skip-floor tables and before the cross-framework table.
$keyedComment = Format-PerfComment -Main $main -Pr $pr -WinUI3 $null -Rust $null -MainFloor $floorMain -PrFloor $floorPr -MainKeyed $keyedMain -PrKeyed $keyedPr -Context $ctx
Assert-Match $keyedComment 'Keyed-list workload' 'comment renders keyed-list table when keyed aggregates present'
$idxRegK   = $keyedComment.IndexOf('Regression vs')
$idxFloorK = $keyedComment.IndexOf('Low-mutation skip-floor')
$idxKeyed  = $keyedComment.IndexOf('Keyed-list workload')
$idxXfwK   = $keyedComment.IndexOf('Cross-framework reference')
Assert-True (($idxRegK -lt $idxKeyed) -and ($idxFloorK -lt $idxKeyed) -and ($idxKeyed -lt $idxXfwK)) 'keyed-list table sits after the regression + skip-floor tables and before cross-framework'

# Omitted entirely when keyed aggregates are absent (keyed-list leg disabled / build omitted).
$noKeyedComment = Format-PerfComment -Main $main -Pr $pr -WinUI3 $null -Rust $null -MainKeyed $null -PrKeyed $null -Context $ctx
Assert-True (-not ($noKeyedComment -like '*Keyed-list workload*')) 'keyed-list table omitted when keyed aggregates null'


# ── Keyed-list allocation sub-table: shared PerfAllocMetricSpec over keyed aggregates ──
# The keyed leg also renders an allocation sub-table (alloc bytes/render + Gen0 GC / 1k
# renders) — the macro signal for keyed-DIFF allocation reductions. Alloc moves DOWN
# main->PR (~20%, an improvement on a lower-is-better metric); tiny jitter keeps each
# paired CI off 0. Magnitudes mirror a real StressPerf.KeyedList @50% run (~328K
# bytes/render, ~63 Gen0/1k).
$keyedAllocMain = Measure-PerfRuns -Runs @(
    [pscustomobject]@{ RendersPerSec = 18.5; AvgReconcileMs = 6.98; AvgDiffMs = 6.86; AvgMemoryMB = 186; AllocBytesPerRender = 328000; Gen0PerKRenders = 63.2; Gen0 = 6; Gen1 = 2; Gen2 = 1; TotalRenders = 96; DurationSeconds = 5 }
    [pscustomobject]@{ RendersPerSec = 18.6; AvgReconcileMs = 6.98; AvgDiffMs = 6.86; AvgMemoryMB = 186; AllocBytesPerRender = 328200; Gen0PerKRenders = 63.4; Gen0 = 6; Gen1 = 2; Gen2 = 1; TotalRenders = 96; DurationSeconds = 5 }
    [pscustomobject]@{ RendersPerSec = 18.4; AvgReconcileMs = 6.98; AvgDiffMs = 6.86; AvgMemoryMB = 186; AllocBytesPerRender = 327800; Gen0PerKRenders = 63.0; Gen0 = 6; Gen1 = 2; Gen2 = 1; TotalRenders = 96; DurationSeconds = 5 }
)
$keyedAllocPr = Measure-PerfRuns -Runs @(
    [pscustomobject]@{ RendersPerSec = 18.5; AvgReconcileMs = 6.98; AvgDiffMs = 6.86; AvgMemoryMB = 186; AllocBytesPerRender = 262000; Gen0PerKRenders = 50.2; Gen0 = 5; Gen1 = 2; Gen2 = 1; TotalRenders = 96; DurationSeconds = 5 }
    [pscustomobject]@{ RendersPerSec = 18.6; AvgReconcileMs = 6.98; AvgDiffMs = 6.86; AvgMemoryMB = 186; AllocBytesPerRender = 262200; Gen0PerKRenders = 50.4; Gen0 = 5; Gen1 = 2; Gen2 = 1; TotalRenders = 96; DurationSeconds = 5 }
    [pscustomobject]@{ RendersPerSec = 18.4; AvgReconcileMs = 6.98; AvgDiffMs = 6.86; AvgMemoryMB = 186; AllocBytesPerRender = 261800; Gen0PerKRenders = 50.0; Gen0 = 5; Gen1 = 2; Gen2 = 1; TotalRenders = 96; DurationSeconds = 5 }
)
$keyedAllocSection = Format-PerfKeyedListSection -MainKeyed $keyedAllocMain -PrKeyed $keyedAllocPr -Percent 50
$keyedAllocText = $keyedAllocSection -join "`n"
Assert-Match $keyedAllocText 'Allocation (keyed-list)' 'keyed section renders the allocation sub-table when alloc present'
Assert-Match $keyedAllocText 'Alloc bytes/render'      'keyed alloc sub-table has bytes/render row'
Assert-Match $keyedAllocText 'Gen0 GC / 1k renders'    'keyed alloc sub-table has Gen0 row'
$keyedAllocRow = ($keyedAllocSection | Where-Object { $_ -match 'Alloc bytes/render' }) -join ' '
Assert-Match $keyedAllocRow 'improvement' 'keyed alloc DOWN main->PR reads improvement (lower-is-better honored)'
# The allocation sub-table sits AFTER the keyed headline metrics table within the section.
$idxKeyedHead  = $keyedAllocText.IndexOf('Avg Reconcile')
$idxKeyedAlloc = $keyedAllocText.IndexOf('Allocation (keyed-list)')
Assert-True (($idxKeyedHead -ge 0) -and ($idxKeyedHead -lt $idxKeyedAlloc)) 'keyed alloc sub-table follows the keyed headline metrics table'

# Omitted when the keyed aggregates carry no alloc metrics (legacy keyed head). The
# $keyedMain/$keyedPr aggregates above were built without alloc fields.
Assert-True (-not ($keyedSectionText -like '*Allocation (keyed-list)*')) 'keyed alloc sub-table omitted when keyed aggregates lack alloc'

# In a full comment the positional StocksGrid allocation table and the keyed allocation
# sub-table are DISTINCT, separately-labelled tables (positional vs keyed workload allocs).
$bothAllocComment = Format-PerfComment -Main $allocMain -Pr $allocPr -WinUI3 $null -Rust $null -MainKeyed $keyedAllocMain -PrKeyed $keyedAllocPr -Context $ctx
Assert-Match $bothAllocComment 'Allocation (Reactor)'    'full comment keeps the StocksGrid allocation table'
Assert-Match $bothAllocComment 'Allocation (keyed-list)' 'full comment adds the distinct keyed allocation sub-table'


# ── Format-PerfFlexSection + Format-PerfComment: flex/Yoga layout workload ─────
# Same direction-aware, by-significance shape as the keyed block above: rps/reconcile/
# diff move DOWN main->PR while memory carries a small SYMMETRIC per-pair jitter (mean
# Δ ~0). The verdicts must split exactly as for the keyed leg — proving the flex section
# reuses the shared direction-aware paired-CI machinery rather than a hard-coded verdict.
$flexMainRuns = @(); $flexPrRuns = @()
1..12 | ForEach-Object {
    $j = ($_ % 4) * 0.05
    $mj = ((($_ % 2) * 2) - 1) * 0.2  # alternating +0.2 / -0.2 so the paired memory Δ straddles 0
    $flexMainRuns += [pscustomobject]@{ RendersPerSec = 8.0 + $j; AvgReconcileMs = 9.0 + $j; AvgDiffMs = 7.0 + $j; AvgMemoryMB = 250 + $mj; TotalRenders = 80; DurationSeconds = 10 }
    $flexPrRuns   += [pscustomobject]@{ RendersPerSec = 7.0 + $j; AvgReconcileMs = 7.0 + $j; AvgDiffMs = 5.0 + $j; AvgMemoryMB = 250 - $mj; TotalRenders = 70; DurationSeconds = 10 }
}
$flexMain = Measure-PerfRuns -Runs $flexMainRuns
$flexPr   = Measure-PerfRuns -Runs $flexPrRuns

# Direct section renderer: empty when either side is null, populated when both present.
Assert-Equal 0 @(Format-PerfFlexSection -MainFlex $null -PrFlex $flexPr -Percent 50).Count 'flex section empty when main flex null'
Assert-Equal 0 @(Format-PerfFlexSection -MainFlex $flexMain -PrFlex $null -Percent 50).Count 'flex section empty when pr flex null'
$flexSection = Format-PerfFlexSection -MainFlex $flexMain -PrFlex $flexPr -Percent 50
$flexSectionText = $flexSection -join "`n"
Assert-Match $flexSectionText 'Flex/Yoga layout workload' 'flex section has heading'
Assert-Match $flexSectionText 'StressPerf.Flex'           'flex heading names the workload'
Assert-Match $flexSectionText 'Avg Reconcile'             'flex section has reconcile row'
Assert-Match $flexSectionText 'Yoga'                      'flex preamble names the Yoga layout engine'
Assert-Match $flexSectionText 'layout pass'               'flex preamble cites the per-frame layout pass'
# Direction-awareness: rps and reconcile both DECREASE main->PR, yet rps (higher-is-
# better) must read regression while reconcile (lower-is-better) reads improvement.
$flexRpsRow   = ($flexSection | Where-Object { $_ -match 'Renders/sec' })   -join ' '
$flexReconRow = ($flexSection | Where-Object { $_ -match 'Avg Reconcile' }) -join ' '
$flexDiffRow  = ($flexSection | Where-Object { $_ -match 'Avg Diff' })      -join ' '
$flexMemRow   = ($flexSection | Where-Object { $_ -match 'Avg Memory' })    -join ' '
Assert-Match $flexRpsRow   'regression'  'flex: rps DOWN reads regression (higher-is-better honored)'
Assert-Match $flexReconRow 'improvement' 'flex: reconcile DOWN reads improvement (lower-is-better honored)'
Assert-Match $flexDiffRow  'improvement' 'flex: diff DOWN reads improvement (lower-is-better honored)'
Assert-Match $flexMemRow   'within noise' 'flex: symmetric memory Δ reads within noise (paired CI straddles 0)'
# -Percent threads into the heading independently of the methodology line.
$flexSection75 = (Format-PerfFlexSection -MainFlex $flexMain -PrFlex $flexPr -Percent 75) -join "`n"
Assert-Match $flexSection75 'Flex/Yoga layout workload*--percent 75' 'flex heading reflects the -Percent argument'

# Threaded through Format-PerfComment: present when flex aggregates present, sitting
# after the keyed-list table and before the cross-framework table.
$flexComment = Format-PerfComment -Main $main -Pr $pr -WinUI3 $null -Rust $null -MainFloor $floorMain -PrFloor $floorPr -MainKeyed $keyedMain -PrKeyed $keyedPr -MainFlex $flexMain -PrFlex $flexPr -Context $ctx
Assert-Match $flexComment 'Flex/Yoga layout workload' 'comment renders flex table when flex aggregates present'
$idxKeyedF = $flexComment.IndexOf('Keyed-list workload')
$idxFlexF  = $flexComment.IndexOf('Flex/Yoga layout workload')
$idxXfwF   = $flexComment.IndexOf('Cross-framework reference')
Assert-True (($idxKeyedF -lt $idxFlexF) -and ($idxFlexF -lt $idxXfwF)) 'flex table sits after the keyed-list table and before cross-framework'

# Omitted entirely when flex aggregates are absent (flex leg disabled / build omitted).
$noFlexComment = Format-PerfComment -Main $main -Pr $pr -WinUI3 $null -Rust $null -MainFlex $null -PrFlex $null -Context $ctx
Assert-True (-not ($noFlexComment -like '*Flex/Yoga layout workload*')) 'flex table omitted when flex aggregates null'

# Allocation sub-table for the flex leg: shared PerfAllocMetricSpec over flex aggregates.
# Alloc moves DOWN main->PR (an improvement on a lower-is-better metric); tiny jitter
# keeps each paired CI off 0.
$flexAllocMain = Measure-PerfRuns -Runs @(
    [pscustomobject]@{ RendersPerSec = 18.5; AvgReconcileMs = 6.98; AvgDiffMs = 6.86; AvgMemoryMB = 186; AllocBytesPerRender = 512000; Gen0PerKRenders = 98.2; Gen0 = 9; Gen1 = 3; Gen2 = 1; TotalRenders = 96; DurationSeconds = 5 }
    [pscustomobject]@{ RendersPerSec = 18.6; AvgReconcileMs = 6.98; AvgDiffMs = 6.86; AvgMemoryMB = 186; AllocBytesPerRender = 512200; Gen0PerKRenders = 98.4; Gen0 = 9; Gen1 = 3; Gen2 = 1; TotalRenders = 96; DurationSeconds = 5 }
    [pscustomobject]@{ RendersPerSec = 18.4; AvgReconcileMs = 6.98; AvgDiffMs = 6.86; AvgMemoryMB = 186; AllocBytesPerRender = 511800; Gen0PerKRenders = 98.0; Gen0 = 9; Gen1 = 3; Gen2 = 1; TotalRenders = 96; DurationSeconds = 5 }
)
$flexAllocPr = Measure-PerfRuns -Runs @(
    [pscustomobject]@{ RendersPerSec = 18.5; AvgReconcileMs = 6.98; AvgDiffMs = 6.86; AvgMemoryMB = 186; AllocBytesPerRender = 384000; Gen0PerKRenders = 74.2; Gen0 = 7; Gen1 = 3; Gen2 = 1; TotalRenders = 96; DurationSeconds = 5 }
    [pscustomobject]@{ RendersPerSec = 18.6; AvgReconcileMs = 6.98; AvgDiffMs = 6.86; AvgMemoryMB = 186; AllocBytesPerRender = 384200; Gen0PerKRenders = 74.4; Gen0 = 7; Gen1 = 3; Gen2 = 1; TotalRenders = 96; DurationSeconds = 5 }
    [pscustomobject]@{ RendersPerSec = 18.4; AvgReconcileMs = 6.98; AvgDiffMs = 6.86; AvgMemoryMB = 186; AllocBytesPerRender = 383800; Gen0PerKRenders = 74.0; Gen0 = 7; Gen1 = 3; Gen2 = 1; TotalRenders = 96; DurationSeconds = 5 }
)
$flexAllocSection = Format-PerfFlexSection -MainFlex $flexAllocMain -PrFlex $flexAllocPr -Percent 50
$flexAllocText = $flexAllocSection -join "`n"
Assert-Match $flexAllocText 'Allocation (flex)'  'flex section renders the allocation sub-table when alloc present'
Assert-Match $flexAllocText 'Alloc bytes/render' 'flex alloc sub-table has bytes/render row'
Assert-Match $flexAllocText 'Gen0 GC / 1k renders' 'flex alloc sub-table has Gen0 row'
$flexAllocRow = ($flexAllocSection | Where-Object { $_ -match 'Alloc bytes/render' }) -join ' '
Assert-Match $flexAllocRow 'improvement' 'flex alloc DOWN main->PR reads improvement (lower-is-better honored)'
$idxFlexHead  = $flexAllocText.IndexOf('Avg Reconcile')
$idxFlexAlloc = $flexAllocText.IndexOf('Allocation (flex)')
Assert-True (($idxFlexHead -ge 0) -and ($idxFlexHead -lt $idxFlexAlloc)) 'flex alloc sub-table follows the flex headline metrics table'
# Omitted when the flex aggregates carry no alloc metrics (legacy flex head).
Assert-True (-not ($flexSectionText -like '*Allocation (flex)*')) 'flex alloc sub-table omitted when flex aggregates lack alloc'

# In a full comment the positional StocksGrid allocation table and the flex allocation
# sub-table are DISTINCT, separately-labelled tables (positional vs flex-layout allocs).
$bothFlexAllocComment = Format-PerfComment -Main $allocMain -Pr $allocPr -WinUI3 $null -Rust $null -MainFlex $flexAllocMain -PrFlex $flexAllocPr -Context $ctx
Assert-Match $bothFlexAllocComment 'Allocation (Reactor)' 'full comment keeps the StocksGrid allocation table'
Assert-Match $bothFlexAllocComment 'Allocation (flex)'    'full comment adds the distinct flex allocation sub-table'


# ── Format-PerfDataGridSection + Format-PerfComment: DataGrid control workload ─
# Same direction-aware, by-significance shape as the keyed/flex blocks above: rps/
# reconcile/diff move DOWN main->PR while memory carries a small SYMMETRIC per-pair
# jitter (mean Δ ~0). The verdicts must split exactly as for the flex leg — proving the
# DataGrid section reuses the shared direction-aware paired-CI machinery.
$dgMainRuns = @(); $dgPrRuns = @()
1..12 | ForEach-Object {
    $j = ($_ % 4) * 0.05
    $mj = ((($_ % 2) * 2) - 1) * 0.2  # alternating +0.2 / -0.2 so the paired memory Δ straddles 0
    $dgMainRuns += [pscustomobject]@{ RendersPerSec = 8.0 + $j; AvgReconcileMs = 9.0 + $j; AvgDiffMs = 7.0 + $j; AvgMemoryMB = 250 + $mj; TotalRenders = 80; DurationSeconds = 10 }
    $dgPrRuns   += [pscustomobject]@{ RendersPerSec = 7.0 + $j; AvgReconcileMs = 7.0 + $j; AvgDiffMs = 5.0 + $j; AvgMemoryMB = 250 - $mj; TotalRenders = 70; DurationSeconds = 10 }
}
$dgMain = Measure-PerfRuns -Runs $dgMainRuns
$dgPr   = Measure-PerfRuns -Runs $dgPrRuns

# Direct section renderer: empty when either side is null, populated when both present.
Assert-Equal 0 @(Format-PerfDataGridSection -MainDataGrid $null -PrDataGrid $dgPr -Percent 50).Count 'datagrid section empty when main datagrid null'
Assert-Equal 0 @(Format-PerfDataGridSection -MainDataGrid $dgMain -PrDataGrid $null -Percent 50).Count 'datagrid section empty when pr datagrid null'
$dgSection = Format-PerfDataGridSection -MainDataGrid $dgMain -PrDataGrid $dgPr -Percent 50
$dgSectionText = $dgSection -join "`n"
Assert-Match $dgSectionText 'DataGrid control workload' 'datagrid section has heading'
Assert-Match $dgSectionText 'StressPerf.DataGrid'       'datagrid heading names the workload'
Assert-Match $dgSectionText 'Avg Reconcile'             'datagrid section has reconcile row'
Assert-Match $dgSectionText 'DataGridComponent'         'datagrid preamble names the DataGrid control'
# Direction-awareness: rps and reconcile both DECREASE main->PR, yet rps (higher-is-
# better) must read regression while reconcile (lower-is-better) reads improvement.
$dgRpsRow   = ($dgSection | Where-Object { $_ -match 'Renders/sec' })   -join ' '
$dgReconRow = ($dgSection | Where-Object { $_ -match 'Avg Reconcile' }) -join ' '
$dgDiffRow  = ($dgSection | Where-Object { $_ -match 'Avg Diff' })      -join ' '
$dgMemRow   = ($dgSection | Where-Object { $_ -match 'Avg Memory' })    -join ' '
Assert-Match $dgRpsRow   'regression'  'datagrid: rps DOWN reads regression (higher-is-better honored)'
Assert-Match $dgReconRow 'improvement' 'datagrid: reconcile DOWN reads improvement (lower-is-better honored)'
Assert-Match $dgDiffRow  'improvement' 'datagrid: diff DOWN reads improvement (lower-is-better honored)'
Assert-Match $dgMemRow   'within noise' 'datagrid: symmetric memory Δ reads within noise (paired CI straddles 0)'
# -Percent threads into the heading independently of the methodology line.
$dgSection75 = (Format-PerfDataGridSection -MainDataGrid $dgMain -PrDataGrid $dgPr -Percent 75) -join "`n"
Assert-Match $dgSection75 'DataGrid control workload*--percent 75' 'datagrid heading reflects the -Percent argument'

# Threaded through Format-PerfComment: present when datagrid aggregates present, sitting
# after the flex table and before the cross-framework table.
$dgComment = Format-PerfComment -Main $main -Pr $pr -WinUI3 $null -Rust $null -MainFloor $floorMain -PrFloor $floorPr -MainKeyed $keyedMain -PrKeyed $keyedPr -MainFlex $flexMain -PrFlex $flexPr -MainDataGrid $dgMain -PrDataGrid $dgPr -Context $ctx
Assert-Match $dgComment 'DataGrid control workload' 'comment renders datagrid table when datagrid aggregates present'
$idxFlexD = $dgComment.IndexOf('Flex/Yoga layout workload')
$idxDgD   = $dgComment.IndexOf('DataGrid control workload')
$idxXfwD  = $dgComment.IndexOf('Cross-framework reference')
Assert-True (($idxFlexD -lt $idxDgD) -and ($idxDgD -lt $idxXfwD)) 'datagrid table sits after the flex table and before cross-framework'

# Omitted entirely when datagrid aggregates are absent (datagrid leg disabled / build omitted).
$noDgComment = Format-PerfComment -Main $main -Pr $pr -WinUI3 $null -Rust $null -MainDataGrid $null -PrDataGrid $null -Context $ctx
Assert-True (-not ($noDgComment -like '*DataGrid control workload*')) 'datagrid table omitted when datagrid aggregates null'

# Allocation sub-table for the datagrid leg: shared PerfAllocMetricSpec over datagrid
# aggregates. Alloc moves DOWN main->PR (an improvement on a lower-is-better metric);
# tiny jitter keeps each paired CI off 0.
$dgAllocMain = Measure-PerfRuns -Runs @(
    [pscustomobject]@{ RendersPerSec = 18.5; AvgReconcileMs = 6.98; AvgDiffMs = 6.86; AvgMemoryMB = 186; AllocBytesPerRender = 612000; Gen0PerKRenders = 118.2; Gen0 = 11; Gen1 = 3; Gen2 = 1; TotalRenders = 96; DurationSeconds = 5 }
    [pscustomobject]@{ RendersPerSec = 18.6; AvgReconcileMs = 6.98; AvgDiffMs = 6.86; AvgMemoryMB = 186; AllocBytesPerRender = 612200; Gen0PerKRenders = 118.4; Gen0 = 11; Gen1 = 3; Gen2 = 1; TotalRenders = 96; DurationSeconds = 5 }
    [pscustomobject]@{ RendersPerSec = 18.4; AvgReconcileMs = 6.98; AvgDiffMs = 6.86; AvgMemoryMB = 186; AllocBytesPerRender = 611800; Gen0PerKRenders = 118.0; Gen0 = 11; Gen1 = 3; Gen2 = 1; TotalRenders = 96; DurationSeconds = 5 }
)
$dgAllocPr = Measure-PerfRuns -Runs @(
    [pscustomobject]@{ RendersPerSec = 18.5; AvgReconcileMs = 6.98; AvgDiffMs = 6.86; AvgMemoryMB = 186; AllocBytesPerRender = 458000; Gen0PerKRenders = 88.2; Gen0 = 8; Gen1 = 3; Gen2 = 1; TotalRenders = 96; DurationSeconds = 5 }
    [pscustomobject]@{ RendersPerSec = 18.6; AvgReconcileMs = 6.98; AvgDiffMs = 6.86; AvgMemoryMB = 186; AllocBytesPerRender = 458200; Gen0PerKRenders = 88.4; Gen0 = 8; Gen1 = 3; Gen2 = 1; TotalRenders = 96; DurationSeconds = 5 }
    [pscustomobject]@{ RendersPerSec = 18.4; AvgReconcileMs = 6.98; AvgDiffMs = 6.86; AvgMemoryMB = 186; AllocBytesPerRender = 457800; Gen0PerKRenders = 88.0; Gen0 = 8; Gen1 = 3; Gen2 = 1; TotalRenders = 96; DurationSeconds = 5 }
)
$dgAllocSection = Format-PerfDataGridSection -MainDataGrid $dgAllocMain -PrDataGrid $dgAllocPr -Percent 50
$dgAllocText = $dgAllocSection -join "`n"
Assert-Match $dgAllocText 'Allocation (datagrid)' 'datagrid section renders the allocation sub-table when alloc present'
Assert-Match $dgAllocText 'Alloc bytes/render'    'datagrid alloc sub-table has bytes/render row'
Assert-Match $dgAllocText 'Gen0 GC / 1k renders'  'datagrid alloc sub-table has Gen0 row'
$dgAllocRow = ($dgAllocSection | Where-Object { $_ -match 'Alloc bytes/render' }) -join ' '
Assert-Match $dgAllocRow 'improvement' 'datagrid alloc DOWN main->PR reads improvement (lower-is-better honored)'
$idxDgHead  = $dgAllocText.IndexOf('Avg Reconcile')
$idxDgAlloc = $dgAllocText.IndexOf('Allocation (datagrid)')
Assert-True (($idxDgHead -ge 0) -and ($idxDgHead -lt $idxDgAlloc)) 'datagrid alloc sub-table follows the datagrid headline metrics table'
# Omitted when the datagrid aggregates carry no alloc metrics (legacy datagrid head).
Assert-True (-not ($dgSectionText -like '*Allocation (datagrid)*')) 'datagrid alloc sub-table omitted when datagrid aggregates lack alloc'

# In a full comment the positional StocksGrid allocation table and the datagrid
# allocation sub-table are DISTINCT, separately-labelled tables.
$bothDgAllocComment = Format-PerfComment -Main $allocMain -Pr $allocPr -WinUI3 $null -Rust $null -MainDataGrid $dgAllocMain -PrDataGrid $dgAllocPr -Context $ctx
Assert-Match $bothDgAllocComment 'Allocation (Reactor)'  'full comment keeps the StocksGrid allocation table'
Assert-Match $bothDgAllocComment 'Allocation (datagrid)' 'full comment adds the distinct datagrid allocation sub-table'


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

    # Malformed-line resilience: repetition drives the rep-keyed pairing and is cast to
    # [int], so a row missing repetition (schema drift / truncation) or carrying a
    # non-numeric value must be SKIPPED at ingestion, not throw past the guard. Two valid
    # reps (0, 2) bracket a missing-repetition row and a non-numeric-repetition row.
    $repJsonl = Join-Path $microTmp 'badrep.jsonl'
    Set-Content -LiteralPath $repJsonl -Encoding UTF8 -Value @(
        '{"benchId":"M1","benchName":"PropertyDiff","variant":"Reactor","iterations":1,"repetition":0,"meanNs":100,"allocBytes":500,"status":"ok"}'
        '{"benchId":"M1","benchName":"PropertyDiff","variant":"Reactor","iterations":1,"meanNs":102,"allocBytes":505,"status":"ok"}'           # missing repetition -> skip
        '{"benchId":"M1","benchName":"PropertyDiff","variant":"Reactor","iterations":1,"repetition":"x","meanNs":103,"allocBytes":507,"status":"ok"}' # non-numeric -> skip
        '{"benchId":"M1","benchName":"PropertyDiff","variant":"Reactor","iterations":1,"repetition":2,"meanNs":98,"allocBytes":495,"status":"ok"}'
    )
    $repMap = $null
    $repThrew = $false
    try { $repMap = Read-MicroBenchResults $repJsonl } catch { $repThrew = $true }
    Assert-True (-not $repThrew)                            'malformed repetition: parser does not throw'
    Assert-Equal 2 $repMap['M1'].MeanNsSamples.Count       'malformed repetition: only the 2 numeric-repetition rows kept'
    Assert-Equal 0 $repMap['M1'].Repetitions[0]            'malformed repetition: surviving reps are the valid ones (0, 2)'
    Assert-Equal 2 $repMap['M1'].Repetitions[1]            'malformed repetition: non-numeric/missing rows dropped, not mis-cast'

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

    # ── ARMED ns-flag mechanism (dormant by default; exercised here with the flag on) ──
    # Promoting ns informational -> flagged is gated on $script:MicroNsAutoFlag. When
    # armed, the row COMBINES the (rep-interleaved, band-gated) ns delta with the alloc
    # delta: either axis can flag, and a disagreement reads 'mixed'. The flag is reset to
    # dormant after this block so the remaining tests see v1 behaviour.
    $prevNsFlag = $script:MicroNsAutoFlag
    $script:MicroNsAutoFlag = $true
    try {
        # Combination matrix: ns now participates.
        Assert-Equal 'better' (Get-PerfMicroRowStatus -NsStatus 'better' -AllocStatus 'noise')  'armed: ns better + alloc noise -> better (ns now drives)'
        Assert-Equal 'worse'  (Get-PerfMicroRowStatus -NsStatus 'worse'  -AllocStatus 'noise')  'armed: ns worse + alloc noise -> worse'
        Assert-Equal 'better' (Get-PerfMicroRowStatus -NsStatus 'better' -AllocStatus 'better') 'armed: both better -> better'
        Assert-Equal 'worse'  (Get-PerfMicroRowStatus -NsStatus 'worse'  -AllocStatus 'worse')  'armed: both worse -> worse'
        Assert-Equal 'mixed'  (Get-PerfMicroRowStatus -NsStatus 'better' -AllocStatus 'worse')  'armed: ns better + alloc worse -> mixed'
        Assert-Equal 'mixed'  (Get-PerfMicroRowStatus -NsStatus 'worse'  -AllocStatus 'better') 'armed: ns worse + alloc better -> mixed'
        Assert-Equal 'noise'  (Get-PerfMicroRowStatus -NsStatus 'noise'  -AllocStatus 'noise')  'armed: both noise -> noise'
        Assert-Equal 'better' (Get-PerfMicroRowStatus -NsStatus 'na'     -AllocStatus 'better') 'armed: ns na -> alloc drives'
        Assert-Equal 'better' (Get-PerfMicroRowStatus -NsStatus 'better' -AllocStatus 'na')     'armed: alloc na -> ns drives (differs from dormant na)'
        Assert-Equal 'na'     (Get-PerfMicroRowStatus -NsStatus 'na'     -AllocStatus 'na')     'armed: both na -> na'
        Assert-Equal 'noise'  (Get-PerfMicroRowStatus -NsStatus 'noise'  -AllocStatus 'na')     'armed: ns noise + alloc na -> noise'
        Assert-Equal 'worse'  (Get-PerfMicroRowStatus -NsStatus 'worse'  -AllocStatus 'na')     'armed: ns worse + alloc na -> worse (ns drives)'
        Assert-Equal 'better' (Get-PerfMicroRowStatus -NsStatus 'noise'  -AllocStatus 'better') 'armed: ns noise + alloc better -> better'
        Assert-Equal 'worse'  (Get-PerfMicroRowStatus -NsStatus 'noise'  -AllocStatus 'worse')  'armed: ns noise + alloc worse -> worse'
        Assert-Equal 'noise'  (Get-PerfMicroRowStatus -NsStatus 'na'     -AllocStatus 'noise')  'armed: ns na + alloc noise -> noise'
        Assert-Equal 'worse'  (Get-PerfMicroRowStatus -NsStatus 'na'     -AllocStatus 'worse')  'armed: ns na + alloc worse -> worse'

        # 'mixed' glyph renders.
        Assert-Equal "$([char]0x2195)$([char]0xFE0F) mixed" (Get-PerfStatusGlyph 'mixed') 'mixed -> up-down arrow glyph'

        # SYNTHETIC IDENTICAL-BINARY CALIBRATION (the no-false-flag proof, armed):
        # an unchanged binary produces ns samples drawn from the SAME distribution on both
        # sides — once rep-interleaved the paired ns differences center on 0 with only
        # sub-band jitter, so the ns CI must clear neither +band nor -band => 'noise'.
        # Here main = pr distribution with +-~1% per-rep jitter (well inside the 3% band).
        $idMain = @(
            (New-MicroRow 'NS1' 'IdenticalBin' 'Reactor' 0 1000 800)
            (New-MicroRow 'NS1' 'IdenticalBin' 'Reactor' 1 1000 800)
            (New-MicroRow 'NS1' 'IdenticalBin' 'Reactor' 2 1000 800)
            (New-MicroRow 'NS1' 'IdenticalBin' 'Reactor' 3 1000 800)
            (New-MicroRow 'NS1' 'IdenticalBin' 'Reactor' 4 1000 800)
        )
        $idPr = @(
            (New-MicroRow 'NS1' 'IdenticalBin' 'Reactor' 0 1010 800)
            (New-MicroRow 'NS1' 'IdenticalBin' 'Reactor' 1  990 800)
            (New-MicroRow 'NS1' 'IdenticalBin' 'Reactor' 2 1005 800)
            (New-MicroRow 'NS1' 'IdenticalBin' 'Reactor' 3  995 800)
            (New-MicroRow 'NS1' 'IdenticalBin' 'Reactor' 4 1002 800)
        )
        $idMainJsonl = Join-Path $microTmp 'nsband-id-main.jsonl'
        $idPrJsonl   = Join-Path $microTmp 'nsband-id-pr.jsonl'
        Set-Content -LiteralPath $idMainJsonl -Value $idMain -Encoding UTF8
        Set-Content -LiteralPath $idPrJsonl   -Value $idPr   -Encoding UTF8
        $idCmp = @(Get-PerfMicroComparison -Main (Read-MicroBenchResults $idMainJsonl) -Pr (Read-MicroBenchResults $idPrJsonl))[0]
        Assert-Equal 'noise' $idCmp.NsDelta.Status 'armed ns calibration: identical-binary sub-band ns jitter reads noise (no false flag)'
        Assert-Equal 'noise' (Get-PerfMicroRowStatus -NsStatus $idCmp.NsDelta.Status -AllocStatus $idCmp.AllocDelta.Status) 'armed ns calibration: identical-binary row = noise'

        # SYNTHETIC REAL-EFFECT: a consistent ns improvement well beyond the band must
        # flag 'better' once armed (the capability the flag delivers).
        $effMain = @(
            (New-MicroRow 'NS2' 'RealWin' 'Reactor' 0 1000 800)
            (New-MicroRow 'NS2' 'RealWin' 'Reactor' 1 1000 800)
            (New-MicroRow 'NS2' 'RealWin' 'Reactor' 2 1000 800)
            (New-MicroRow 'NS2' 'RealWin' 'Reactor' 3 1000 800)
        )
        $effPr = @(
            (New-MicroRow 'NS2' 'RealWin' 'Reactor' 0 900 800)
            (New-MicroRow 'NS2' 'RealWin' 'Reactor' 1 905 800)
            (New-MicroRow 'NS2' 'RealWin' 'Reactor' 2 898 800)
            (New-MicroRow 'NS2' 'RealWin' 'Reactor' 3 902 800)
        )
        $effMainJsonl = Join-Path $microTmp 'nsband-eff-main.jsonl'
        $effPrJsonl   = Join-Path $microTmp 'nsband-eff-pr.jsonl'
        Set-Content -LiteralPath $effMainJsonl -Value $effMain -Encoding UTF8
        Set-Content -LiteralPath $effPrJsonl   -Value $effPr   -Encoding UTF8
        $effCmp = @(Get-PerfMicroComparison -Main (Read-MicroBenchResults $effMainJsonl) -Pr (Read-MicroBenchResults $effPrJsonl))[0]
        Assert-Equal 'better' $effCmp.NsDelta.Status 'armed ns calibration: consistent -10% ns (beyond 3% band) flags better'
        Assert-Equal 'better' (Get-PerfMicroRowStatus -NsStatus $effCmp.NsDelta.Status -AllocStatus $effCmp.AllocDelta.Status) 'armed ns calibration: real ns win drives row better'

        # The SAME real-effect data must read 'noise' when DORMANT (alloc unchanged), proving
        # the flag is the only thing that changes the verdict — arming is measurement-only.
        $script:MicroNsAutoFlag = $false
        Assert-Equal 'noise' (Get-PerfMicroRowStatus -NsStatus $effCmp.NsDelta.Status -AllocStatus $effCmp.AllocDelta.Status) 'dormant: same real ns win reads noise (alloc-only), so arming only relabels'
        $script:MicroNsAutoFlag = $true
    } finally {
        $script:MicroNsAutoFlag = $prevNsFlag
    }
    # The armed block restored the flag to its dormant default, so the remaining tests
    # (and production) see v1 alloc-only behaviour — no flag leak across tests.
    Assert-True (-not $script:MicroNsAutoFlag) 'armed block reset MicroNsAutoFlag to dormant (no leak into later tests)'

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

    # PR5c widened the micro ALLOC min-effect band 1.0% -> 3.0% (a provisional hedge, pending the
    # post-merge real-CI identical-binary calibration that the 1000-iter cut invalidated). Pin the
    # NEW band so a regression of the constant or a future mis-calibration is caught: a ~2% alloc
    # delta must read 'noise' at 3.0% (it WOULD have flagged at the old 1.0%), while a real ~4%
    # delta still flags 'worse'. Direct Get-PerfDelta keeps this deterministic.
    Assert-Equal 3.0 $script:MicroAllocMinEffectPct        'micro alloc min-effect band is the 3.0% provisional hedge'
    $b2B = @(1000, 1000, 1000, 1000)
    $b2C = @(1020, 1021, 1019, 1020)              # ~+2%: inside the new 3% band, outside the old 1%
    $band2 = Get-PerfDelta -Baseline 1000 -Candidate 1020 -LowerIsBetter $true `
        -BaselineSamples $b2B -CandidateSamples $b2C -RequirePairedCI -MinEffectPct $script:MicroAllocMinEffectPct
    Assert-Equal 'noise' $band2.Status                     'band@3.0%: a ~2% alloc delta reads noise (within the widened band)'
    $band2Old = Get-PerfDelta -Baseline 1000 -Candidate 1020 -LowerIsBetter $true `
        -BaselineSamples $b2B -CandidateSamples $b2C -RequirePairedCI -MinEffectPct 1.0
    Assert-Equal 'worse' $band2Old.Status                  'band@1.0%: the SAME ~2% delta would have flagged — proves the widen changed behavior'
    $b4B = @(1000, 1000, 1000, 1000)
    $b4C = @(1040, 1041, 1039, 1040)              # ~+4%: past the new 3% band
    $band4 = Get-PerfDelta -Baseline 1000 -Candidate 1040 -LowerIsBetter $true `
        -BaselineSamples $b4B -CandidateSamples $b4C -RequirePairedCI -MinEffectPct $script:MicroAllocMinEffectPct
    Assert-Equal 'worse' $band4.Status                     'band@3.0%: a real ~4% alloc delta still flags worse through the widened band'

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
    $section = Format-PerfMicroSection -Micro $cmp -ExpectedCount 4
    $sectionText = $section -join "`n"
    Assert-Match $sectionText 'Reconciler micro-benchmarks'      'section has heading'
    Assert-Match $sectionText 'ns/op'                            'section header names ns/op'
    Assert-Match $sectionText 'B/op'                             'section header names B/op'
    Assert-True ($sectionText.Contains('`M1` PropertyDiff'))     'section row labels bench + name'
    Assert-Match $sectionText '95% CI'                           'section delta cells carry a CI'
    # numeric sort survives into the rendered table (M2 row appears before M10 row).
    Assert-True (($sectionText.IndexOf('`M2`')) -lt ($sectionText.IndexOf('`M10`'))) 'rendered rows keep numeric order'
    # Dormant (default) section prose: alloc drives the verdict, ns is context-only.
    Assert-Match $sectionText 'Status tracks allocated bytes/op' 'dormant section: status tracks alloc bytes/op'
    Assert-Match $sectionText 'not auto-flagged'                 'dormant section: ns is shown for context, not flagged'
    Assert-True (-not $sectionText.Contains('Status combines ns/op')) 'dormant section: does NOT claim ns is combined'

    # ── Loud incompleteness (PR5c) ──────────────────────────────────────────────
    # A short table or a fully-omitted leg must be VISIBLE in the comment, never silently
    # absent (the #693 regression: a per-round timeout dropped the whole section and it went
    # unnoticed for the PR's entire life). Three render contracts:
    #   (1) rows present & complete (4/4)   -> no note (the happy path above);
    #   (2) rows present but < the expected count -> a "N/<expected> Incomplete" warning above the table;
    #   (3) null rows + an omit reason      -> a visible "omitted this run -- <reason>" callout;
    #   (4) null rows + no reason           -> still silent (the leg was simply not requested).
    Assert-True (-not $sectionText.Contains('Incomplete'))       'complete render (4/4 benches): no incompleteness note'
    $shortText = (Format-PerfMicroSection -Micro $cmp -ExpectedCount 16) -join "`n"
    Assert-Match $shortText 'Incomplete'                         'short render: labels the section incomplete'
    Assert-Match $shortText '4/16'                               'short render: shows the N/16 bench count'
    Assert-Match $shortText 'WARNING'                            'short render: uses a visible GitHub warning callout'
    Assert-Match $shortText 'Reconciler micro-benchmarks'        'short render: still renders the heading + table'
    Assert-True ($shortText.Contains('`M1` PropertyDiff'))       'short render: the benches that DID pair still render'
    # Default-count resolution (the production call shape): Format-PerfComment never passes
    # -ExpectedCount, so the default MUST resolve to $script:MicroExpectedBenchCount. A wrong
    # default (the 13 this PR fixed) would silently mis-label a real 13-of-16 run as complete.
    Assert-Equal 17 $script:MicroExpectedBenchCount             'canonical micro bench count is the full 17-bench emitted set (M1-M14 + 3 supplementary)'
    $defaultText = (Format-PerfMicroSection -Micro $cmp) -join "`n"
    Assert-Match $defaultText 'Incomplete'                       'default-count render: a 4-row run is incomplete vs the canonical full suite'
    Assert-Match $defaultText "4/$($script:MicroExpectedBenchCount)" 'default-count render: denominator is the script-scoped canonical count, not a literal'

    $omitText = (Format-PerfMicroSection -Micro $null -OmitReason 'the rep-interleave kept fewer than 2 paired rounds (a per-round timeout)') -join "`n"
    Assert-Match $omitText 'omitted this run'                    'omit render: visible omission block when a reason is supplied'
    Assert-Match $omitText 'timeout'                             'omit render: surfaces the omit reason text'
    Assert-Match $omitText 'Reconciler micro-benchmarks'        'omit render: keeps the heading so the leg is not silent'
    Assert-Match $omitText 'WARNING'                             'omit render: uses a visible GitHub warning callout'
    Assert-Equal 0 @(Format-PerfMicroSection -Micro $null -OmitReason $null).Count 'omit render: silent when null rows AND no reason (leg not requested)'
    Assert-Equal 0 @(Format-PerfMicroSection -Micro @() -OmitReason '   ').Count   'omit render: whitespace-only reason treated as no reason'

    # ── Drift guard: canonical count tracks the C# source of truth (PR5c) ────────
    # $script:MicroExpectedBenchCount is a hand-maintained mirror of BenchCatalog.All in
    # tests/perf_bench/PerfBench.ControlModel/Benches/AllBenches.cs. If a bench is added/removed
    # there but the constant is not updated, the completeness check silently mis-labels every run
    # (a 13-of-16 run reads "complete", or a full run reads "incomplete") — exactly the 13-vs-16
    # undercount this PR fixed. Parse the catalog initializer and assert the two agree so they
    # cannot diverge again without a red test. (Source is read, never built — works on any host.)
    $catalogPath = Join-Path $PSScriptRoot '..\..\perf_bench\PerfBench.ControlModel\Benches\AllBenches.cs'
    Assert-True (Test-Path -LiteralPath $catalogPath) "drift guard: BenchCatalog source exists ($catalogPath)"
    $catalogSrc  = Get-Content -LiteralPath $catalogPath -Raw
    $catalogInit = [regex]::Match($catalogSrc, '(?s)All\s*\{\s*get;\s*\}\s*=\s*new\s+IBench\[\]\s*\{(.*?)\};')
    Assert-True $catalogInit.Success 'drift guard: located the BenchCatalog.All initializer block'
    $catalogCount = ([regex]::Matches($catalogInit.Groups[1].Value, 'new\s+\w+\s*\(')).Count
    Assert-Equal 17 $catalogCount                               'drift guard: BenchCatalog.All declares 17 benches (M1-M13 + OAlloc/OUpdate/C207 + M14)'
    Assert-Equal $script:MicroExpectedBenchCount $catalogCount  'drift guard: $script:MicroExpectedBenchCount matches the C# catalog count (no silent drift)'

    # Format-PerfComment threads -Micro through into the comment.
    $microComment = Format-PerfComment -Main $allocMain -Pr $allocPr -WinUI3 $null -Rust $null -Micro $cmp -Context $ctx
    Assert-Match $microComment 'Reconciler micro-benchmarks'     'comment includes micro section when -Micro supplied'
    Assert-Match $microComment 'WinUI-undiluted'                 'comment carries the micro footnote'
    Assert-Match $microComment 'flag stays dormant pending a real-CI identical-binary band calibration' 'dormant footnote: ns flag dormant pending calibration'

    # End-to-end: Format-PerfComment threads -MicroOmitReason so the omission is visible in the
    # ASSEMBLED comment (not just the section helper) — and renders no bench table when omitted.
    $omitComment = Format-PerfComment -Main $allocMain -Pr $allocPr -WinUI3 $null -Rust $null -Micro $null -MicroOmitReason 'the micro exe was not built for the PR side' -Context $ctx
    Assert-Match $omitComment 'omitted this run'                 'comment surfaces micro omission end-to-end'
    Assert-Match $omitComment 'micro exe was not built'          'comment carries the omit reason text'
    Assert-True (-not ($omitComment -like '*| Bench |*'))        'comment omission renders no bench table (rows absent)'

    # Armed render: flip the master switch and confirm the section prose + footnote switch
    # to the combined-axis wording (and back). Arming is measurement-only, so this just
    # proves the render contract for when the flag is eventually armed post-calibration.
    $prevFlagRender = $script:MicroNsAutoFlag
    $script:MicroNsAutoFlag = $true
    try {
        $armedSection = (Format-PerfMicroSection -Micro $cmp -ExpectedCount 4) -join "`n"
        Assert-Match $armedSection 'Status combines ns/op and allocated bytes/op' 'armed section: status combines ns + alloc'
        Assert-Match $armedSection 'mixed'                                         'armed section: documents the mixed verdict'
        Assert-True (-not $armedSection.Contains('Status tracks allocated bytes/op')) 'armed section: drops the alloc-only wording'
        $armedMicroComment = Format-PerfComment -Main $allocMain -Pr $allocPr -WinUI3 $null -Rust $null -Micro $cmp -Context $ctx
        Assert-Match $armedMicroComment 'combines ns/op and allocated bytes/op'    'armed footnote: combined-axis wording'
    } finally {
        $script:MicroNsAutoFlag = $prevFlagRender
    }
    # Omitted / null -Micro -> no micro section (back-compat with existing callers).
    Assert-True (-not ($allocComment -like '*Reconciler micro-benchmarks*')) 'micro section omitted when -Micro not supplied'
}
finally {
    Remove-Item -LiteralPath $microTmp -Recurse -Force -ErrorAction SilentlyContinue
}


# ── Read-RowMemoResults + Format-PerfRowMemoSection: opt-in row-memo leg ───────
# The row-memo leg is the one SINGLE-TREE leg: a single PR-head build that prints
# stable key=value lines internally A/Bing Baseline vs Memo(i, () => row) (#327). The
# parser must read those lines into a typed object (or null on missing/garbage), and the
# renderer must produce the same-build Baseline|Memo|Win table + the skip-precondition note.
$rowMemoTmp = Join-Path ([IO.Path]::GetTempPath()) ("perflib-rowmemo-{0}" -f ([guid]::NewGuid().ToString('N')))
New-Item -ItemType Directory -Path $rowMemoTmp -Force | Out-Null
try {
    $rmKv = Join-Path $rowMemoTmp 'rowmemo.kv.txt'
    @(
        'baseline_ns=379.275', 'baseline_bytes=4616', 'baseline_rebuilds=1000000',
        'baseline_same_instance=false', 'baseline_can_skip=false',
        'memo_ns=43.633', 'memo_bytes=224', 'memo_rebuilds=0',
        'memo_same_instance=true', 'memo_can_skip=true',
        'recycles_per_arm=1000000', 'row_nodes=9', 'window=50'
    ) | Set-Content -LiteralPath $rmKv -Encoding UTF8

    $rm = Read-RowMemoResults -Path $rmKv
    Assert-True ($null -ne $rm)              'row-memo parser returns an object for valid key=value output'
    Assert-Equal 379.275 $rm.BaselineNs      'row-memo parser reads baseline_ns'
    Assert-Equal 4616    $rm.BaselineBytes   'row-memo parser reads baseline_bytes'
    Assert-Equal 1000000 $rm.BaselineRebuilds 'row-memo parser reads baseline_rebuilds'
    Assert-Equal 43.633  $rm.MemoNs          'row-memo parser reads memo_ns'
    Assert-Equal 224     $rm.MemoBytes       'row-memo parser reads memo_bytes'
    Assert-Equal 0       $rm.MemoRebuilds    'row-memo parser reads memo_rebuilds'
    Assert-True  $rm.MemoSameInstance        'row-memo parser reads memo_same_instance=true'
    Assert-True  $rm.MemoCanSkip             'row-memo parser reads memo_can_skip=true'
    Assert-True (-not $rm.BaselineSameInstance) 'row-memo parser reads baseline_same_instance=false'
    Assert-Equal 1000000 $rm.RecyclesPerArm  'row-memo parser reads recycles_per_arm'
    Assert-Equal 9       $rm.RowNodes         'row-memo parser reads row_nodes'

    # Missing-required-key and missing-file both yield $null so the leg omits the table.
    $rmBadKv = Join-Path $rowMemoTmp 'rowmemo-bad.kv.txt'
    @('baseline_ns=379.275', 'memo_ns=43.633') | Set-Content -LiteralPath $rmBadKv -Encoding UTF8
    Assert-Null (Read-RowMemoResults -Path $rmBadKv)                              'row-memo parser returns null when required keys are missing'
    Assert-Null (Read-RowMemoResults -Path (Join-Path $rowMemoTmp 'nope.txt'))    'row-memo parser returns null when the file is missing'
    Assert-Null (Read-RowMemoResults -Path $null)                                'row-memo parser returns null for a null path'
    # The two Baseline skip-precondition flags are REQUIRED (the narrative asserts them),
    # so a capture that dropped either one must fail fast to $null rather than silently
    # defaulting the flag to $false and rendering a story the bench never measured.
    $rmFull = @(
        'baseline_ns=379.275', 'baseline_bytes=4616', 'baseline_rebuilds=1000000',
        'baseline_same_instance=false', 'baseline_can_skip=false',
        'memo_ns=43.633', 'memo_bytes=224', 'memo_rebuilds=0',
        'memo_same_instance=true', 'memo_can_skip=true'
    )
    foreach ($drop in 'baseline_same_instance', 'baseline_can_skip') {
        $rmDropKv = Join-Path $rowMemoTmp ("rowmemo-drop-{0}.kv.txt" -f $drop)
        @($rmFull | Where-Object { -not $_.StartsWith("$drop=") }) | Set-Content -LiteralPath $rmDropKv -Encoding UTF8
        Assert-Null (Read-RowMemoResults -Path $rmDropKv) "row-memo parser returns null when $drop is missing (now required)"
    }

    # Renderer: empty array when null; full table + Win ratios + note when populated.
    $times = [char]0x00D7
    Assert-Equal 0 @(Format-PerfRowMemoSection -RowMemo $null).Count 'row-memo section empty when result null'
    $rmSection = Format-PerfRowMemoSection -RowMemo $rm
    $rmText = $rmSection -join "`n"
    Assert-Match $rmText 'Row memoization (opt-in)'           'row-memo section has the heading'
    Assert-Match $rmText '| Metric | Baseline | Memo | Win |' 'row-memo table uses the Baseline|Memo|Win columns'
    Assert-Match $rmText 'ns/recycle'                         'row-memo table has the ns/recycle row'
    Assert-Match $rmText 'bytes/recycle'                      'row-memo table has the bytes/recycle row'
    Assert-Match $rmText 'factory rebuilds'                   'row-memo table has the factory-rebuilds row'
    Assert-Match $rmText ("8.7$times faster")                'row-memo Win cell: 8.7x faster (379.275/43.633)'
    Assert-Match $rmText ("20.6$times less")                 'row-memo Win cell: 20.6x less alloc (4616/224)'
    Assert-Match $rmText '**eliminated**'                     'row-memo Win cell: rebuilds eliminated (baseline>0, memo=0)'
    Assert-Match $rmText '1,000,000'                          'row-memo renders the per-arm recycle count with separators'
    Assert-Match $rmText 'CanSkipUpdate'                      'row-memo note cites the CanSkipUpdate skip precondition'
    # The note renders the MEASURED Baseline flags, not hard-coded literals. $rm carries
    # both false, so the note must show sameInstance=false / CanSkipUpdate=false.
    Assert-Match $rmText 'sameInstance=false'                'row-memo note renders the measured Baseline sameInstance flag'
    Assert-Match $rmText 'CanSkipUpdate=false'               'row-memo note renders the measured Baseline CanSkipUpdate flag'
    # Prove it is DATA-DRIVEN, not a constant: a (hypothetical) result whose Baseline flags
    # read true must surface true in the note — so a bench bug / runtime change can't leave
    # the comment asserting a stale "false".
    $rmFlipped = $rm.PSObject.Copy()
    $rmFlipped.BaselineSameInstance = $true
    $rmFlipped.BaselineCanSkip = $true
    $rmFlippedText = (Format-PerfRowMemoSection -RowMemo $rmFlipped) -join "`n"
    Assert-Match $rmFlippedText 'sameInstance=true'          'row-memo note reflects a measured Baseline sameInstance=true (note is data-driven, not hard-coded)'
    Assert-Match $rmFlippedText 'CanSkipUpdate=true'         'row-memo note reflects a measured Baseline CanSkipUpdate=true (note is data-driven, not hard-coded)'
    # The headless-only caveat must be present so reviewers know the real win is bigger.
    Assert-Match $rmText 'captured by the headless' 'row-memo note flags that the stacked reconcile/patch saving is not captured headlessly'

    # Threaded through Format-PerfComment: present when set (after the regression table,
    # before the cross-framework reference), omitted when null (back-compat with callers
    # that never pass -RowMemo).
    $rmComment = Format-PerfComment -Main $main -Pr $pr -WinUI3 $null -Rust $null -RowMemo $rm -Context $ctx
    Assert-Match $rmComment 'Row memoization (opt-in)' 'comment renders the row-memo table when a result is supplied'
    $idxRegRm = $rmComment.IndexOf('Regression vs')
    $idxRowMemo = $rmComment.IndexOf('Row memoization (opt-in)')
    $idxXfwRm = $rmComment.IndexOf('Cross-framework reference')
    Assert-True (($idxRegRm -lt $idxRowMemo) -and ($idxRowMemo -lt $idxXfwRm)) 'row-memo table sits after the regression table and before cross-framework'
    $noRmComment = Format-PerfComment -Main $main -Pr $pr -WinUI3 $null -Rust $null -RowMemo $null -Context $ctx
    Assert-True (-not ($noRmComment -like '*Row memoization (opt-in)*')) 'row-memo table omitted when no result supplied'
    # Existing callers that never pass -RowMemo at all must still omit the section.
    $legacyComment = Format-PerfComment -Main $main -Pr $pr -WinUI3 $null -Rust $null -Context $ctx
    Assert-True (-not ($legacyComment -like '*Row memoization (opt-in)*')) 'row-memo table omitted for callers that do not pass -RowMemo'
}
finally {
    Remove-Item -LiteralPath $rowMemoTmp -Recurse -Force -ErrorAction SilentlyContinue
}


if ($script:Fail -gt 0) {
    Write-Host "FAILED: $($script:Fail) / $($script:Pass + $script:Fail) assertions" -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  ✗ $f" -ForegroundColor Red }
    exit 1
}
Write-Host "PASSED: all $($script:Pass) assertions" -ForegroundColor Green
exit 0
