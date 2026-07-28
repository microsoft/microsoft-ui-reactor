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
    [pscustomobject]@{ Key = 'nupkg.Reactor'; Label = 'Microsoft.UI.Reactor.nupkg'; Group = 'Packages (compressed .nupkg)'; Bytes = 1200000 }
    [pscustomobject]@{ Key = 'asm|Reactor|Reactor.dll'; Label = 'Reactor.dll';       Group = 'Assemblies in Microsoft.UI.Reactor'; Bytes = 900000 }
    [pscustomobject]@{ Key = 'nupkg.Legacy';  Label = 'Legacy.nupkg';               Group = 'Packages (compressed .nupkg)'; Bytes = 4000 }
)
$head = @(
    [pscustomobject]@{ Key = 'nupkg.Reactor'; Label = 'Microsoft.UI.Reactor.nupkg'; Group = 'Packages (compressed .nupkg)'; Bytes = 1260000 }
    [pscustomobject]@{ Key = 'asm|Reactor|Reactor.dll'; Label = 'Reactor.dll';       Group = 'Assemblies in Microsoft.UI.Reactor'; Bytes = 900010 }
    [pscustomobject]@{ Key = 'nupkg.New';     Label = 'Brand.New.nupkg';            Group = 'Packages (compressed .nupkg)'; Bytes = 7000 }
)
$comment = Format-BuildMetricsComment -BaseMeasurements $base -HeadMeasurements $head -HeadSha 'abcdef1234567' -BaseSha '1234567890abc' -RunUrl 'https://example/run/1'
Assert-Match $comment '<!-- reactor-build-metrics -->' 'comment carries the sticky marker'
Assert-Match $comment 'Build metrics'                  'comment has heading'
Assert-Match $comment 'abcdef1'                        'head sha shortened to 7'
Assert-Match $comment '### Packages (compressed .nupkg)'      'packages group header'
Assert-Match $comment '### Assemblies in Microsoft.UI.Reactor' 'assemblies group header'
Assert-Match $comment 'Microsoft.UI.Reactor.nupkg'     'lists the framework package'
Assert-Match $comment 'Brand.New.nupkg'                'head-only artifact listed (added)'
Assert-Match $comment 'Legacy.nupkg'                   'base-only artifact listed (removed)'
Assert-Match $comment 'workflow run'                   'footer links the run'
# The header + column use "base" (accurate for any base branch), not "main".
Assert-Match    $comment 'vs the base branch'          'header uses base-branch wording, not hard-coded main'
Assert-Match    $comment 'base | PR'                   'table column header says base'
Assert-NotMatch $comment 'main | PR'                   'table column header is not the hard-coded main'
# The tiny Reactor.dll delta (10 bytes) must read as unchanged, not a regression.
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

# ── Get-BuildMetricsPackageSpec / ConvertTo-SafeMeasurements (security boundary) ─
$spec = Get-BuildMetricsPackageSpec
Assert-Equal 3 $spec.Count 'package spec has all three tracked packages'
Assert-Equal 'Microsoft.UI.Reactor.nupkg' ($spec | Where-Object { $_.PkgKey -eq 'Reactor' }).PkgLabel 'spec maps the framework nupkg label'
Assert-Equal 'Assemblies in Microsoft.UI.Reactor' ($spec | Where-Object { $_.PkgKey -eq 'Reactor' }).AssemblyGroup 'spec maps the framework assembly group'

