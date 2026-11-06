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
      Merge-CoberturaDocuments    union cobertura documents line by line (the lane
                                  merge; Merge-Cobertura.ps1 is its file wrapper).
      ConvertTo-SafePercent       gate an artifact-supplied percentage (0..100).
      ConvertTo-SafeCount         gate an artifact-supplied non-negative integer.
      ConvertTo-SafeCoverageMetrics  project raw (untrusted) numbers onto a
                                  validated metrics object — the render-time
                                  security boundary for the privileged poster.
      Test-CoverageMetricsPresent whether a metrics object carries a real line %.
      Get-CoverageScoringVersion  the scoring version these scripts write.
      Get-CoverageMetricsScoringVersion  the scoring version a metrics object
                                  carries (1 when it predates the field).
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
      ScoringVersion   (optional) the scoring version that produced the numbers, or
                       $null for numbers recorded before the field existed.

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

# Which way the numbers in coverage.json were produced. The poster shows a delta only
# between numbers of the same version, so bump this whenever a change to the merge or to
# Get-CoberturaRates changes what the numbers mean. Numbers that carry no version count
# as version 1, the dotnet-coverage merge.
#   2  Merge-CoberturaDocuments, with branches counted on class-level lines.
$script:CoverageScoringVersion = 2

function Get-CoverageCommentMarker {
    <#
    .SYNOPSIS
        The hidden HTML marker used to find + update the sticky coverage comment.
    #>
    return $script:CoverageCommentMarker
}

function Get-CoverageScoringVersion {
    <#
    .SYNOPSIS
        The scoring version Measure-Coverage.ps1 records in coverage.json.
    #>
    return $script:CoverageScoringVersion
}

