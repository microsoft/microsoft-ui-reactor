<#
.SYNOPSIS
    Pure, dot-source-able helpers for the automatic build-metrics (artifact
    size-diff) PR comment (.github/workflows/build-metrics.yml).

.DESCRIPTION
    These functions have no side effects beyond Write-Warning, so they can be
    unit-tested locally (BuildMetricsLib.Tests.ps1) without building anything:

      Format-ByteSize             humanize a byte count (1024-based: B/KB/MB/GB).
      Format-SignedByteSize       same, with an explicit +/- sign.
      Get-SizeDelta               base-vs-head delta, direction-aware, with a
                                  small noise band (grew / shrank / unchanged /
                                  added / removed / na).
      Get-SizeStatusGlyph         emoji/glyph for a delta status.
      Format-SizeDeltaCell        the "Δ" table cell (signed size + percent).
      ConvertTo-MeasurementMap    index an array of measurement objects by Key.
      Format-BuildMetricsComment  the sticky size-diff markdown comment.

    A "measurement" is a [pscustomobject] with:
      Key    stable identifier ('nupkg.<PkgKey>' for a package, or
             'asm|<PkgKey>|<Dll>' for one shipped DLL inside it)
      Label  display name (e.g. 'Microsoft.UI.Reactor.nupkg' or 'Reactor.dll')
      Group  section header ('Packages (compressed .nupkg)' | 'Assemblies in <package>')
      Bytes  size in bytes, or $null when the artifact was not produced.

    Growth is the "bad" direction for every tracked artifact (smaller ships
    faster / trims better), so `shrank` is flagged as the improvement and `grew`
    as the regression. The comment is informational only — it never fails a PR.
#>

Set-StrictMode -Version Latest

# Hidden marker used to find + update-in-place the sticky PR comment. Mirrors the
# convention in tests/stress_perf/ci/PerfLib.ps1 ('<!-- reactor-perf-compare -->').
$script:BuildMetricsCommentMarker = '<!-- reactor-build-metrics -->'

# Default significance band. A delta counts as a real change only when it clears
# BOTH floors: absolute bytes AND percent. .nupkg files are ZIP archives whose
# compressed size can jitter by a few bytes across otherwise-identical builds, so
# a tiny band keeps that noise from rendering as a spurious ⚠️ regression.
$script:BuildMetricsNoiseFloorBytes = 64
$script:BuildMetricsNoiseFloorPct   = 0.05

# ── Trusted package spec ─────────────────────────────────────────────────────
# The canonical, ordered list of shipped packages. This is the SINGLE SOURCE OF
# TRUTH for what the comment can display. The measure job runs untrusted PR code,
# so the privileged poster must NOT trust label/group strings that come back in
# the uploaded sizes.json — it re-maps every row through this spec via
# ConvertTo-SafeMeasurements:
#   * each package's compressed .nupkg row gets a TRUSTED PkgLabel + PackageGroup,
#     keyed 'nupkg.<PkgKey>';
#   * each per-DLL row (keyed 'asm|<PkgKey>|<Dll>') is accepted only when <PkgKey>
#     is one of these known keys AND <Dll> passes a strict .dll-filename allowlist.
# The validated DLL filename is then the ONLY artifact-derived string that ever
# reaches the rendered markdown, and the allowlist forbids every markdown/HTML
# meta-character, so it is always safe to emit verbatim. Byte counts are validated
# as non-negative integers. PkgKey is alphanumeric, so it can never contain the
# '|' delimiter used in the asm row keys. New DLLs shipped by a package appear on
# their own — no per-DLL list is hard-coded here.
$script:BuildMetricsPackageSpec = @(
    [pscustomobject]@{ PkgKey = 'Reactor';  PkgLabel = 'Microsoft.UI.Reactor.nupkg';          PackageGroup = 'Packages (compressed .nupkg)'; AssemblyGroup = 'Assemblies in Microsoft.UI.Reactor' }
    [pscustomobject]@{ PkgKey = 'Advanced'; PkgLabel = 'Microsoft.UI.Reactor.Advanced.nupkg'; PackageGroup = 'Packages (compressed .nupkg)'; AssemblyGroup = 'Assemblies in Microsoft.UI.Reactor.Advanced' }
    [pscustomobject]@{ PkgKey = 'Devtools'; PkgLabel = 'Microsoft.UI.Reactor.Devtools.nupkg'; PackageGroup = 'Packages (compressed .nupkg)'; AssemblyGroup = 'Assemblies in Microsoft.UI.Reactor.Devtools' }
)