# Well-formed raw input: every shipped DLL row is projected onto its package's
# trusted assembly group, the nupkg rows keep trusted labels, and byte counts are
# preserved. nupkg rows come first (spec order), then per-DLL rows sorted by name.
$raw = @(
    [pscustomobject]@{ Key = 'nupkg.Reactor';                       Label = 'IGNORED'; Group = 'IGNORED'; Bytes = 2034863 }
    [pscustomobject]@{ Key = 'asm|Reactor|Reactor.dll';             Label = 'IGNORED'; Group = 'IGNORED'; Bytes = 3384320 }
    [pscustomobject]@{ Key = 'asm|Reactor|Reactor.Analyzers.dll';   Label = 'IGNORED'; Group = 'IGNORED'; Bytes = 303104 }
    [pscustomobject]@{ Key = 'asm|Reactor|Reactor.Wrappers.Abstractions.dll'; Label = 'IGNORED'; Group = 'IGNORED'; Bytes = 10240 }
    [pscustomobject]@{ Key = 'nupkg.Advanced';                      Label = 'IGNORED'; Group = 'IGNORED'; Bytes = 33792 }
    [pscustomobject]@{ Key = 'asm|Advanced|Reactor.Advanced.dll';   Label = 'IGNORED'; Group = 'IGNORED'; Bytes = 33792 }
    [pscustomobject]@{ Key = 'nupkg.Devtools';                      Label = 'IGNORED'; Group = 'IGNORED'; Bytes = 442368 }
    [pscustomobject]@{ Key = 'asm|Devtools|Microsoft.UI.Reactor.Devtools.dll'; Label = 'IGNORED'; Group = 'IGNORED'; Bytes = 442368 }
)
$safe = ConvertTo-SafeMeasurements -RawMeasurements $raw
# 3 nupkg rows + 3 valid Reactor DLLs + 1 Advanced DLL + 1 Devtools DLL.
Assert-Equal 8 $safe.Count 'safe measurements: 3 nupkg rows + all validated DLL rows'
Assert-Equal 'nupkg.Reactor' $safe[0].Key 'safe output starts with the nupkg rows in spec order'
Assert-Equal 'Microsoft.UI.Reactor.nupkg' $safe[0].Label 'safe output uses the TRUSTED package label, not the artifact label'
Assert-Equal 'nupkg.Advanced' $safe[1].Key 'second nupkg row is Advanced (spec order)'
Assert-Equal 'nupkg.Devtools' $safe[2].Key 'third nupkg row is Devtools (spec order)'
Assert-Equal 442368 $safe[2].Bytes 'Devtools nupkg bytes preserved'
Assert-Equal 2034863 $safe[0].Bytes 'valid integer nupkg bytes preserved'

# Per-DLL rows: validated filename is the label, package assembly group is the group.
$reactorDlls = @($safe | Where-Object { $_.Group -eq 'Assemblies in Microsoft.UI.Reactor' })
Assert-Equal 3 $reactorDlls.Count 'all three Reactor DLL rows mapped to the framework assembly group'
Assert-Equal 'Reactor.Analyzers.dll' $reactorDlls[0].Label 'DLL rows sorted deterministically by filename (Analyzers first)'
Assert-Equal 'Reactor.Wrappers.Abstractions.dll' $reactorDlls[2].Label 'DLL rows sorted deterministically by filename (Wrappers last)'
Assert-Equal 3384320 ($reactorDlls | Where-Object { $_.Label -eq 'Reactor.dll' }).Bytes 'per-DLL bytes preserved'
Assert-Equal 'Reactor.Analyzers.dll' ($reactorDlls[0].Label) 'analyzer DLL displayed with its own filename'
$advancedDlls = @($safe | Where-Object { $_.Group -eq 'Assemblies in Microsoft.UI.Reactor.Advanced' })
Assert-Equal 1 $advancedDlls.Count 'Advanced package contributes its single DLL'
Assert-Equal 'Reactor.Advanced.dll' $advancedDlls[0].Label 'Advanced DLL uses its filename'
$devtoolsDlls = @($safe | Where-Object { $_.Group -eq 'Assemblies in Microsoft.UI.Reactor.Devtools' })
Assert-Equal 1 $devtoolsDlls.Count 'Devtools package contributes its single DLL'
Assert-Equal 'Microsoft.UI.Reactor.Devtools.dll' $devtoolsDlls[0].Label 'Devtools DLL uses its filename'

# Security: a malicious Label/Group in the artifact must never reach the output;
# the trusted labels/groups are used instead.
$evil = @(
    [pscustomobject]@{ Key = 'nupkg.Reactor';           Label = '<img src=x onerror=alert(1)>'; Group = '[!WARNING] pwned'; Bytes = 100 }
    [pscustomobject]@{ Key = 'asm|Reactor|Reactor.dll'; Label = '<script>evil</script>';        Group = '| pipes | here';  Bytes = 200 }
)
$safeEvil = ConvertTo-SafeMeasurements -RawMeasurements $evil
Assert-Equal 'Microsoft.UI.Reactor.nupkg' $safeEvil[0].Label 'injected package label is dropped for the trusted label'
$evilJoined = (($safeEvil | ForEach-Object { $_.Label + '|' + $_.Group }) -join ' ')
Assert-NotMatch $evilJoined 'onerror' 'no injected markup survives sanitization (label)'
Assert-NotMatch $evilJoined 'script'  'no injected markup survives sanitization (script)'

