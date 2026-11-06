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

function Assert-Throws {
    param([scriptblock]$Action, [string]$Like, [string]$Message)
    $threw = $false; $text = ''
    try { & $Action } catch { $threw = $true; $text = $_.Exception.Message }
    Assert-True ($threw -and $text -like $Like) "$Message (threw=$threw, message=[$text])"
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

# Method lines repeat their class's lines; only the class-level lines are counted.
[xml]$docWithMethods = @'
<coverage line-rate="1"><packages><package><classes><class>
  <methods><method><lines><line number="1" hits="1" condition-coverage="50% (1/2)" /></lines></method></methods>
  <lines><line number="1" hits="1" condition-coverage="50% (1/2)" /></lines>
</class></classes></package></packages></coverage>
'@
$r4 = Get-CoberturaRatesFromXml -Doc $docWithMethods
Assert-Equal 1 $r4.BranchesCovered 'a branch repeated on a method line is counted once (covered)'
Assert-Equal 2 $r4.BranchesTotal   'a branch repeated on a method line is counted once (total)'

# ── Merge-CoberturaDocuments ─────────────────────────────────────────────────
# Shaped like the lanes that `dotnet-coverage merge` failed to union (PR #1464). The
# selftest lane is instrumented statically: it covers FlexPanel's static constructor
# (lines 53-54) and names both constructors `FlexPanel()`, and it has no branch data.
# The unit lane is instrumented dynamically: it names them `.ctor` / `.cctor`, covers
# neither, and carries the branch data. It also has a class and a package the selftest
# lane lacks, so these union tests opt in with -AllowDifferentLines; the default refusal
# is tested further down.
[xml]$selftestLane = @'
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<coverage line-rate="0.6" branch-rate="1" complexity="1" version="1.9" timestamp="100" lines-covered="3" lines-valid="5">
  <packages>
    <package line-rate="0.6" branch-rate="1" complexity="1" name="Reactor">
      <classes>
        <class line-rate="0.6" branch-rate="1" complexity="1" name="Microsoft.UI.Reactor.Layout.FlexPanel" filename="/_/src/Reactor/Yoga/FlexPanel.cs">
          <methods>
            <method line-rate="1" branch-rate="1" complexity="1" name="FlexPanel" signature="()">
              <lines><line number="30" hits="1" branch="False" /></lines>
            </method>
            <method line-rate="1" branch-rate="1" complexity="1" name="FlexPanel" signature="()">
              <lines><line number="53" hits="1" branch="False" /><line number="54" hits="1" branch="False" /></lines>
            </method>
            <method line-rate="0" branch-rate="1" complexity="1" name="MeasureOverride" signature="(Windows.Foundation.Size)">
              <lines><line number="70" hits="0" branch="False" /><line number="71" hits="0" branch="False" /></lines>
            </method>
          </methods>
          <lines>
            <line number="30" hits="1" branch="False" />
            <line number="53" hits="1" branch="False" />
            <line number="54" hits="1" branch="False" />
            <line number="70" hits="0" branch="False" />
            <line number="71" hits="0" branch="False" />
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>
'@
[xml]$unitLane = @'
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<coverage line-rate="0.375" branch-rate="0.25" complexity="4" version="1.9" timestamp="200" lines-covered="3" lines-valid="8" branches-covered="1" branches-valid="4">
  <packages>
    <package line-rate="0.2857142857142857" branch-rate="0.25" complexity="3" name="Reactor">
      <classes>
        <class line-rate="0.2" branch-rate="0.5" complexity="2" name="Microsoft.UI.Reactor.Layout.FlexPanel" filename="/_/src/Reactor/Yoga/FlexPanel.cs">
          <methods>
            <method line-rate="0" branch-rate="1" complexity="1" name=".ctor" signature="()">
              <lines><line number="30" hits="0" branch="False" /></lines>
            </method>
            <method line-rate="0" branch-rate="1" complexity="1" name=".cctor" signature="()">
              <lines><line number="53" hits="0" branch="False" /><line number="54" hits="0" branch="False" /></lines>
            </method>
            <method line-rate="0.5" branch-rate="0.5" complexity="2" name="MeasureOverride" signature="(Windows.Foundation.Size)">
              <lines><line number="70" hits="1" branch="True" condition-coverage="50% (1/2)" /><line number="71" hits="0" branch="False" /></lines>
            </method>
          </methods>
          <lines>
            <line number="30" hits="0" branch="False" />
            <line number="53" hits="0" branch="False" />
            <line number="54" hits="0" branch="False" />
            <line number="70" hits="1" branch="True" condition-coverage="50% (1/2)" />
            <line number="71" hits="0" branch="False" />
          </lines>
        </class>
        <class line-rate="0.5" branch-rate="0" complexity="2" name="Microsoft.UI.Reactor.UnitOnly" filename="/_/src/Reactor/UnitOnly.cs">
          <methods>
            <method line-rate="0.5" branch-rate="0" complexity="2" name="Run" signature="()">
              <lines><line number="5" hits="1" branch="False" /><line number="6" hits="0" branch="True" condition-coverage="0% (0/2)" /></lines>
            </method>
          </methods>
          <lines>
            <line number="5" hits="1" branch="False" />
            <line number="6" hits="0" branch="True" condition-coverage="0% (0/2)" />
          </lines>
        </class>
      </classes>
    </package>
    <package line-rate="1" branch-rate="1" complexity="1" name="Reactor.Advanced">
      <classes>
        <class line-rate="1" branch-rate="1" complexity="1" name="Microsoft.UI.Reactor.Charting.Axis" filename="/_/src/Reactor.Advanced/Charting/Axis.cs">
          <methods>
            <method line-rate="1" branch-rate="1" complexity="1" name="Tick" signature="()">
              <lines><line number="9" hits="1" branch="False" /></lines>
            </method>
          </methods>
          <lines><line number="9" hits="1" branch="False" /></lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>
'@

# The class-level lines of a report, as "package|class|file|line" -> "hits|branch|condition-coverage".
function Get-ClassLineMap([xml]$Doc) {
    $map = [System.Collections.Generic.SortedDictionary[string, string]]::new([System.StringComparer]::Ordinal)
    foreach ($pkg in $Doc.SelectNodes('/coverage/packages/package')) {
        foreach ($cls in $pkg.SelectNodes('classes/class')) {
            foreach ($l in $cls.SelectNodes('lines/line')) {
                $key = '{0}|{1}|{2}|{3}' -f $pkg.GetAttribute('name'), $cls.GetAttribute('name'), $cls.GetAttribute('filename'), $l.GetAttribute('number')
                $map[$key] = '{0}|{1}|{2}' -f $l.GetAttribute('hits'), $l.GetAttribute('branch'), $l.GetAttribute('condition-coverage')
            }
        }
    }
    return $map
}
function Format-ClassLineMap($Map) { ($Map.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join '; ' }

$selftestBefore = $selftestLane.OuterXml
$unitBefore = $unitLane.OuterXml
$merged = Merge-CoberturaDocuments -Documents $selftestLane, $unitLane -AllowDifferentLines

$flex = 'Reactor|Microsoft.UI.Reactor.Layout.FlexPanel|/_/src/Reactor/Yoga/FlexPanel.cs'
$expectedUnion = [System.Collections.Generic.SortedDictionary[string, string]]::new([System.StringComparer]::Ordinal)
$expectedUnion["$flex|30"] = '1|False|'
$expectedUnion["$flex|53"] = '1|False|'
$expectedUnion["$flex|54"] = '1|False|'
$expectedUnion["$flex|70"] = '1|True|50% (1/2)'
$expectedUnion["$flex|71"] = '0|False|'
$expectedUnion['Reactor|Microsoft.UI.Reactor.UnitOnly|/_/src/Reactor/UnitOnly.cs|5'] = '1|False|'
$expectedUnion['Reactor|Microsoft.UI.Reactor.UnitOnly|/_/src/Reactor/UnitOnly.cs|6'] = '0|True|0% (0/2)'
$expectedUnion['Reactor.Advanced|Microsoft.UI.Reactor.Charting.Axis|/_/src/Reactor.Advanced/Charting/Axis.cs|9'] = '1|False|'
$mergedMap = Get-ClassLineMap $merged
Assert-Equal (Format-ClassLineMap $expectedUnion) (Format-ClassLineMap $mergedMap) 'merge: every class line is the union of the lanes'
Assert-Equal '1|False|' $mergedMap["$flex|53"] 'merge keeps static-constructor line 53, covered only under the second FlexPanel() method'
Assert-Equal '1|False|' $mergedMap["$flex|54"] 'merge keeps static-constructor line 54, covered only under the second FlexPanel() method'
Assert-Equal '1|True|50% (1/2)' $mergedMap["$flex|70"] 'merge carries the unit lane''s branch data onto the class line'

$root = $merged.DocumentElement
Assert-Equal '6'    $root.GetAttribute('lines-covered')    'merge: root lines-covered is the union count'
Assert-Equal '8'    $root.GetAttribute('lines-valid')      'merge: root lines-valid counts every class line once'
Assert-Equal '0.75' $root.GetAttribute('line-rate')        'merge: root line-rate is covered/valid of the union'
Assert-Equal '1'    $root.GetAttribute('branches-covered') 'merge: root branches-covered'
Assert-Equal '4'    $root.GetAttribute('branches-valid')   'merge: root branches-valid'
Assert-Equal '0.25' $root.GetAttribute('branch-rate')      'merge: root branch-rate is recomputed, not the lane''s hard-coded 1'
Assert-Equal '200'  $root.GetAttribute('timestamp')        'merge: root timestamp is the newest input''s'

$mergedFlex = $merged.SelectSingleNode("//class[@name='Microsoft.UI.Reactor.Layout.FlexPanel']")
Assert-Equal '0.8' $mergedFlex.GetAttribute('line-rate') 'merge: class line-rate is recomputed from its merged lines'
$flexMethods = @($mergedFlex.SelectNodes('methods/method'))
Assert-Equal 'FlexPanel,FlexPanel,MeasureOverride' (($flexMethods | ForEach-Object { $_.GetAttribute('name') }) -join ',') 'merge: methods come from the first input that has the class'
Assert-Equal '1,1' (($flexMethods[1].SelectNodes('lines/line') | ForEach-Object { $_.GetAttribute('hits') }) -join ',') 'merge: a method line takes its class''s merged line'
Assert-Equal '50% (1/2)' $flexMethods[2].SelectSingleNode("lines/line[@number='70']").GetAttribute('condition-coverage') 'merge: method lines get the merged branch data too'
Assert-Equal '0.5' $flexMethods[2].GetAttribute('line-rate') 'merge: method line-rate is recomputed'
Assert-True ($null -ne $merged.SelectSingleNode("/coverage/packages/package[@name='Reactor.Advanced']/classes/class[@name='Microsoft.UI.Reactor.Charting.Axis']")) 'merge: a package only a later input has is added'

$mergedRates = Get-CoberturaRatesFromXml -Doc $merged
Assert-Equal 75 $mergedRates.Line            'merge -> rates: line % of the union'
Assert-Equal 25 $mergedRates.Branch          'merge -> rates: branch %'
Assert-Equal 1  $mergedRates.BranchesCovered 'merge -> rates: branches covered, not doubled by the method lines'
Assert-Equal 4  $mergedRates.BranchesTotal   'merge -> rates: branches total, not doubled by the method lines'

Assert-Equal $selftestBefore $selftestLane.OuterXml 'merge leaves the first input untouched'
Assert-Equal $unitBefore $unitLane.OuterXml 'merge leaves the second input untouched'
Assert-Equal $merged.OuterXml (Merge-CoberturaDocuments -Documents $selftestLane, $unitLane -AllowDifferentLines).OuterXml 'merge is deterministic'

$reversed = Merge-CoberturaDocuments -Documents $unitLane, $selftestLane -AllowDifferentLines
Assert-Equal (Format-ClassLineMap $mergedMap) (Format-ClassLineMap (Get-ClassLineMap $reversed)) 'merge: input order does not change any class line'
Assert-Equal '0.75' $reversed.DocumentElement.GetAttribute('line-rate') 'merge: input order does not change the line rate'
Assert-Equal '.ctor' $reversed.SelectSingleNode("//class[@name='Microsoft.UI.Reactor.Layout.FlexPanel']/methods/method").GetAttribute('name') 'merge: reversed, the methods come from the unit lane'

# Branch data in more than one input: the most covered conditions, and the highest hit count.
[xml]$branchA = '<coverage timestamp="1"><packages><package name="P"><classes><class name="C" filename="c.cs"><methods /><lines><line number="1" hits="3" branch="True" condition-coverage="50% (1/2)" /><line number="2" hits="1" branch="True" condition-coverage="50% (1/2)" /></lines></class></classes></package></packages></coverage>'
[xml]$branchB = '<coverage timestamp="2"><packages><package name="P"><classes><class name="C" filename="c.cs"><methods /><lines><line number="1" hits="1" branch="True" condition-coverage="100% (2/2)" /><line number="2" hits="0" branch="True" condition-coverage="0% (0/2)" /></lines></class></classes></package></packages></coverage>'
$branchMap = Get-ClassLineMap (Merge-CoberturaDocuments -Documents $branchA, $branchB)
Assert-Equal '3|True|100% (2/2)' $branchMap['P|C|c.cs|1'] 'merge: highest hit count, most covered conditions'
Assert-Equal '1|True|50% (1/2)'  $branchMap['P|C|c.cs|2'] 'merge: a lane with fewer covered conditions does not lower the line'

# By default the inputs must list the same lines. The unit lane above has a class and a
# package the selftest lane lacks, as when one lane measured fewer assemblies.
Assert-Throws { Merge-CoberturaDocuments -Documents $selftestLane, $unitLane } '*do not list the same lines*3 line(s) are not in every report: input 1 lacks 3.*' 'merge refuses lanes that list different lines by default'
Assert-Throws { Merge-CoberturaDocuments -Documents $selftestLane, $unitLane -Names 'selftest-1.cobertura.xml', 'unit.cobertura.xml' } '*input 1 (selftest-1.cobertura.xml) lacks 3. For example, line 5 of Microsoft.UI.Reactor.UnitOnly (/_/src/Reactor/UnitOnly.cs) is missing from input 1 (selftest-1.cobertura.xml);*' 'merge names the report that lacks lines and the first line it lacks'
Assert-Throws { Merge-CoberturaDocuments -Documents $unitLane, $selftestLane } '*input 2 lacks 3.*' 'merge reports the lacking input whatever its position'
Assert-Throws { Merge-CoberturaDocuments -Documents $selftestLane, $unitLane } '*For example, line 5 of Microsoft.UI.Reactor.UnitOnly (/_/src/Reactor/UnitOnly.cs) is missing from input 1; line 9 of Microsoft.UI.Reactor.Charting.Axis (*' 'merge gives one example per class, not every line of the first class'
# A class one lane names differently would otherwise count its lines twice.
[xml]$namedOneWay = '<coverage><packages><package name="P"><classes><class name="N.Widget" filename="w.cs"><lines><line number="1" hits="1" /><line number="2" hits="0" /></lines></class></classes></package></packages></coverage>'
[xml]$namedOtherWay = '<coverage><packages><package name="P"><classes><class name="N.Widget`1" filename="w.cs"><lines><line number="1" hits="0" /><line number="2" hits="1" /></lines></class></classes></package></packages></coverage>'
Assert-Throws { Merge-CoberturaDocuments -Documents $namedOneWay, $namedOtherWay } '*4 line(s) are not in every report: input 1 lacks 2; input 2 lacks 2.*' 'merge refuses a class that one lane names differently'
Assert-Equal '4' (Merge-CoberturaDocuments -Documents $namedOneWay, $namedOtherWay -AllowDifferentLines).DocumentElement.GetAttribute('lines-valid') '-AllowDifferentLines merges them anyway, counting each name''s lines'
$manyReports = @(1..63 | ForEach-Object { [xml]'<coverage><packages /></coverage>' })
Assert-Throws { Merge-CoberturaDocuments -Documents $manyReports } '*more than 62 reports*' 'merge says when it cannot compare that many reports'
Assert-Equal '0' (Merge-CoberturaDocuments -Documents $manyReports -AllowDifferentLines).DocumentElement.GetAttribute('lines-valid') '-AllowDifferentLines merges more than 62 reports'

[xml]$dupClass = '<coverage><packages><package name="P"><classes><class name="C" filename="c.cs"><lines /></class><class name="C" filename="c.cs"><lines /></class></classes></package></packages></coverage>'
Assert-Throws { Merge-CoberturaDocuments -Documents $dupClass } "*lists class 'C' (c.cs) more than once*" 'merge rejects an input that lists a class twice'
[xml]$badCondition = '<coverage><packages><package name="P"><classes><class name="C" filename="c.cs"><lines><line number="1" hits="1" branch="True" condition-coverage="half" /></lines></class></classes></package></packages></coverage>'
Assert-Throws { Merge-CoberturaDocuments -Documents $badCondition } "*unreadable condition-coverage 'half'*" 'merge rejects an unreadable condition-coverage'
[xml]$notCobertura = '<report><packages /></report>'
Assert-Throws { Merge-CoberturaDocuments -Documents $selftestLane, $notCobertura } '*Input 2 is not a cobertura report*' 'merge rejects a document that is not cobertura'
[xml]$strayMethodLine = '<coverage><packages><package name="P"><classes><class name="C" filename="c.cs"><methods><method name="M" signature="()"><lines><line number="2" hits="1" /></lines></method></methods><lines><line number="1" hits="1" /></lines></class></classes></package></packages></coverage>'
Assert-Throws { Merge-CoberturaDocuments -Documents $strayMethodLine } "*lists line 2, which its class's lines do not*" 'merge rejects a method line its class does not list'

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

# The scoring version: optional, validated like a count, absent meaning version 1.
Assert-Null $safe.ScoringVersion 'a record without scoringVersion carries none'
$versioned = ConvertTo-SafeCoverageMetrics ([pscustomobject]@{ line = '87.3'; scoringVersion = 2 })
Assert-Equal 2 $versioned.ScoringVersion 'safe metrics keep a valid scoring version'
Assert-Null (ConvertTo-SafeCoverageMetrics ([pscustomobject]@{ line = '87.3'; scoringVersion = '2|<b>' })).ScoringVersion 'an injected scoring version is dropped'
Assert-Equal 1 (Get-CoverageMetricsScoringVersion $safe) 'metrics without a scoring version count as version 1'
Assert-Equal 1 (Get-CoverageMetricsScoringVersion ([pscustomobject]@{ Line = 1 })) 'metrics without the property at all count as version 1'
Assert-Equal 1 (Get-CoverageMetricsScoringVersion $null) 'absent metrics count as version 1'
Assert-Equal 2 (Get-CoverageMetricsScoringVersion $versioned) 'metrics report the scoring version they carry'
Assert-True ((Get-CoverageScoringVersion) -gt 1) 'the current scoring version differs from the unversioned numbers of the dotnet-coverage merge'

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

# Scoring versions: numbers the scripts scored differently are not compared.
$headV2 = [pscustomobject]@{ Line = 88.29; Branch = 46.55; BranchesCovered = 17242; BranchesTotal = 37038; ScoringVersion = 2 }
$baseV1 = [pscustomobject]@{ Line = 88.29; Branch = 66.33; BranchesCovered = 1044; BranchesTotal = 1574 }
$baseV2 = [pscustomobject]@{ Line = 88.20; Branch = 46.50; BranchesCovered = 17220; BranchesTotal = 37038; ScoringVersion = 2 }
$newHeadOldBase = Format-CoverageComment -BaseMetrics $baseV1 -HeadMetrics $headV2 -HeadSha 'aaa' -BaseSha 'bbbbbbbbb'
Assert-Match    $newHeadOldBase '[!WARNING]'                              'a base scored by older scripts emits a warning'
Assert-Match    $newHeadOldBase 'until the base branch is measured again' 'the warning says when the comparison returns'
Assert-Match    $newHeadOldBase "| Line   | n/a | 88.29% | $([char]0x2014) |" 'no line delta across scoring versions'
Assert-Match    $newHeadOldBase "| Branch | n/a | 46.55% (17242/37038) | $([char]0x2014) |" 'no branch delta across scoring versions'
Assert-NotMatch $newHeadOldBase '66.33%'                                  'the differently scored base number is not shown'
Assert-NotMatch $newHeadOldBase '(`bbbbbbb`)'                             'the header cites no base it does not compare against'
Assert-NotMatch $newHeadOldBase 'No coverage change beyond'               'no no-change note without a comparison'
$oldHeadNewBase = Format-CoverageComment -BaseMetrics $baseV2 -HeadMetrics ([pscustomobject]@{ Line = 88.19; Branch = 66.39; BranchesCovered = 1045; BranchesTotal = 1574 }) -HeadSha 'aaa' -BaseSha 'bbb'
Assert-Match    $oldHeadNewBase 'Update this PR from the base branch'     'a PR scored by older scripts is told to update from the base branch'
Assert-Match    $oldHeadNewBase "| Line   | n/a | 88.19% | $([char]0x2014) |" 'no delta for a PR scored by older scripts'
$sameScoring = Format-CoverageComment -BaseMetrics $baseV2 -HeadMetrics $headV2 -HeadSha 'aaa' -BaseSha 'bbb'
Assert-Match    $sameScoring '| Line   | 88.20% | 88.29% | +0.09 pp |'  'numbers of one scoring version are compared'
Assert-NotMatch $sameScoring '[!WARNING]'                                 'one scoring version: no warning'

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

# head + base present but scored differently -> the PR's absolute numbers and a warning.
$presentV2 = ConvertTo-SafeCoverageMetrics ([pscustomobject]@{ line = '87.3'; branch = '75.2'; branchesCovered = '510'; branchesTotal = '678'; scoringVersion = '2' })
$selScoring = Format-CoverageCommentFromMetrics -HeadMetrics $presentV2 -BaseMetrics $basePresent -HasBase $true -HeadSha 'aaa' -BaseSha 'bbb'
Assert-Match    $selScoring 'scored by an older version' 'selector: head and base scored differently -> warning'
Assert-NotMatch $selScoring '+1.20 pp'                   'selector: head and base scored differently -> no delta'

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