# Strict allowlist for the ONLY artifact-derived string allowed into the rendered
# comment: a DLL filename. Absolutely anchored (\A ... \z, so a trailing newline
# cannot slip past a '$' end-of-line match) and CASE-SENSITIVELY matched below via
# -cnotmatch (so Unicode case-folding — e.g. the Kelvin sign U+212A folding to 'k'
# — cannot smuggle a non-ASCII glyph through [A-Za-z]). Limited to characters that
# cannot carry markdown/HTML meaning (no backtick, pipe, angle bracket, bracket,
# space, or newline), so a validated filename is always safe to drop verbatim into
# a table cell. A name that fails this is dropped (never rendered).
$script:BuildMetricsDllNameRegex = '\A[A-Za-z0-9._+-]+\.dll\z'

# Defense-in-depth cap: the per-DLL rows come from the untrusted sizes.json a PR
# build produced, and a malicious PR could edit the measure script to emit
# thousands of validly-shaped 'asm|<PkgKey>|<name>.dll' keys, bloating the
# rendered comment (or blowing past GitHub's comment-size limit and failing the
# privileged poster). Real packages ship a handful of DLLs, so we hard-cap how
# many per-DLL rows any single package may contribute. Rows are sorted by
# filename BEFORE the cap is applied, so the selection is deterministic (the
# lexicographically smallest names win) and can't be steered by row ordering.
$script:BuildMetricsMaxDllRowsPerPackage = 32

function Get-BuildMetricsPackageSpec {
    <#
    .SYNOPSIS
        The trusted, ordered package spec (PkgKey → PkgLabel / PackageGroup /
        AssemblyGroup) for the tracked NuGet packages.
    #>
    return $script:BuildMetricsPackageSpec
}

function ConvertTo-SafeBytes {
    <#
    .SYNOPSIS
        Return $Value as a non-negative [long], or $null when it is missing,
        non-integer, negative, or otherwise not a plain integer (e.g. '1.5',
        '1e9', 'not-a-number'). The sole gate for artifact-supplied byte counts.
    #>
    param([AllowNull()]$Value)
    if ($null -eq $Value) { return $null }
    $parsed = [long]0
    if ([long]::TryParse([string]$Value, [ref]$parsed) -and $parsed -ge 0) {
        return $parsed
    }
    return $null
}

function ConvertTo-SafeMeasurements {
    <#
    .SYNOPSIS
        Project raw (untrusted) measurement objects — e.g. parsed from a sizes.json
        an unprivileged PR job produced — onto the trusted package spec.

    .DESCRIPTION
        Security boundary for the privileged poster: never render label/group text
        that came from the artifact.

        (a) For each trusted package, emit the compressed-.nupkg row (key
            'nupkg.<PkgKey>') carrying the TRUSTED PkgLabel + PackageGroup and a
            byte count accepted ONLY when it is a non-negative integer.

        (b) For each raw row keyed 'asm|<PkgKey>|<Dll>' where <PkgKey> is a KNOWN
            package and <Dll> passes the strict .dll allowlist, emit a per-DLL row
            using the validated <Dll> filename as the display Label and the
            package's TRUSTED AssemblyGroup as the Group. Rows are sorted by Label
            for deterministic output and then hard-capped per package
            ($BuildMetricsMaxDllRowsPerPackage) so a malicious PR cannot flood the
            comment with unboundedly many rows. Unknown keys, malformed keys, and
            filenames that fail the allowlist are dropped.

        The output is therefore fully determined by trusted code plus a set of
        validated integers and allowlisted DLL filenames.
    #>
    param([AllowNull()]$RawMeasurements)

    $rawByKey = @{}
    if ($null -ne $RawMeasurements) {
        foreach ($m in @($RawMeasurements)) {
            if ($null -eq $m) { continue }
            if (-not $m.PSObject.Properties['Key']) { continue }
            $rawByKey[[string]$m.Key] = $m
        }
    }

    $out = [System.Collections.Generic.List[object]]::new()

    # (a) One compressed-.nupkg row per trusted package, in spec order — so the
    # single "Packages (compressed .nupkg)" section renders first.
    foreach ($p in $script:BuildMetricsPackageSpec) {
        $key = 'nupkg.' + $p.PkgKey
        $bytes = $null
        if ($rawByKey.ContainsKey($key)) {
            $raw = $rawByKey[$key]
            if ($raw.PSObject.Properties['Bytes']) { $bytes = ConvertTo-SafeBytes $raw.Bytes }
        }
        $out.Add([pscustomobject]@{ Key = $key; Label = $p.PkgLabel; Group = $p.PackageGroup; Bytes = $bytes })
    }

    # (b) Per-DLL rows, grouped by package (spec order), sorted by validated
    # filename within each package.
    foreach ($p in $script:BuildMetricsPackageSpec) {
        $rows = [System.Collections.Generic.List[object]]::new()
        foreach ($rawKey in $rawByKey.Keys) {
            $parts = ([string]$rawKey).Split('|')
            if ($parts.Count -ne 3) { continue }
            if ($parts[0] -ne 'asm') { continue }
            if ($parts[1] -ne $p.PkgKey) { continue }
            $name = $parts[2]
            if ($name -cnotmatch $script:BuildMetricsDllNameRegex) { continue }
            $raw = $rawByKey[$rawKey]
            $bytes = $null
            if ($raw.PSObject.Properties['Bytes']) { $bytes = ConvertTo-SafeBytes $raw.Bytes }
            $rows.Add([pscustomobject]@{ Key = $rawKey; Label = $name; Group = $p.AssemblyGroup; Bytes = $bytes })
        }
        # Sort by validated filename first, THEN cap, so the kept subset is
        # deterministic and independent of artifact row ordering.
        $sorted = @($rows | Sort-Object Label)
        $limit = [Math]::Min($sorted.Count, $script:BuildMetricsMaxDllRowsPerPackage)
        for ($i = 0; $i -lt $limit; $i++) { $out.Add($sorted[$i]) }
    }

    return $out
}