# Security: a malicious DLL FILENAME (the only artifact-derived string that can be
# rendered) must be rejected unless it passes the strict, case-sensitive,
# absolutely-anchored allowlist \A[A-Za-z0-9._+-]+\.dll\z.
$kelvinDll = [string]([char]0x212A) + '.dll'   # U+212A case-folds to 'k' under IgnoreCase
$badNames = @(
    [pscustomobject]@{ Key = 'asm|Reactor|<img>';            Label = 'x'; Group = 'y'; Bytes = 1 }  # angle brackets
    [pscustomobject]@{ Key = 'asm|Reactor|a|b.dll';          Label = 'x'; Group = 'y'; Bytes = 2 }  # embedded pipe -> 4-part key
    [pscustomobject]@{ Key = 'asm|Reactor|x.dll`bad`';       Label = 'x'; Group = 'y'; Bytes = 3 }  # backticks
    [pscustomobject]@{ Key = 'asm|Reactor|..\evil.dll';      Label = 'x'; Group = 'y'; Bytes = 4 }  # traversal / backslash
    [pscustomobject]@{ Key = 'asm|Reactor|../evil.dll';      Label = 'x'; Group = 'y'; Bytes = 4 }  # traversal / forward slash (ZIP path form)
    [pscustomobject]@{ Key = 'asm|Reactor|no-extension';     Label = 'x'; Group = 'y'; Bytes = 5 }  # not a .dll
    [pscustomobject]@{ Key = 'asm|Reactor|has space.dll';    Label = 'x'; Group = 'y'; Bytes = 6 }  # space
    [pscustomobject]@{ Key = 'asm|Unknown|Good.dll';         Label = 'x'; Group = 'y'; Bytes = 7 }  # unknown PkgKey
    [pscustomobject]@{ Key = "asm|Reactor|Trailing.dll`n";   Label = 'x'; Group = 'y'; Bytes = 9 }  # trailing newline ('$' would allow; '\z' rejects)
    [pscustomobject]@{ Key = "asm|Reactor|$kelvinDll";       Label = 'x'; Group = 'y'; Bytes = 10 } # Unicode case-fold (rejected by -cnotmatch)
    [pscustomobject]@{ Key = 'asm|Reactor|Legit.dll';        Label = 'x'; Group = 'y'; Bytes = 8 }  # the one valid row
)
$safeBad = ConvertTo-SafeMeasurements -RawMeasurements $badNames
$asmRows = @($safeBad | Where-Object { $_.Group -like 'Assemblies in*' })
Assert-Equal 1 $asmRows.Count 'only the single well-formed DLL filename is kept'
Assert-Equal 'Legit.dll' $asmRows[0].Label 'the kept row is the allowlisted filename'
$safeBadJoined = (($safeBad | ForEach-Object { [string]$_.Label }) -join ' ')
Assert-NotMatch $safeBadJoined 'img'          'angle-bracket filename dropped'
Assert-NotMatch $safeBadJoined 'evil'         'path-traversal filename dropped (both slash styles)'
Assert-NotMatch $safeBadJoined 'bad'          'backtick filename dropped'
Assert-NotMatch $safeBadJoined 'no-extension' 'non-.dll name dropped'
Assert-NotMatch $safeBadJoined 'space'        'filename with a space dropped'
Assert-NotMatch $safeBadJoined 'Good.dll'     'DLL under an unknown package key dropped'
Assert-NotMatch $safeBadJoined 'Trailing'     'filename with a trailing newline dropped (absolute \z anchor)'
Assert-True     (-not $safeBadJoined.Contains([string]([char]0x212A))) 'Unicode case-fold filename dropped (case-sensitive match)'