function Get-CoberturaRatesFromXml {
    <#
    .SYNOPSIS
        Aggregate a merged cobertura document into line % + branch % (+ the branch
        covered/total counts).
    .DESCRIPTION
        The line rate is read from the root `line-rate` (0..1, scaled to a percent).
        The branch rate is summed here from the class-level lines'
        `condition-coverage="P% (covered/total)"` attributes. It is not read from the
        root because not every report fills it in: the statically instrumented
        selftest reports carry no branch data yet write `branch-rate="1"`. Method lines
        are skipped because they repeat their class's lines, so counting both would
        count every branch twice.
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
    foreach ($l in $Doc.SelectNodes('/coverage/packages/package/classes/class/lines/line[@condition-coverage]')) {
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

function Merge-CoberturaDocuments {
    <#
    .SYNOPSIS
        Merge cobertura documents so that a line is covered when any input covers it.
        Returns a new document; the inputs are not modified.
    .DESCRIPTION
        Lines are matched on package, class, file name and line number, which every lane
        report of one build lists identically. Methods are not a reliable identity, and
        `dotnet-coverage merge` matches lines through their method, so it does not union
        these reports. The unit lane is instrumented dynamically and names constructors
        `.ctor` / `.cctor`. The selftest lanes are instrumented statically and name both
        constructors after the type, so one of their reports can hold two methods with
        the same name and signature. The tool keeps or drops lines like these
        differently from one run to the next. On PR #1464 it dropped 62 FlexPanel.cs
        lines that both selftest lanes covered. It also discards the unit lane's
        class-level branch data.

        By default the inputs must list exactly the same class lines, and the merge
        throws, naming the reports and the first lines that differ, when they do not. A
        report that measured fewer assemblies or named a class differently would
        otherwise skew the union without any error: its coverage of a missing class
        would vanish, and a renamed class would count its lines twice.

        Each merged line records:
          hits      the largest count any input recorded, so a line is covered exactly
                    when some input covered it. (dotnet-coverage records 0 or 1, so a
                    sum would add nothing.)
          branches  the most covered conditions any input recorded, out of the most
                    conditions any input counted. Cobertura counts a line's conditions
                    but does not say which ones ran. So this is exact when one input
                    carries branch data for the line (today only the unit lane records
                    any), and a lower bound when several do.

        Each class keeps its element from the first input that contains it: its methods,
        their names and its complexity. Each method line takes its class's merged line.
        Every line-rate and branch-rate is recomputed from the merged lines, and the root
        gets lines-covered, lines-valid, branches-covered and branches-valid. Its timestamp
        is the newest input's, so the same inputs always give the same document.
    .PARAMETER Names
        Optional names for the inputs (for example their file names), used in errors.
    .PARAMETER AllowDifferentLines
        Merge inputs that list different lines, such as reports of different builds. Each
        line then counts once however many inputs list it.
    #>
    param(
        [Parameter(Mandatory)][xml[]]$Documents,
        [string[]]$Names,
        [switch]$AllowDifferentLines
    )

    $inv = [System.Globalization.CultureInfo]::InvariantCulture
    $ordinal = [System.StringComparer]::Ordinal

    function Get-ClassKey([string]$Package, [System.Xml.XmlElement]$Class) {
        return "$Package`0$($Class.GetAttribute('name'))`0$($Class.GetAttribute('filename'))"
    }
    function Format-InputName([int]$Index) {
        if ($Names -and $Index -lt $Names.Count -and $Names[$Index]) { return " ($($Names[$Index]))" }
        return ''
    }
    function Format-Rate([long]$Covered, [long]$Valid) {
        # Nothing to cover counts as fully covered, as dotnet-coverage writes it.
        if ($Valid -le 0) { return '1' }
        return ([double]$Covered / $Valid).ToString('R', $inv)
    }

    # Pass 1: the union. Key -> line number -> [hits, covered conditions, conditions, branch
    # flag, bitmask of the inputs that list the line].
    # Hashtables, not generic dictionaries: PowerShell indexes them natively and a miss is
    # $null, which keeps the per-line cost of these ~200k lines low.
    $compareLines = -not $AllowDifferentLines -and $Documents.Count -gt 1
    if ($compareLines -and $Documents.Count -gt 62) {
        throw "Comparing the lines of more than 62 reports is not supported. Pass -AllowDifferentLines to merge $($Documents.Count) reports without comparing them."
    }
    $union = [hashtable]::new($ordinal)
    $distinctLines = [long]0
    $listings = [long]0
    $timestamp = $null
    for ($i = 0; $i -lt $Documents.Count; $i++) {
        $root = $Documents[$i].DocumentElement
        if ($null -eq $root -or $root.LocalName -ne 'coverage') {
            throw "Input $($i + 1)$(Format-InputName $i) is not a cobertura report: it has no <coverage> root."
        }
        $ts = [long]0
        if ([long]::TryParse($root.GetAttribute('timestamp'), [ref]$ts) -and ($null -eq $timestamp -or $ts -gt $timestamp)) {
            $timestamp = $ts
        }
        $bit = [long]1 -shl $i
        $seen = [System.Collections.Generic.HashSet[string]]::new($ordinal)
        foreach ($pkg in $root.SelectNodes('packages/package')) {
            $pkgName = $pkg.GetAttribute('name')
            foreach ($cls in $pkg.SelectNodes('classes/class')) {
                $key = Get-ClassKey $pkgName $cls
                # Two elements for one class would each be scored with the class's merged lines.
                if (-not $seen.Add($key)) {
                    throw "Input $($i + 1)$(Format-InputName $i) lists class '$($cls.GetAttribute('name'))' ($($cls.GetAttribute('filename'))) more than once."
                }
                $lines = $union[$key]
                if ($null -eq $lines) {
                    $lines = @{}
                    $union[$key] = $lines
                }
                foreach ($line in $cls.SelectNodes('lines/line')) {
                    $number = [int]::Parse($line.GetAttribute('number'), $inv)
                    $hits = [long]::Parse($line.GetAttribute('hits'), $inv)
                    $entry = $lines[$number]
                    if ($null -eq $entry) {
                        $entry = [long[]]::new(5)
                        $lines[$number] = $entry
                        $distinctLines++
                    }
                    if ($compareLines -and ($entry[4] -band $bit) -eq 0) {
                        $entry[4] = $entry[4] -bor $bit
                        $listings++
                    }
                    if ($hits -gt $entry[0]) { $entry[0] = $hits }
                    if ($line.GetAttribute('branch') -eq 'True') { $entry[3] = 1 }
                    $cc = $line.GetAttribute('condition-coverage')
                    if ($cc) {
                        if ($cc -notmatch '\((\d+)/(\d+)\)') {
                            throw "Input $($i + 1)$(Format-InputName $i) has an unreadable condition-coverage '$cc' on line $number of '$($cls.GetAttribute('name'))'."
                        }
                        $c = [long]$Matches[1]
                        $t = [long]$Matches[2]
                        if ($c -gt $entry[1]) { $entry[1] = $c }
                        if ($t -gt $entry[2]) { $entry[2] = $t }
                    }
                }
            }
        }
    }

    # Every input lists every line exactly when the listings add up; only then is the
    # union walked again, to say which lines differ.
    if ($compareLines -and $listings -ne $Documents.Count * $distinctLines) {
        $everyInput = ([long]1 -shl $Documents.Count) - 1
        $lacking = [long[]]::new($Documents.Count)
        # Sort key (package, class, file, zero-padded line) -> the inputs that list the line.
        $differing = [System.Collections.Generic.Dictionary[string, long]]::new($ordinal)
        foreach ($classKey in $union.Keys) {
            $classLines = $union[$classKey]
            foreach ($number in $classLines.Keys) {
                $mask = $classLines[$number][4]
                if ($mask -eq $everyInput) { continue }
                for ($j = 0; $j -lt $Documents.Count; $j++) {
                    if (($mask -band ([long]1 -shl $j)) -eq 0) { $lacking[$j]++ }
                }
                $differing["$classKey`0$($number.ToString('D10', $inv))"] = $mask
            }
        }
        $byInput = for ($j = 0; $j -lt $Documents.Count; $j++) {
            if ($lacking[$j] -gt 0) { "input $($j + 1)$(Format-InputName $j) lacks $($lacking[$j])" }
        }
        [string[]]$sortKeys = @($differing.Keys)
        [System.Array]::Sort($sortKeys, $ordinal)
        # The first differing line of each of the first three classes: a whole missing
        # assembly then reads as several classes, not three lines of one.
        $examples = [System.Collections.Generic.List[string]]::new()
        $previousClass = $null
        foreach ($sortKey in $sortKeys) {
            $parts = $sortKey -split "`0"
            $thisClass = "$($parts[0])`0$($parts[1])`0$($parts[2])"
            if ($thisClass -ceq $previousClass) { continue }
            $previousClass = $thisClass
            $absent = for ($j = 0; $j -lt $Documents.Count; $j++) {
                if (($differing[$sortKey] -band ([long]1 -shl $j)) -eq 0) { "input $($j + 1)$(Format-InputName $j)" }
            }
            $examples.Add("line $([int]$parts[3]) of $($parts[1]) ($($parts[2])) is missing from $($absent -join ', ')")
            if ($examples.Count -eq 3) { break }
        }
        throw ("The reports do not list the same lines, so their union would mis-score coverage. " +
            "$($differing.Count) line(s) are not in every report: $($byInput -join '; '). " +
            "For example, $($examples -join '; '). " +
            'Merge reports of one build that measured the same assemblies, or pass -AllowDifferentLines to merge them anyway.')
    }

    # Pass 2: the structure. A copy of the first input, plus every package and class that
    # only a later input has.
    $out = [xml]$Documents[0].CloneNode($true)
    $outRoot = $out.DocumentElement
    $packagesEl = $outRoot.SelectSingleNode('packages')
    if ($null -eq $packagesEl) { $packagesEl = $outRoot.AppendChild($out.CreateElement('packages')) }
    $pkgEls = [System.Collections.Generic.Dictionary[string, System.Xml.XmlElement]]::new($ordinal)
    $classKeys = [System.Collections.Generic.HashSet[string]]::new($ordinal)
    foreach ($pkg in $packagesEl.SelectNodes('package')) {
        $pkgName = $pkg.GetAttribute('name')
        if (-not $pkgEls.ContainsKey($pkgName)) { $pkgEls[$pkgName] = $pkg }
        foreach ($cls in $pkg.SelectNodes('classes/class')) { $null = $classKeys.Add((Get-ClassKey $pkgName $cls)) }
    }
    for ($i = 1; $i -lt $Documents.Count; $i++) {
        foreach ($pkg in $Documents[$i].DocumentElement.SelectNodes('packages/package')) {
            $pkgName = $pkg.GetAttribute('name')
            $target = $null
            if (-not $pkgEls.TryGetValue($pkgName, [ref]$target)) {
                $target = $packagesEl.AppendChild($out.ImportNode($pkg, $true))
                $pkgEls[$pkgName] = $target
                foreach ($cls in $target.SelectNodes('classes/class')) { $null = $classKeys.Add((Get-ClassKey $pkgName $cls)) }
                continue
            }
            foreach ($cls in $pkg.SelectNodes('classes/class')) {
                if ($classKeys.Add((Get-ClassKey $pkgName $cls))) {
                    $classesEl = $target.SelectSingleNode('classes')
                    if ($null -eq $classesEl) { $classesEl = $target.AppendChild($out.CreateElement('classes')) }
                    $null = $classesEl.AppendChild($out.ImportNode($cls, $true))
                }
            }
        }
    }

    # Pass 3: write the union into the structure and recompute every rate from it.
    $rootCov = [long]0; $rootVal = [long]0; $rootBc = [long]0; $rootBt = [long]0
    foreach ($pkg in $packagesEl.SelectNodes('package')) {
        $pkgName = $pkg.GetAttribute('name')
        $pkgCov = [long]0; $pkgVal = [long]0; $pkgBc = [long]0; $pkgBt = [long]0
        foreach ($cls in $pkg.SelectNodes('classes/class')) {
            $lines = $union[(Get-ClassKey $pkgName $cls)]
            $linesEl = $cls.SelectSingleNode('lines')
            if ($null -eq $linesEl) { $linesEl = $cls.AppendChild($out.CreateElement('lines')) }
            while ($linesEl.HasChildNodes) { $null = $linesEl.RemoveChild($linesEl.FirstChild) }
            $clsCov = [long]0; $clsBc = [long]0; $clsBt = [long]0
            [int[]]$numbers = @($lines.Keys)
            [System.Array]::Sort($numbers)
            foreach ($number in $numbers) {
                $entry = $lines[$number]
                $el = $out.CreateElement('line')
                $el.SetAttribute('number', $number.ToString($inv))
                $el.SetAttribute('hits', $entry[0].ToString($inv))
                $el.SetAttribute('branch', $(if ($entry[3] -eq 1 -or $entry[2] -gt 0) { 'True' } else { 'False' }))
                if ($entry[2] -gt 0) {
                    $c = [math]::Min($entry[1], $entry[2])
                    $pct = [math]::Round(100.0 * $c / $entry[2], 2).ToString($inv)
                    $el.SetAttribute('condition-coverage', "$pct% ($c/$($entry[2]))")
                    $clsBc += $c
                    $clsBt += $entry[2]
                }
                if ($entry[0] -gt 0) { $clsCov++ }
                $null = $linesEl.AppendChild($el)
            }
            foreach ($method in $cls.SelectNodes('methods/method')) {
                $mCov = [long]0; $mVal = [long]0; $mBc = [long]0; $mBt = [long]0
                foreach ($ml in $method.SelectNodes('lines/line')) {
                    $number = [int]::Parse($ml.GetAttribute('number'), $inv)
                    $entry = $lines[$number]
                    if ($null -eq $entry) {
                        throw "Method '$($method.GetAttribute('name'))' of '$($cls.GetAttribute('name'))' lists line $number, which its class's lines do not."
                    }
                    $ml.SetAttribute('hits', $entry[0].ToString($inv))
                    $ml.SetAttribute('branch', $(if ($entry[3] -eq 1 -or $entry[2] -gt 0) { 'True' } else { 'False' }))
                    if ($entry[2] -gt 0) {
                        $c = [math]::Min($entry[1], $entry[2])
                        $pct = [math]::Round(100.0 * $c / $entry[2], 2).ToString($inv)
                        $ml.SetAttribute('condition-coverage', "$pct% ($c/$($entry[2]))")
                        $mBc += $c
                        $mBt += $entry[2]
                    } else {
                        $ml.RemoveAttribute('condition-coverage')
                    }
                    $mVal++
                    if ($entry[0] -gt 0) { $mCov++ }
                }
                $method.SetAttribute('line-rate', (Format-Rate $mCov $mVal))
                $method.SetAttribute('branch-rate', (Format-Rate $mBc $mBt))
            }
            $cls.SetAttribute('line-rate', (Format-Rate $clsCov $lines.Count))
            $cls.SetAttribute('branch-rate', (Format-Rate $clsBc $clsBt))
            $pkgCov += $clsCov; $pkgVal += $lines.Count; $pkgBc += $clsBc; $pkgBt += $clsBt
        }
        $pkg.SetAttribute('line-rate', (Format-Rate $pkgCov $pkgVal))
        $pkg.SetAttribute('branch-rate', (Format-Rate $pkgBc $pkgBt))
        $rootCov += $pkgCov; $rootVal += $pkgVal; $rootBc += $pkgBc; $rootBt += $pkgBt
    }
    $outRoot.SetAttribute('line-rate', (Format-Rate $rootCov $rootVal))
    $outRoot.SetAttribute('branch-rate', (Format-Rate $rootBc $rootBt))
    if ($null -ne $timestamp) { $outRoot.SetAttribute('timestamp', $timestamp.ToString($inv)) }
    $outRoot.SetAttribute('lines-covered', $rootCov.ToString($inv))
    $outRoot.SetAttribute('lines-valid', $rootVal.ToString($inv))
    $outRoot.SetAttribute('branches-covered', $rootBc.ToString($inv))
    $outRoot.SetAttribute('branches-valid', $rootBt.ToString($inv))
    return $out
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
        ScoringVersion  = ConvertTo-SafeCount   (Get-RawProp $Raw 'scoringVersion')
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

function Get-CoverageMetricsScoringVersion {
    <#
    .SYNOPSIS
        The scoring version a metrics object carries, or 1 when it carries none: every
        number recorded before the field existed came from the dotnet-coverage merge.
    #>
    param([AllowNull()]$Metrics)
    if ($null -ne $Metrics -and $Metrics.PSObject.Properties['ScoringVersion'] -and $null -ne $Metrics.ScoringVersion) {
        return [long]$Metrics.ScoringVersion
    }
    return [long]1
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
        Coverage metrics objects (Line/Branch/BranchesCovered/BranchesTotal, and an
        optional ScoringVersion). Head drives the PR column; base supplies the
        comparison column. When the two carry different scoring versions, their numbers
        mean different things, so the PR's absolute coverage is shown with a note
        instead of a delta.
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

    $basePresent = (Test-CoverageMetricsPresent $BaseMetrics) -and -not $BaselineUnavailable
    $headScoring = Get-CoverageMetricsScoringVersion $HeadMetrics
    $baseScoring = Get-CoverageMetricsScoringVersion $BaseMetrics
    $scoringDiffers = $basePresent -and $headScoring -ne $baseScoring
    $hasBase = $basePresent -and -not $scoringDiffers
    $baseSuffix = if ($hasBase -and $baseShort) { " (``$baseShort``)" } else { '' }
    $lines.Add("Coverage for ``$headShort`` vs the base branch$baseSuffix — unit + selftest merged.")
    $lines.Add('')

    if ($BaselineUnavailable) {
        $lines.Add('> [!WARNING]')
        $lines.Add('> The base branch measurement is unavailable, so no delta is shown — reporting the PR''s absolute coverage only.')
        $lines.Add('')
    } elseif ($scoringDiffers) {
        $lines.Add('> [!WARNING]')
        if ($headScoring -lt $baseScoring) {
            $lines.Add('> This PR''s coverage was scored by an older version of the coverage scripts than the base branch''s, so no delta is shown. Update this PR from the base branch to compare them.')
        } else {
            $lines.Add('> The base branch''s coverage was scored by an older version of the coverage scripts, so no delta is shown until the base branch is measured again.')
        }
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
          - head + base present             -> full base | PR | Δ comparison, or the
                                               PR's absolute coverage and a note when
                                               the two were scored differently.
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
