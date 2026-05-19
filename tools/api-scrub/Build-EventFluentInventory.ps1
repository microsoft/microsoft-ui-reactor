#!/usr/bin/env pwsh
# Scans Reactor element records for Action/Action<T>/EventHandler callback
# properties and emits a CSV inventory for spec 039 Phase 0.3.
#
# Output columns: Element, PropertyName, DelegateType, Source(spec §), File
#
# Source attribution is best-effort — it grep-matches the property name to
# the spec section text. Unmatched rows ship with "?" and should be
# spot-checked by hand.
#
# Usage:
#   pwsh tools/api-scrub/Build-EventFluentInventory.ps1
#
# Writes:
#   tests/Reactor.SelfTests/Fixtures/event-fluent-inventory.csv

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')),
    [string]$ElementFile = 'src/Reactor/Core/Element.cs',
    [string]$ControlsDir = 'src/Reactor/Controls',
    [string]$SpecFile = 'docs/specs/039-property-and-event-scrub.md',
    [string]$OutputCsv = 'tests/Reactor.SelfTests/Fixtures/event-fluent-inventory.csv'
)

$ErrorActionPreference = 'Stop'

function Get-SectionForProperty {
    param([string]$PropertyName, [string]$ElementName, [string]$SpecText)
    # Search for the most-specific section that mentions this property AND element.
    # Falls back to property-only match.
    $sections = [regex]::Matches($SpecText, '(?ms)^(### \d+\.\d+|## §\d+)\s*(.*?)(?=^### \d|^## §|\z)')
    foreach ($s in $sections) {
        $header = $s.Groups[1].Value
        $body = $s.Groups[2].Value
        if ($body -match [regex]::Escape($PropertyName) -and $body -match [regex]::Escape($ElementName.Replace('Element',''))) {
            if ($header -match '^### (\d+\.\d+)') { return "§$($matches[1])" }
            if ($header -match '^## §(\d+)') { return "§$($matches[1])" }
        }
    }
    foreach ($s in $sections) {
        $header = $s.Groups[1].Value
        $body = $s.Groups[2].Value
        if ($body -match [regex]::Escape($PropertyName)) {
            if ($header -match '^### (\d+\.\d+)') { return "§$($matches[1])" }
            if ($header -match '^## §(\d+)') { return "§$($matches[1])" }
        }
    }
    return '?'
}

function Parse-File {
    param([string]$Path, [string]$SpecText)

    $content = Get-Content -Raw -LiteralPath $Path
    $rows = New-Object System.Collections.Generic.List[object]

    # Match records, capturing the type name AND the body until the next record/end.
    $recordPattern = '(?ms)public\s+(?:sealed\s+)?record\s+(\w+Element)(?:<[^>]+>)?\s*[(:][^{]*?\{(.*?)^\}'
    foreach ($m in [regex]::Matches($content, $recordPattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)) {
        $elementName = $m.Groups[1].Value
        $body = $m.Groups[2].Value

        # Find Action / Action<T...> / EventHandler<...> init properties.
        # The delegate-type subgroup supports one level of nested generics
        # (e.g. Action<IReadOnlySet<RowKey>>) via a balancing alternation.
        $propPattern = '(?m)^\s*public\s+(Action(?:<(?:[^<>]|<[^<>]*>)+>)?\??|EventHandler(?:<(?:[^<>]|<[^<>]*>)+>)?\??)\s+(\w+)\s*\{\s*get;\s*init;\s*\}'
        foreach ($pm in [regex]::Matches($body, $propPattern)) {
            $delegateType = $pm.Groups[1].Value
            $propName = $pm.Groups[2].Value
            $section = Get-SectionForProperty -PropertyName $propName -ElementName $elementName -SpecText $SpecText
            $rows.Add([pscustomobject]@{
                Element       = $elementName
                PropertyName  = $propName
                DelegateType  = $delegateType
                Source        = $section
                File          = (Resolve-Path -Relative $Path)
            })
        }
    }

    # Also scan positional record parameters for callback shapes.
    $posPattern = '(?ms)public\s+(?:sealed\s+)?record\s+(\w+Element)(?:<[^>]+>)?\s*\((.*?)\)\s*[:{]'
    foreach ($m in [regex]::Matches($content, $posPattern)) {
        $elementName = $m.Groups[1].Value
        $paramList = $m.Groups[2].Value
        # Match each parameter: TYPE NAME [= DEFAULT]
        # Allow one level of nested generics in the delegate type argument.
        $paramPattern = '(Action(?:<(?:[^<>]|<[^<>]*>)+>)?\??|EventHandler(?:<(?:[^<>]|<[^<>]*>)+>)?\??)\s+(\w+)'
        foreach ($pm in [regex]::Matches($paramList, $paramPattern)) {
            $delegateType = $pm.Groups[1].Value
            $propName = $pm.Groups[2].Value
            # Avoid double-counting if same name already captured as init prop
            $exists = $rows | Where-Object { $_.Element -eq $elementName -and $_.PropertyName -eq $propName }
            if (-not $exists) {
                $section = Get-SectionForProperty -PropertyName $propName -ElementName $elementName -SpecText $SpecText
                $rows.Add([pscustomobject]@{
                    Element       = $elementName
                    PropertyName  = $propName
                    DelegateType  = $delegateType
                    Source        = $section
                    File          = (Resolve-Path -Relative $Path)
                })
            }
        }
    }

    return $rows
}

Push-Location $RepoRoot
try {
    $specText = Get-Content -Raw -LiteralPath $SpecFile

    $allRows = New-Object System.Collections.Generic.List[object]
    foreach ($r in @(Parse-File -Path $ElementFile -SpecText $specText)) { $allRows.Add($r) }

    Get-ChildItem -Path $ControlsDir -Filter *.cs -Recurse | ForEach-Object {
        foreach ($r in @(Parse-File -Path $_.FullName -SpecText $specText)) { $allRows.Add($r) }
    }

    # Drop the universal UIElementBase event surface — these are not per-control
    # callbacks the audit cares about; they are inherited from Element itself.
    $universalNoise = @(
        'OnMountAction','OnSizeChanged','OnPointerPressed','OnPointerMoved',
        'OnPointerReleased','OnPointerEntered','OnPointerExited','OnPointerCanceled',
        'OnPointerCaptureLost','OnPointerWheelChanged','OnTapped','OnDoubleTapped',
        'OnRightTapped','OnHolding','OnKeyDown','OnKeyUp','OnPreviewKeyDown',
        'OnPreviewKeyUp','OnCharacterReceived','OnGotFocus','OnLostFocus',
        'OnAccessKeyDisplayRequested'
    )
    # Filter out the universal UIElement event surface AND non-event callbacks
    # like component-style refs (Action<TRef>? Ref) — those are not "events".
    # Anything genuinely event-shaped follows the OnX naming convention.
    $filtered = $allRows |
        Where-Object { $universalNoise -notcontains $_.PropertyName } |
        Where-Object { $_.PropertyName -like 'On*' }

    $outFull = Join-Path $RepoRoot $OutputCsv
    $outDir  = Split-Path $outFull -Parent
    if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

    $filtered |
        Sort-Object Element, PropertyName |
        Export-Csv -LiteralPath $outFull -NoTypeInformation -Encoding utf8

    Write-Host ("Wrote {0} rows to {1}" -f $filtered.Count, $OutputCsv)
}
finally {
    Pop-Location
}