function Format-ByteSize {
    <#
    .SYNOPSIS
        Humanize a byte count using 1024-based units (B/KB/MB/GB). Negative
        values keep a leading '-'.
    #>
    param([AllowNull()]$Bytes)
    if ($null -eq $Bytes) { return 'n/a' }
    $b = [double]$Bytes
    $sign = ''
    if ($b -lt 0) { $sign = '-'; $b = -$b }
    $units = @('B', 'KB', 'MB', 'GB', 'TB')
    $i = 0
    while ($b -ge 1024 -and $i -lt ($units.Count - 1)) {
        $b = $b / 1024.0
        $i++
    }
    # Bytes render as a whole number; KB+ get 1-2 decimals so small drift is visible.
    if ($i -eq 0) {
        return "$sign$([long]$b) B"
    }
    $digits = if ($b -lt 10) { 2 } else { 1 }
    $rounded = [math]::Round($b, $digits)
    $fmt = "0.$('0' * $digits)"
    return "$sign$($rounded.ToString($fmt, [System.Globalization.CultureInfo]::InvariantCulture)) $($units[$i])"
}

function Format-SignedByteSize {
    <#
    .SYNOPSIS
        Format-ByteSize with an explicit leading '+' for non-negative values so a
        growth/shrink is unambiguous in the delta column.
    #>
    param([AllowNull()]$Bytes)
    if ($null -eq $Bytes) { return 'n/a' }
    $v = [long]$Bytes
    if ($v -ge 0) { return "+$(Format-ByteSize $v)" }
    # Format-ByteSize already prefixes negatives with '-'.
    return (Format-ByteSize $v)
}

function Get-SizeDelta {
    <#
    .SYNOPSIS
        Base-vs-head size delta with a small noise band. Status is one of:
        grew | shrank | unchanged | added | removed | na.
    .DESCRIPTION
        - added   : absent at base, present at head (new artifact).
        - removed : present at base, absent at head (dropped artifact).
        - na      : absent on both sides.
        - unchanged: present on both, |Δ| below the noise band (must clear BOTH
                     the byte floor AND the percent floor to count as a change).
        - grew/shrank: present on both, |Δ| clears the band.
        Growth is the regression direction, so Improved is $true only for shrank.
    #>
    param(
        [AllowNull()]$BaseBytes,
        [AllowNull()]$HeadBytes,
        [long]$NoiseFloorBytes = $script:BuildMetricsNoiseFloorBytes,
        [double]$NoiseFloorPct = $script:BuildMetricsNoiseFloorPct
    )
    $hasBase = $null -ne $BaseBytes
    $hasHead = $null -ne $HeadBytes

    if (-not $hasBase -and -not $hasHead) {
        return [pscustomobject]@{ Status = 'na'; DeltaBytes = $null; DeltaPct = $null; Improved = $null }
    }
    if (-not $hasBase -and $hasHead) {
        return [pscustomobject]@{ Status = 'added'; DeltaBytes = [long]$HeadBytes; DeltaPct = $null; Improved = $false }
    }
    if ($hasBase -and -not $hasHead) {
        return [pscustomobject]@{ Status = 'removed'; DeltaBytes = -([long]$BaseBytes); DeltaPct = $null; Improved = $true }
    }

    $b = [long]$BaseBytes
    $h = [long]$HeadBytes
    $delta = $h - $b
    $pct = if ($b -ne 0) { ($delta / [double]$b) * 100.0 } else { $null }

    $clearsBytes = [math]::Abs($delta) -ge $NoiseFloorBytes
    $clearsPct = ($null -eq $pct) -or ([math]::Abs($pct) -ge $NoiseFloorPct)
    $significant = $clearsBytes -and $clearsPct

    if (-not $significant) {
        return [pscustomobject]@{
            Status     = 'unchanged'
            DeltaBytes = $delta
            DeltaPct   = if ($null -ne $pct) { [math]::Round($pct, 2) } else { $null }
            Improved   = $null
        }
    }

    $status = if ($delta -gt 0) { 'grew' } else { 'shrank' }
    return [pscustomobject]@{
        Status     = $status
        DeltaBytes = $delta
        DeltaPct   = if ($null -ne $pct) { [math]::Round($pct, 2) } else { $null }
        Improved   = ($delta -lt 0)
    }
}