# Unknown package keys are dropped; non-integer / negative / decimal / scientific
# bytes become null.
$junk = @(
    [pscustomobject]@{ Key = 'nupkg.Evil';                Label = 'x'; Group = 'y'; Bytes = 5 }   # unknown key -> dropped
    [pscustomobject]@{ Key = 'asm|Reactor|Reactor.dll';   Label = 'x'; Group = 'y'; Bytes = 'not-a-number' }
    [pscustomobject]@{ Key = 'nupkg.Advanced';            Label = 'x'; Group = 'y'; Bytes = -50 }
    [pscustomobject]@{ Key = 'asm|Advanced|Reactor.Advanced.dll'; Label = 'x'; Group = 'y'; Bytes = '1e9' }
    [pscustomobject]@{ Key = 'asm|Devtools|Microsoft.UI.Reactor.Devtools.dll'; Label = 'x'; Group = 'y'; Bytes = '1.5' }
)
$safeJunk = ConvertTo-SafeMeasurements -RawMeasurements $junk
Assert-True ((@($safeJunk | Where-Object { $_.Key -eq 'nupkg.Evil' })).Count -eq 0) 'unknown package key dropped'
Assert-Null ($safeJunk | Where-Object { $_.Key -eq 'asm|Reactor|Reactor.dll' }).Bytes   'non-numeric DLL bytes -> null'
Assert-Null ($safeJunk | Where-Object { $_.Key -eq 'nupkg.Advanced' }).Bytes 'negative nupkg bytes -> null'
Assert-Null ($safeJunk | Where-Object { $_.Key -eq 'asm|Advanced|Reactor.Advanced.dll' }).Bytes 'scientific-notation bytes -> null'
Assert-Null ($safeJunk | Where-Object { $_.Key -eq 'asm|Devtools|Microsoft.UI.Reactor.Devtools.dll' }).Bytes 'decimal bytes -> null'

# Defense-in-depth: a malicious PR that emits an unbounded number of validly-shaped
# per-DLL rows must not flood the rendered comment. Rows are capped per package,
# and the kept subset is deterministic (sorted by filename, smallest names win).
$flood = for ($i = 0; $i -lt 500; $i++) {
    [pscustomobject]@{ Key = ('asm|Reactor|Flood{0:D4}.dll' -f $i); Label = 'x'; Group = 'y'; Bytes = 1 }
}
$safeFlood = ConvertTo-SafeMeasurements -RawMeasurements $flood
$reactorFlood = @($safeFlood | Where-Object { $_.Group -eq 'Assemblies in Microsoft.UI.Reactor' })
Assert-Equal 32 $reactorFlood.Count 'per-DLL rows hard-capped per package (flood bounded to 32)'
Assert-Equal 'Flood0000.dll' $reactorFlood[0].Label  'capped subset is deterministic (smallest filename kept first)'
Assert-Equal 'Flood0031.dll' $reactorFlood[31].Label 'capped subset keeps the lexicographically smallest 32 names'
Assert-True (-not ($safeFlood | Where-Object { $_.Label -eq 'Flood0032.dll' })) 'rows beyond the cap are dropped'

# Null input -> just the three nupkg rows with all-null bytes (renders as a "failed" set).
$safeNull = ConvertTo-SafeMeasurements -RawMeasurements $null
Assert-Equal 3 $safeNull.Count 'null raw -> the three package nupkg rows only'
Assert-Equal 0 (@($safeNull | Where-Object { $null -ne $_.Bytes }).Count) 'null raw -> all bytes null'

# End-to-end: a sanitized set renders a normal comment with trusted labels only,
# with each package's DLLs under its own assembly section.
$safeComment = Format-BuildMetricsComment -BaseMeasurements $safeNull -HeadMeasurements $safe -HeadSha 'abc1234' -BaseSha 'def5678'
Assert-Match    $safeComment 'Microsoft.UI.Reactor.nupkg'        'sanitized render shows trusted package labels'
Assert-Match    $safeComment '### Packages (compressed .nupkg)'  'sanitized render has the packages section first'
Assert-Match    $safeComment '### Assemblies in Microsoft.UI.Reactor' 'sanitized render has the per-package assembly section'
Assert-Match    $safeComment 'Reactor.Analyzers.dll'             'sanitized render lists the analyzer DLL'
Assert-Match    $safeComment 'Reactor.Wrappers.Abstractions.dll' 'sanitized render lists the abstractions DLL'
Assert-Match    $safeComment 'Reactor.Advanced.dll'              'sanitized render lists the Advanced DLL'
Assert-Match    $safeComment '### Assemblies in Microsoft.UI.Reactor.Devtools' 'sanitized render has the Devtools assembly section'
Assert-Match    $safeComment 'Microsoft.UI.Reactor.Devtools.dll' 'sanitized render lists the Devtools DLL'
Assert-NotMatch $safeComment 'IGNORED'                           'sanitized render drops artifact-supplied labels'
# The packages section must render before the assemblies sections.
$pkgIdx = $safeComment.IndexOf('### Packages')
$asmIdx = $safeComment.IndexOf('### Assemblies in')
Assert-True (($pkgIdx -ge 0) -and ($asmIdx -gt $pkgIdx)) 'nupkg section renders before the assemblies sections'

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
