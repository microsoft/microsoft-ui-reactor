<#
.SYNOPSIS
    Dependency-free unit tests for CoverageLib.ps1 (the pure cobertura parser /
    percent-format / delta / renderer used by the automatic merged-coverage PR
    comment).

.DESCRIPTION
    Runs headless with no build, no instrumentation, and no external test
    framework, so it is safe on any runner (it is wired into
    .github/workflows/coverage-lib-tests.yml on changes under tests/coverage/ci/**).
    Exits non-zero if any assertion fails.

    Run locally:  pwsh tests/coverage/ci/CoverageLib.Tests.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'CoverageLib.ps1')

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
    # Ordinal substring test — NOT -like, whose wildcard metacharacters ([ ] * ?)
    # would make needles such as '[!CAUTION]' match without the literal text.
    if ($Haystack.Contains($Needle)) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add("$Message`n    missing substring: [$Needle]") }
}

function Assert-NotMatch {
    param([string]$Haystack, [string]$Needle, [string]$Message)
    if (-not $Haystack.Contains($Needle)) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add("$Message`n    unexpected substring: [$Needle]") }
}

# ── Get-CoberturaRatesFromXml ────────────────────────────────────────────────
[xml]$doc = @'
<coverage line-rate="0.8610" branch-rate="1">
  <packages>
    <package>
      <classes>
        <class>
          <lines>
            <line number="1" hits="1" />
            <line number="2" hits="1" condition-coverage="50% (1/2)" />
            <line number="3" hits="0" condition-coverage="75% (3/4)" />
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>
'@
$r = Get-CoberturaRatesFromXml -Doc $doc
Assert-Equal 86.1  $r.Line            'line-rate 0.8610 -> 86.1%'
Assert-Equal 66.67 $r.Branch          'branch = 100*(1+3)/(2+4) = 66.67%'
Assert-Equal 4     $r.BranchesCovered 'branch covered summed (1+3)'
Assert-Equal 6     $r.BranchesTotal   'branch total summed (2+4)'

# No conditional branches at all -> branch metrics null, line still parsed.
[xml]$docNoBranch = '<coverage line-rate="0.5"><packages><package><classes><class><lines><line number="1" hits="1" /></lines></class></classes></package></packages></coverage>'
$r2 = Get-CoberturaRatesFromXml -Doc $docNoBranch
Assert-Equal 50 $r2.Line          'line-rate 0.5 -> 50%'
Assert-Null  $r2.Branch           'no condition-coverage -> null branch'
Assert-Null  $r2.BranchesCovered  'no condition-coverage -> null covered'
Assert-Null  $r2.BranchesTotal    'no condition-coverage -> null total'

# Missing line-rate -> null line (no crash).
[xml]$docNoLine = '<coverage branch-rate="1"><packages /></coverage>'
$r3 = Get-CoberturaRatesFromXml -Doc $docNoLine
Assert-Null $r3.Line 'missing line-rate -> null line'

# ── ConvertTo-SafePercent ────────────────────────────────────────────────────
Assert-Equal 87.3 (ConvertTo-SafePercent '87.30') 'valid decimal percent parses'
Assert-Equal 0    (ConvertTo-SafePercent '0')     'zero percent'
Assert-Equal 100  (ConvertTo-SafePercent '100')   '100 percent allowed'
Assert-Null  (ConvertTo-SafePercent $null)        'null -> null'
Assert-Null  (ConvertTo-SafePercent '')           'empty -> null'
Assert-Null  (ConvertTo-SafePercent '150')        '>100 rejected'
Assert-Null  (ConvertTo-SafePercent '-5')         'negative/sign rejected'
Assert-Null  (ConvertTo-SafePercent '1e2')        'scientific notation rejected'
Assert-Null  (ConvertTo-SafePercent '87.3|<img>') 'markdown/pipe injection rejected'
Assert-Null  (ConvertTo-SafePercent 'NaN')        'non-numeric rejected'

# ── ConvertTo-SafeCount ──────────────────────────────────────────────────────
Assert-Equal 510 (ConvertTo-SafeCount '510') 'valid integer count'
Assert-Equal 0   (ConvertTo-SafeCount '0')   'zero count'
Assert-Null  (ConvertTo-SafeCount $null)     'null count -> null'
Assert-Null  (ConvertTo-SafeCount '-1')      'negative count -> null'
Assert-Null  (ConvertTo-SafeCount '1.5')     'decimal count -> null'
Assert-Null  (ConvertTo-SafeCount '1e9')     'scientific count -> null'
Assert-Null  (ConvertTo-SafeCount '5; evil') 'injection count -> null'

# ── ConvertTo-SafeCoverageMetrics (security boundary) ────────────────────────
$safe = ConvertTo-SafeCoverageMetrics ([pscustomobject]@{ line = '87.30'; branch = '75.2'; branchesCovered = '510'; branchesTotal = '678' })
Assert-Equal 87.3 $safe.Line            'safe metrics keep valid line'
Assert-Equal 75.2 $safe.Branch          'safe metrics keep valid branch'
Assert-Equal 510  $safe.BranchesCovered 'safe metrics keep valid covered'
Assert-Equal 678  $safe.BranchesTotal   'safe metrics keep valid total'

$evil = ConvertTo-SafeCoverageMetrics ([pscustomobject]@{ line = '99|<script>alert(1)</script>'; branch = '-5'; branchesCovered = '1e9'; branchesTotal = 'DROP TABLE' })
Assert-Null $evil.Line            'injected line dropped'
Assert-Null $evil.Branch          'signed branch dropped'
Assert-Null $evil.BranchesCovered 'scientific covered dropped'
Assert-Null $evil.BranchesTotal   'non-numeric total dropped'

$nullMetrics = ConvertTo-SafeCoverageMetrics $null
Assert-Null $nullMetrics.Line   'null raw -> null line'
Assert-Null $nullMetrics.Branch 'null raw -> null branch'
Assert-True (-not (Test-CoverageMetricsPresent $nullMetrics)) 'all-null metrics are not present'
Assert-True (Test-CoverageMetricsPresent $safe)               'valid metrics are present'

# ── Format-Percent / Format-BranchCell / Format-SignedPoints ─────────────────
Assert-Equal '87.30%' (Format-Percent 87.3)  'percent formats to 2 dp'
Assert-Equal '86.10%' (Format-Percent 86.1)  'percent pads to 2 dp'
Assert-Equal 'n/a'    (Format-Percent $null) 'null percent -> n/a'

Assert-Equal '74.50% (500/671)' (Format-BranchCell 74.5 500 671) 'branch cell shows counts'
Assert-Equal '74.50%'           (Format-BranchCell 74.5 $null $null) 'branch cell without counts'
Assert-Equal 'n/a'              (Format-BranchCell $null 1 2) 'null branch cell -> n/a'

Assert-Equal '+1.20 pp' (Format-SignedPoints 1.2)   'positive pp gets +'
Assert-Equal '-0.30 pp' (Format-SignedPoints -0.3)  'negative pp keeps -'
Assert-Equal '0.00 pp'  (Format-SignedPoints 0)     'zero pp no sign'
Assert-Equal 'n/a'      (Format-SignedPoints $null) 'null pp -> n/a'

# ── Get-CoverageDelta ────────────────────────────────────────────────────────
$d = Get-CoverageDelta -BasePct 86.1 -HeadPct 87.3
Assert-Equal 'up'  $d.Status      'rise beyond floor -> up'
Assert-Equal 1.2   $d.DeltaPoints 'up delta points'
Assert-True  $d.Improved          'up is the improvement'

$d = Get-CoverageDelta -BasePct 87.3 -HeadPct 86.1
Assert-Equal 'down' $d.Status      'fall beyond floor -> down'
Assert-Equal -1.2   $d.DeltaPoints 'down delta points (negative)'
Assert-True  (-not $d.Improved)    'down is not an improvement'

# Below the noise floor -> unchanged (delta still reported).
$d = Get-CoverageDelta -BasePct 86.10 -HeadPct 86.15
Assert-Equal 'unchanged' $d.Status      'tiny delta -> unchanged'
Assert-Equal 0.05        $d.DeltaPoints 'unchanged still reports the delta'
Assert-Null  $d.Improved                'unchanged has no improvement flag'

# Exactly at the floor counts as a real move.
$d = Get-CoverageDelta -BasePct 86.0 -HeadPct 86.1
Assert-Equal 'up' $d.Status 'delta == floor (0.1) -> up'

# Missing either side -> na.
Assert-Equal 'na' (Get-CoverageDelta -BasePct $null -HeadPct 87.3).Status 'missing base -> na'
Assert-Equal 'na' (Get-CoverageDelta -BasePct 86.1 -HeadPct $null).Status 'missing head -> na'
Assert-Null  (Get-CoverageDelta -BasePct $null -HeadPct $null).DeltaPoints 'na has no delta'

# ── Get-CoverageStatusGlyph / Format-CoverageDeltaCell ───────────────────────
Assert-Equal "$([char]0x2705)"                (Get-CoverageStatusGlyph 'up')        'up -> check'
Assert-Equal "$([char]0x26A0)$([char]0xFE0F)" (Get-CoverageStatusGlyph 'down')      'down -> warning'
Assert-Equal "$([char]0x2248)"                (Get-CoverageStatusGlyph 'unchanged') 'unchanged -> approx'
Assert-Equal "$([char]0x2014)"                (Get-CoverageStatusGlyph 'na')        'na -> dash'

Assert-Equal "$([char]0x2014)" (Format-CoverageDeltaCell (Get-CoverageDelta -BasePct $null -HeadPct 87.3)) 'na cell -> dash'
Assert-Equal '+1.20 pp'        (Format-CoverageDeltaCell (Get-CoverageDelta -BasePct 86.1 -HeadPct 87.3))  'up cell -> signed pp'

# ── Format-CoverageComment: full comparison ──────────────────────────────────
$base = [pscustomobject]@{ Line = 86.1; Branch = 74.5; BranchesCovered = 500; BranchesTotal = 671 }
$head = [pscustomobject]@{ Line = 87.3; Branch = 75.2; BranchesCovered = 510; BranchesTotal = 678 }
$comment = Format-CoverageComment -BaseMetrics $base -HeadMetrics $head -HeadSha 'abcdef1234567' -BaseSha '1234567890abc' -RunUrl 'https://example/run/1'
Assert-Match $comment '<!-- reactor-coverage -->' 'comment carries the sticky marker'
Assert-Match $comment 'Merged coverage'           'comment has heading'
Assert-Match $comment 'abcdef1'                    'head sha shortened to 7'
Assert-Match $comment '1234567'                    'base sha shortened to 7'
Assert-Match $comment 'vs the base branch'         'header uses base-branch wording'
Assert-Match $comment 'Metric | base | PR'         'table column header says base | PR'
Assert-NotMatch $comment 'main | PR'               'table column header is not hard-coded main'
Assert-Match $comment '| Line   | 86.10% | 87.30% | +1.20 pp |' 'line row shows base, PR, +delta'
Assert-Match $comment '74.50% (500/671)'           'branch base cell shows counts'
Assert-Match $comment '75.20% (510/678)'           'branch PR cell shows counts'
Assert-Match $comment '+0.70 pp'                   'branch delta rendered'
Assert-Match $comment "$([char]0x2705)"            'improvement glyph present'
Assert-Match $comment 'workflow run'               'footer links the run'
Assert-Match $comment 'percentage points'          'legend explains the pp unit'

# Regression rendering: PR lower than base -> warning glyph + negative delta.
$worse = [pscustomobject]@{ Line = 84.0; Branch = 70.0; BranchesCovered = 400; BranchesTotal = 671 }
$regressed = Format-CoverageComment -BaseMetrics $base -HeadMetrics $worse -HeadSha 'aaa' -BaseSha 'bbb'
Assert-Match $regressed '-2.10 pp'                  'line regression shows negative pp'
Assert-Match $regressed "$([char]0x26A0)$([char]0xFE0F)" 'regression shows warning glyph'
Assert-NotMatch $regressed 'No coverage change beyond' 'regression is not reported as no-change'

# All-unchanged rendering: the "no change" note appears, no up/down rows.
$flatComment = Format-CoverageComment -BaseMetrics $base -HeadMetrics $base -HeadSha 'aaa' -BaseSha 'bbb'
Assert-Match $flatComment 'No coverage change beyond the noise floor' 'flat comparison notes no change'
Assert-Match $flatComment '0.00 pp'                                   'flat comparison shows a zero delta'

# Failure mode: emits a caution block, not a table.
$failComment = Format-CoverageComment -Failed -RunUrl 'https://example/run/2'
Assert-Match    $failComment '<!-- reactor-coverage -->' 'failure comment keeps the marker'
Assert-Match    $failComment '[!CAUTION]'                'failure comment is a caution admonition'
Assert-NotMatch $failComment '| Metric |'               'failure comment has no coverage table'

# A present-looking call whose head has no real numbers also renders the failure body.
$noHead = Format-CoverageComment -HeadMetrics ([pscustomobject]@{ Line = $null; Branch = $null; BranchesCovered = $null; BranchesTotal = $null }) -RunUrl 'https://example/run/3'
Assert-Match $noHead '[!CAUTION]' 'null head metrics -> caution body'

# Baseline-unavailable mode: head coverage renders, delta is a dash, warning shown.
$baseGone = Format-CoverageComment -HeadMetrics $head -BaselineUnavailable -HeadSha 'aaa' -BaseSha 'bbb' -RunUrl 'https://example/run/4'
Assert-Match    $baseGone '[!WARNING]'                 'baseline-unavailable emits a warning'
Assert-Match    $baseGone '87.30%'                     'baseline-unavailable still shows PR line coverage'
Assert-Match    $baseGone "| Line   | n/a | 87.30% | $([char]0x2014) |" 'baseline-unavailable shows n/a base and a dash delta'
Assert-NotMatch $baseGone 'No coverage change beyond'  'baseline-unavailable suppresses the no-change note'
Assert-NotMatch $baseGone '+0.00 pp'                   'baseline-unavailable shows no signed delta'

# Security end-to-end: an injected value in the raw record never reaches markdown.
$evilRaw = ConvertTo-SafeCoverageMetrics ([pscustomobject]@{ line = '<img src=x onerror=alert(1)>'; branch = '75.2'; branchesCovered = '510'; branchesTotal = '678' })
$evilComment = Format-CoverageComment -BaseMetrics $base -HeadMetrics $evilRaw -HeadSha 'aaa' -BaseSha 'bbb'
Assert-NotMatch $evilComment 'onerror' 'sanitized metrics never inject markup into the comment'

# ── Format-CoverageComment: no-baseline (branch dispatch) mode ────────────────
$nb = Format-CoverageComment -HeadMetrics $head -NoBaseline -HeadSha 'abcdef1234567' -RunUrl 'https://example/run/5'
Assert-Match    $nb '<!-- reactor-coverage -->' 'no-baseline keeps the marker'
Assert-Match    $nb '| Metric | Coverage |'     'no-baseline uses a plain two-column table'
Assert-Match    $nb '| Line   | 87.30% |'       'no-baseline shows absolute line coverage'
Assert-Match    $nb '| Branch | 75.20% (510/678) |' 'no-baseline shows absolute branch coverage'
Assert-NotMatch $nb 'vs the base branch'        'no-baseline omits the base-branch wording'
Assert-NotMatch $nb '[!WARNING]'                'no-baseline is not a warning'
Assert-NotMatch $nb ' pp'                        'no-baseline shows no delta column'
Assert-NotMatch $nb ([string][char]0x0394)      'no-baseline has no delta header'

# ── Format-CoverageCommentFromMetrics: the shared render-mode selector ────────
$present = ConvertTo-SafeCoverageMetrics ([pscustomobject]@{ line = '87.3'; branch = '75.2'; branchesCovered = '510'; branchesTotal = '678' })
$basePresent = ConvertTo-SafeCoverageMetrics ([pscustomobject]@{ line = '86.1'; branch = '74.5'; branchesCovered = '500'; branchesTotal = '671' })
$absent = ConvertTo-SafeCoverageMetrics $null

# head absent -> Failed body.
$selFailed = Format-CoverageCommentFromMetrics -HeadMetrics $absent -BaseMetrics $basePresent -HasBase $true -HeadSha 'aaa' -BaseSha 'bbb'
Assert-Match    $selFailed '[!CAUTION]'          'selector: absent head -> failed body'
Assert-NotMatch $selFailed '| Metric |'          'selector: failed body has no table'

# head + base present, HasBase true -> full comparison.
$selCompare = Format-CoverageCommentFromMetrics -HeadMetrics $present -BaseMetrics $basePresent -HasBase $true -HeadSha 'aaa' -BaseSha 'bbb'
Assert-Match    $selCompare 'Metric | base | PR' 'selector: head+base -> comparison table'
Assert-Match    $selCompare '+1.20 pp'           'selector: comparison shows the delta'
Assert-NotMatch $selCompare '[!WARNING]'         'selector: comparison has no warning'

# head present, base absent, HasBase true -> baseline unavailable.
$selUnavail = Format-CoverageCommentFromMetrics -HeadMetrics $present -BaseMetrics $absent -HasBase $true -HeadSha 'aaa' -BaseSha 'bbb'
Assert-Match    $selUnavail '[!WARNING]'          'selector: missing base -> baseline-unavailable'
Assert-Match    $selUnavail '87.30%'              'selector: baseline-unavailable still shows head'

# head present, HasBase false -> no-baseline absolute (branch dispatch).
$selNoBase = Format-CoverageCommentFromMetrics -HeadMetrics $present -BaseMetrics $absent -HasBase $false -HeadSha 'aaa'
Assert-Match    $selNoBase '| Metric | Coverage |' 'selector: no base -> absolute table'
Assert-NotMatch $selNoBase 'vs the base branch'    'selector: no base omits base-branch wording'
Assert-NotMatch $selNoBase '[!WARNING]'            'selector: no base is not a warning'

# ── Summary ──────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host "CoverageLib tests: $script:Pass passed, $script:Fail failed."
if ($script:Fail -gt 0) {
    Write-Host ''
    Write-Host 'Failures:' -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    exit 1
}
exit 0