function Get-SizeStatusGlyph {
    <#
    .SYNOPSIS
        Short status glyph for a delta status (growth is the regression).
    #>
    param([string]$Status)
    switch ($Status) {
        'grew'      { return [char]0x26A0 + [char]0xFE0F }  # warning sign
        'shrank'    { return [char]0x2705 }                 # check mark
        'unchanged' { return [char]0x2248 }                 # almost-equal-to
        'added'     { return [char]::ConvertFromUtf32(0x1F195) }                # NEW button
        'removed'   { return [char]::ConvertFromUtf32(0x1F5D1) + [char]0xFE0F } # wastebasket
        default     { return [char]0x2014 }                 # em dash
    }
}

function Format-SizeDeltaCell {
    <#
    .SYNOPSIS
        Render the "Δ" cell: signed size plus percent, or a word for add/remove/na.
    #>
    param([pscustomobject]$Delta)
    switch ($Delta.Status) {
        'na'      { return [char]0x2014 }
        'added'   { return "$(Format-SignedByteSize $Delta.DeltaBytes) <sub>new</sub>" }
        'removed' { return "$(Format-SignedByteSize $Delta.DeltaBytes) <sub>removed</sub>" }
        default {
            $size = Format-SignedByteSize $Delta.DeltaBytes
            if ($null -ne $Delta.DeltaPct) {
                $pct = ('{0:+0.00;-0.00;0.00}' -f $Delta.DeltaPct)
                return "$size ($pct%)"
            }
            return $size
        }
    }
}

function ConvertTo-MeasurementMap {
    <#
    .SYNOPSIS
        Index an array of measurement objects by Key into an ordered hashtable.
        Later entries win, matching a re-measure overwrite.
    #>
    param([AllowNull()][object[]]$Measurements)
    $map = [ordered]@{}
    if ($null -eq $Measurements) { return $map }
    foreach ($m in $Measurements) {
        if ($null -eq $m) { continue }
        $map[[string]$m.Key] = $m
    }
    return $map
}

