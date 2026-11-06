<#
.SYNOPSIS
    Merge cobertura reports into one whose covered lines are the exact union of the
    inputs' covered lines.

.DESCRIPTION
    Use this instead of `dotnet-coverage merge <reports> --output <out> --output-format
    cobertura`. That command drops covered lines from these reports, because it matches
    lines through their method and the unit and selftest reports name methods
    differently. Merge-CoberturaDocuments in CoverageLib.ps1 documents the full rules.

    Fails when the inputs contain no lines at all, so an empty measurement cannot be
    scored as full coverage.

    Merge a unit and a selftest report:
      pwsh tests/coverage/ci/Merge-Cobertura.ps1 -OutFile merged.cobertura.xml unit.cobertura.xml selftest.cobertura.xml
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory)][string]$OutFile,
    [Parameter(Mandatory, ValueFromRemainingArguments)][string[]]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'CoverageLib.ps1')

$docs = foreach ($p in $Path) {
    $doc = [System.Xml.XmlDocument]::new()
    $doc.Load((Resolve-Path -LiteralPath $p).Path)
    $doc
}
$merged = Merge-CoberturaDocuments -Documents $docs
$root = $merged.DocumentElement
if ($root.GetAttribute('lines-valid') -eq '0') {
    throw "None of the $(@($Path).Count) input report(s) lists a single line, so there is no coverage to merge."
}

$outFull = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutFile)
$outParent = Split-Path -Parent $outFull
if ($outParent -and -not (Test-Path -LiteralPath $outParent)) {
    New-Item -ItemType Directory -Force -Path $outParent | Out-Null
}
$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.IndentChars = '  '
$settings.NewLineChars = "`n"
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)
$writer = [System.Xml.XmlWriter]::Create($outFull, $settings)
try { $merged.Save($writer) } finally { $writer.Dispose() }

Write-Host ("Merged {0} report(s): {1}/{2} lines covered, {3}/{4} branches covered -> {5}" -f
    @($Path).Count, $root.GetAttribute('lines-covered'), $root.GetAttribute('lines-valid'),
    $root.GetAttribute('branches-covered'), $root.GetAttribute('branches-valid'), $outFull)
