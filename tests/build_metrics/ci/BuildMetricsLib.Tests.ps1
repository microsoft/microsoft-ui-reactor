<#
.SYNOPSIS
    Dependency-free unit tests for BuildMetricsLib.ps1 (the pure byte-format /
    delta / renderer used by the automatic build-metrics PR comment).

.DESCRIPTION
    Runs headless with no build and no external test framework, so it is safe on
    any runner (it is wired into .github/workflows/build-metrics-lib-tests.yml on
    changes under tests/build_metrics/ci/**). Exits non-zero if any assertion fails.

    Run locally:  pwsh tests/build_metrics/ci/BuildMetricsLib.Tests.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'BuildMetricsLib.ps1')

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

function Assert-NotMatch {
    param([string]$Haystack, [string]$Needle, [string]$Message)
    if ($Haystack -notlike "*$Needle*") { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add("$Message`n    unexpected substring: [$Needle]") }
}

# ── Format-ByteSize ──────────────────────────────────────────────────────────
Assert-Equal 'n/a'      (Format-ByteSize $null) 'null bytes -> n/a'
Assert-Equal '0 B'      (Format-ByteSize 0)     'zero bytes'
Assert-Equal '512 B'    (Format-ByteSize 512)   'sub-KB stays in bytes'
Assert-Equal '1023 B'   (Format-ByteSize 1023)  'just under 1 KB stays in bytes'
Assert-Equal '1.00 KB'  (Format-ByteSize 1024)  '1024 -> 1.00 KB'
Assert-Equal '1.50 KB'  (Format-ByteSize 1536)  '1.5 KB'
Assert-Equal '10.0 KB'  (Format-ByteSize 10240) '>=10 uses 1 decimal'
Assert-Equal '1.00 MB'  (Format-ByteSize (1024 * 1024))       '1 MB'
Assert-Equal '2.50 MB'  (Format-ByteSize ([long](2.5 * 1024 * 1024))) '2.5 MB'
Assert-Equal '-1.00 KB' (Format-ByteSize -1024) 'negative keeps sign'

# ── Format-SignedByteSize ────────────────────────────────────────────────────
Assert-Equal '+1.00 KB' (Format-SignedByteSize 1024)  'positive gets +'
Assert-Equal '+0 B'     (Format-SignedByteSize 0)     'zero gets +'
Assert-Equal '-1.00 KB' (Format-SignedByteSize -1024) 'negative keeps -'
Assert-Equal 'n/a'      (Format-SignedByteSize $null) 'null -> n/a'

# ── Get-SizeDelta ────────────────────────────────────────────────────────────
$d = Get-SizeDelta -BaseBytes 100000 -HeadBytes 110000
Assert-Equal 'grew'  $d.Status     'growth beyond band -> grew'
Assert-Equal 10000   $d.DeltaBytes 'grew delta bytes'
Assert-Equal 10      $d.DeltaPct   'grew delta pct (10%)'
Assert-True  (-not $d.Improved)    'grew is not an improvement'

$d = Get-SizeDelta -BaseBytes 110000 -HeadBytes 100000
Assert-Equal 'shrank' $d.Status     'shrink beyond band -> shrank'
Assert-Equal -10000   $d.DeltaBytes 'shrank delta bytes (negative)'
Assert-True  $d.Improved            'shrank is the improvement'

# Below both floors -> unchanged (but delta still reported).
$d = Get-SizeDelta -BaseBytes 1000000 -HeadBytes 1000010
Assert-Equal 'unchanged' $d.Status     'tiny delta -> unchanged'
Assert-Equal 10          $d.DeltaBytes 'unchanged still reports the delta'
Assert-Null  $d.Improved               'unchanged has no improvement flag'

# Clears percent but not the byte floor -> unchanged (both floors must clear).
$d = Get-SizeDelta -BaseBytes 100 -HeadBytes 140
Assert-Equal 'unchanged' $d.Status 'clears pct but under 64B byte floor -> unchanged'

# Clears the byte floor but not the percent floor -> unchanged.
$d = Get-SizeDelta -BaseBytes 100000000 -HeadBytes 100000100
Assert-Equal 'unchanged' $d.Status 'clears bytes but under 0.05% floor -> unchanged'

# Added / removed / na.
$d = Get-SizeDelta -BaseBytes $null -HeadBytes 5000
Assert-Equal 'added' $d.Status     'new artifact -> added'
Assert-Equal 5000    $d.DeltaBytes 'added delta = head size'
Assert-True  (-not $d.Improved)    'added is not an improvement'

$d = Get-SizeDelta -BaseBytes 5000 -HeadBytes $null
Assert-Equal 'removed' $d.Status     'dropped artifact -> removed'
Assert-Equal -5000     $d.DeltaBytes 'removed delta = -base size'
Assert-True  $d.Improved             'removed counts as an improvement (smaller ship)'

$d = Get-SizeDelta -BaseBytes $null -HeadBytes $null
Assert-Equal 'na' $d.Status  'absent both sides -> na'
Assert-Null  $d.DeltaBytes   'na has no delta'

# Zero base with a head value: pct is null but the byte floor still classifies it.
$d = Get-SizeDelta -BaseBytes 0 -HeadBytes 100000
Assert-Equal 'grew' $d.Status 'zero base, real head bytes -> grew (pct floor skipped)'
Assert-Null  $d.DeltaPct      'zero base -> null pct'

# ── Get-SizeStatusGlyph ──────────────────────────────────────────────────────
Assert-Equal "$([char]0x26A0)$([char]0xFE0F)" (Get-SizeStatusGlyph 'grew')      'grew -> warning'
Assert-Equal "$([char]0x2705)"                (Get-SizeStatusGlyph 'shrank')    'shrank -> check'
Assert-Equal "$([char]0x2248)"                (Get-SizeStatusGlyph 'unchanged') 'unchanged -> approx'
Assert-Equal "$([char]0x2014)"                (Get-SizeStatusGlyph 'na')        'na -> dash'

# ── Format-SizeDeltaCell ─────────────────────────────────────────────────────
Assert-Equal "$([char]0x2014)" (Format-SizeDeltaCell (Get-SizeDelta -BaseBytes $null -HeadBytes $null)) 'na cell -> dash'
Assert-Match  (Format-SizeDeltaCell (Get-SizeDelta -BaseBytes 100000 -HeadBytes 110000)) '+9.77 KB (+10.00%)' 'grew cell shows +size (+pct)'
Assert-Match  (Format-SizeDeltaCell (Get-SizeDelta -BaseBytes 110000 -HeadBytes 100000)) '(-9.09%)' 'shrank cell shows negative pct'
Assert-Match  (Format-SizeDeltaCell (Get-SizeDelta -BaseBytes $null -HeadBytes 5000)) 'new'     'added cell labelled new'
Assert-Match  (Format-SizeDeltaCell (Get-SizeDelta -BaseBytes 5000 -HeadBytes $null)) 'removed' 'removed cell labelled removed'

# ── ConvertTo-MeasurementMap ─────────────────────────────────────────────────
$map = ConvertTo-MeasurementMap -Measurements @(
    [pscustomobject]@{ Key = 'a'; Label = 'A'; Bytes = 1 }
    [pscustomobject]@{ Key = 'b'; Label = 'B'; Bytes = 2 }
    [pscustomobject]@{ Key = 'a'; Label = 'A2'; Bytes = 3 }
)
Assert-Equal 2   $map.Keys.Count      'map dedupes by key'
Assert-Equal 3   $map['a'].Bytes      'later entry wins'
Assert-Equal 0   (ConvertTo-MeasurementMap -Measurements $null).Keys.Count 'null -> empty map'

# ── Format-BuildMetricsComment ───────────────────────────────────────────────
$base = @(
    [pscustomobject]@{ Key = 'nupkg.Reactor'; Label = 'Microsoft.UI.Reactor.nupkg'; Group = 'Packages (compressed)'; Bytes = 1200000 }
    [pscustomobject]@{ Key = 'asm.Reactor';   Label = 'Reactor.dll';                Group = 'Assemblies (uncompressed)'; Bytes = 900000 }
    [pscustomobject]@{ Key = 'nupkg.Legacy';  Label = 'Legacy.nupkg';               Group = 'Packages (compressed)'; Bytes = 4000 }
)
$head = @(
    [pscustomobject]@{ Key = 'nupkg.Reactor'; Label = 'Microsoft.UI.Reactor.nupkg'; Group = 'Packages (compressed)'; Bytes = 1260000 }
    [pscustomobject]@{ Key = 'asm.Reactor';   Label = 'Reactor.dll';                Group = 'Assemblies (uncompressed)'; Bytes = 900010 }
    [pscustomobject]@{ Key = 'nupkg.New';     Label = 'Brand.New.nupkg';            Group = 'Packages (compressed)'; Bytes = 7000 }
)
$comment = Format-BuildMetricsComment -BaseMeasurements $base -HeadMeasurements $head -HeadSha 'abcdef1234567' -BaseSha '1234567890abc' -RunUrl 'https://example/run/1'
Assert-Match $comment '<!-- reactor-build-metrics -->' 'comment carries the sticky marker'
Assert-Match $comment 'Build metrics'                  'comment has heading'
Assert-Match $comment 'abcdef1'                        'head sha shortened to 7'
Assert-Match $comment '### Packages (compressed)'      'packages group header'
Assert-Match $comment '### Assemblies (uncompressed)'  'assemblies group header'
Assert-Match $comment 'Microsoft.UI.Reactor.nupkg'     'lists the framework package'
Assert-Match $comment 'Brand.New.nupkg'                'head-only artifact listed (added)'
Assert-Match $comment 'Legacy.nupkg'                   'base-only artifact listed (removed)'
Assert-Match $comment 'workflow run'                   'footer links the run'
# The header + column use "base" (accurate for any base branch), not "main".
Assert-Match    $comment 'vs the base branch'          'header uses base-branch wording, not hard-coded main'
Assert-Match    $comment 'base | PR'                   'table column header says base'
Assert-NotMatch $comment 'main | PR'                   'table column header is not the hard-coded main'
# The tiny asm.Reactor delta (10 bytes) must read as unchanged, not a regression.
Assert-Match $comment "$([char]0x2248)"                'within-noise glyph present for tiny delta'

# All-unchanged rendering: the "no change" note appears, no grew/shrank rows.
$flat = @(
    [pscustomobject]@{ Key = 'nupkg.Reactor'; Label = 'Microsoft.UI.Reactor.nupkg'; Group = 'Packages (compressed)'; Bytes = 1200000 }
)
$flatComment = Format-BuildMetricsComment -BaseMeasurements $flat -HeadMeasurements $flat -HeadSha 'aaa' -BaseSha 'bbb'
Assert-Match $flatComment 'No size change beyond the noise floor' 'flat comparison notes no change'

# Failure mode: emits a caution block, not tables.
$failComment = Format-BuildMetricsComment -Failed -RunUrl 'https://example/run/2'
Assert-Match    $failComment '<!-- reactor-build-metrics -->' 'failure comment keeps the marker'
Assert-Match    $failComment '[!CAUTION]'                     'failure comment is a caution admonition'
Assert-NotMatch $failComment '| Artifact |'                  'failure comment has no size table'

# Baseline-unavailable mode: head sizes render, but with no misleading "added"
# rows and a warning that the delta is unavailable.
$baseGoneComment = Format-BuildMetricsComment -HeadMeasurements $head -BaselineUnavailable -HeadSha 'aaa' -BaseSha 'bbb'
Assert-Match    $baseGoneComment '[!WARNING]'                 'baseline-unavailable emits a warning'
Assert-Match    $baseGoneComment 'Microsoft.UI.Reactor.nupkg' 'baseline-unavailable still lists head artifacts'
Assert-NotMatch $baseGoneComment '<sub>new</sub>'            'baseline-unavailable does not mislabel rows as new'
Assert-NotMatch $baseGoneComment 'No size change beyond'      'baseline-unavailable suppresses the no-change note'

# ── Get-BuildMetricsTargetSpec / ConvertTo-SafeMeasurements (security boundary) ─
$spec = Get-BuildMetricsTargetSpec
Assert-Equal 6 $spec.Count 'target spec has all six tracked artifacts'
Assert-Equal 'Microsoft.UI.Reactor.nupkg' ($spec | Where-Object { $_.Key -eq 'nupkg.Reactor' }).Label 'spec maps the framework nupkg label'

# Well-formed raw input: trusted labels applied, numeric bytes preserved, order = spec.
$raw = @(
    [pscustomobject]@{ Key = 'asm.Reactor';   Label = 'IGNORED';  Group = 'IGNORED'; Bytes = 3384320 }
    [pscustomobject]@{ Key = 'nupkg.Reactor'; Label = 'IGNORED';  Group = 'IGNORED'; Bytes = 2034863 }
)
$safe = ConvertTo-SafeMeasurements -RawMeasurements $raw
Assert-Equal 6 $safe.Count 'safe measurements always cover the full spec'
Assert-Equal 'nupkg.Reactor' $safe[0].Key 'safe output is in spec order (nupkg.Reactor first)'
Assert-Equal 'Microsoft.UI.Reactor.nupkg' $safe[0].Label 'safe output uses the TRUSTED label, not the artifact label'
Assert-Equal 2034863 $safe[0].Bytes 'valid integer bytes preserved'
Assert-Equal 3384320 ($safe | Where-Object { $_.Key -eq 'asm.Reactor' }).Bytes 'valid bytes preserved regardless of raw order'

# Security: a malicious Label/Group in the artifact must never reach the output.
$evil = @([pscustomobject]@{ Key = 'nupkg.Reactor'; Label = '<img src=x onerror=alert(1)>'; Group = '[!WARNING] pwned'; Bytes = 100 })
$safeEvil = ConvertTo-SafeMeasurements -RawMeasurements $evil
Assert-Equal 'Microsoft.UI.Reactor.nupkg' $safeEvil[0].Label 'injected label is dropped for the trusted label'
Assert-NotMatch (($safeEvil | ForEach-Object { $_.Label + '|' + $_.Group }) -join ' ') 'onerror' 'no injected markup survives sanitization'

# Unknown keys are dropped; non-integer / negative / decimal bytes become null.
$junk = @(
    [pscustomobject]@{ Key = 'nupkg.Evil';    Label = 'x'; Group = 'y'; Bytes = 5 }   # unknown key -> dropped
    [pscustomobject]@{ Key = 'asm.Reactor';   Label = 'x'; Group = 'y'; Bytes = 'not-a-number' }
    [pscustomobject]@{ Key = 'nupkg.Advanced';Label = 'x'; Group = 'y'; Bytes = -50 }
    [pscustomobject]@{ Key = 'asm.Advanced';  Label = 'x'; Group = 'y'; Bytes = '1e9' }
)
$safeJunk = ConvertTo-SafeMeasurements -RawMeasurements $junk
Assert-True ((@($safeJunk | Where-Object { $_.Key -eq 'nupkg.Evil' })).Count -eq 0) 'unknown key dropped'
Assert-Null ($safeJunk | Where-Object { $_.Key -eq 'asm.Reactor' }).Bytes   'non-numeric bytes -> null'
Assert-Null ($safeJunk | Where-Object { $_.Key -eq 'nupkg.Advanced' }).Bytes 'negative bytes -> null'
Assert-Null ($safeJunk | Where-Object { $_.Key -eq 'asm.Advanced' }).Bytes   'scientific-notation bytes -> null'

# Null input -> full spec with all-null bytes (renders as a "failed" set).
$safeNull = ConvertTo-SafeMeasurements -RawMeasurements $null
Assert-Equal 6 $safeNull.Count 'null raw -> full spec'
Assert-Equal 0 (@($safeNull | Where-Object { $null -ne $_.Bytes }).Count) 'null raw -> all bytes null'

# End-to-end: a sanitized set renders a normal comment with trusted labels only.
$safeComment = Format-BuildMetricsComment -BaseMeasurements $safeNull -HeadMeasurements $safe -HeadSha 'abc1234' -BaseSha 'def5678'
Assert-Match    $safeComment 'Microsoft.UI.Reactor.nupkg' 'sanitized render shows trusted labels'
Assert-NotMatch $safeComment 'IGNORED'                    'sanitized render drops artifact-supplied labels'

# ── Summary ──────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host "BuildMetricsLib tests: $script:Pass passed, $script:Fail failed."
if ($script:Fail -gt 0) {
    Write-Host ''
    Write-Host 'Failures:' -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    exit 1
}
exit 0