function Format-BuildMetricsComment {
    <#
    .SYNOPSIS
        Render the full sticky build-metrics comment from base + head measurements.
    .PARAMETER BaseMeasurements / HeadMeasurements
        Arrays of measurement pscustomobjects (Key/Label/Group/Bytes). The head
        set drives row order and labels; base supplies the comparison column.
    .PARAMETER HeadSha / BaseSha
        Commit SHAs for the header (short-formatted here).
    .PARAMETER RunUrl
        Optional workflow-run URL for the footer.
    .PARAMETER Failed
        When set, emit a short failure notice instead of the tables (so the poster
        always has a body to write).
    .PARAMETER BaselineUnavailable
        When set, the base branch measurement is missing (e.g. its build failed),
        so render the PR's absolute sizes with no delta column meaning instead of
        mislabelling every artifact as newly "added".
    #>
    param(
        [AllowNull()][object[]]$BaseMeasurements,
        [AllowNull()][object[]]$HeadMeasurements,
        [string]$HeadSha = '',
        [string]$BaseSha = '',
        [string]$RunUrl = '',
        [switch]$Failed,
        [switch]$BaselineUnavailable
    )
    $marker = $script:BuildMetricsCommentMarker
    $headShort = if ($HeadSha) { $HeadSha.Substring(0, [math]::Min(7, $HeadSha.Length)) } else { 'unknown' }
    $baseShort = if ($BaseSha) { $BaseSha.Substring(0, [math]::Min(7, $BaseSha.Length)) } else { '' }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add($marker)
    $lines.Add('## ' + [char]::ConvertFromUtf32(0x1F4E6) + ' Build metrics')  # package emoji
    $lines.Add('')

    if ($Failed) {
        $lines.Add('> [!CAUTION]')
        $suffix = if ($RunUrl) { " See the [workflow run]($RunUrl)." } else { '' }
        $lines.Add("> The build-metrics run did not produce artifact sizes (build failed).$suffix")
        $lines.Add('')
        return ($lines -join "`n")
    }

    $baseSuffix = if ($baseShort) { " (``$baseShort``)" } else { '' }
    $lines.Add("Artifact sizes for ``$headShort`` vs the base branch$baseSuffix.")
    $lines.Add('')

    if ($BaselineUnavailable) {
        $lines.Add('> [!WARNING]')
        $lines.Add('> The base branch build did not produce sizes, so no delta is available — showing the PR''s absolute sizes only.')
        $lines.Add('')
        # Clear the base measurements (the base column still renders, as n/a) so
        # present-head rows show "—" instead of misclassifying as newly "added".
        $BaseMeasurements = $null
    }

    $baseMap = ConvertTo-MeasurementMap -Measurements $BaseMeasurements
    $headMap = ConvertTo-MeasurementMap -Measurements $HeadMeasurements

    # Row order: head measurements first (source of truth for the PR), then any
    # base-only keys (removed artifacts) appended so a drop is never hidden.
    $orderedKeys = [System.Collections.Generic.List[string]]::new()
    foreach ($k in $headMap.Keys) { $orderedKeys.Add([string]$k) }
    foreach ($k in $baseMap.Keys) { if (-not $headMap.Contains($k)) { $orderedKeys.Add([string]$k) } }

    if ($orderedKeys.Count -eq 0) {
        $lines.Add('_No tracked artifacts were measured._')
        $lines.Add('')
        return ($lines -join "`n")
    }

    # Group rows under their Group header, preserving first-seen group order.
    $groupOrder = [System.Collections.Generic.List[string]]::new()
    $rowsByGroup = @{}
    $anyChange = $false
    foreach ($key in $orderedKeys) {
        $headM = if ($headMap.Contains($key)) { $headMap[$key] } else { $null }
        $baseM = if ($baseMap.Contains($key)) { $baseMap[$key] } else { $null }
        $ref = if ($headM) { $headM } else { $baseM }
        $label = [string]$ref.Label
        $group = if ($ref.PSObject.Properties['Group'] -and $ref.Group) { [string]$ref.Group } else { 'Artifacts' }
        $baseBytes = if ($baseM) { $baseM.Bytes } else { $null }
        $headBytes = if ($headM) { $headM.Bytes } else { $null }

        $delta = if ($BaselineUnavailable) {
            [pscustomobject]@{ Status = 'na'; DeltaBytes = $null; DeltaPct = $null; Improved = $null }
        } else {
            Get-SizeDelta -BaseBytes $baseBytes -HeadBytes $headBytes
        }
        if ($delta.Status -in @('grew', 'shrank', 'added', 'removed')) { $anyChange = $true }

        $row = '| {0} | {1} | {2} | {3} | {4} |' -f `
            $label, `
        (Format-ByteSize $baseBytes), `
        (Format-ByteSize $headBytes), `
        (Format-SizeDeltaCell $delta), `
        (Get-SizeStatusGlyph $delta.Status)

        if (-not $rowsByGroup.ContainsKey($group)) {
            $rowsByGroup[$group] = [System.Collections.Generic.List[string]]::new()
            $groupOrder.Add($group)
        }
        $rowsByGroup[$group].Add($row)
    }

    foreach ($group in $groupOrder) {
        $lines.Add("### $group")
        $lines.Add('')
        $lines.Add('| Artifact | base | PR | Δ | |')
        $lines.Add('|---|--:|--:|--:|:-:|')
        foreach ($row in $rowsByGroup[$group]) { $lines.Add($row) }
        $lines.Add('')
    }

    if (-not $anyChange -and -not $BaselineUnavailable) {
        $lines.Add('No size change beyond the noise floor. ' + [char]0x2705)
        $lines.Add('')
    }

    $legend = [char]0x2705 + ' smaller / ' + [char]0x26A0 + [char]0xFE0F + ' larger / ' +
        [char]0x2248 + ' within noise'
    $lines.Add("<sub>$legend. Sizes come from a Release <code>dotnet pack</code> on the CI runner: packages are the compressed <code>.nupkg</code> download size, assemblies the uncompressed DLL inside it.")
    if ($RunUrl) {
        $lines.Add("<a href=`"$RunUrl`">workflow run</a>.</sub>")
    } else {
        $lines.Add('</sub>')
    }

    return ($lines -join "`n")
}
